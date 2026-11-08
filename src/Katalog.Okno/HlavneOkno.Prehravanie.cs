using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Jadro.Prehravanie;
using Microsoft.Data.Sqlite;

namespace Katalog.Okno;

/// <summary>What the user chose to play or open: a file (by its folder and name) or a folder of a disk.</summary>
internal sealed record CielPrehrania(long DiskId, long PriecinokId, string? Subor, string Pripona)
{
    public bool JePriecinok => Subor is null;

    /// <summary>Folders (Blu-ray, DVD, a season), videos and audio play; other files open in their program.</summary>
    public bool DaSaPrehrat => JePriecinok || DruhySuborov.Urci(Pripona) is DruhSuboru.Video or DruhSuboru.Zvuk;
}

/// <summary>
/// Playing (stage E2): files, playlists and Blu-ray or DVD folders go to the chosen player. When the disk is not
/// connected, the window waits for it and starts playing as soon as it is.
/// </summary>
public partial class HlavneOkno
{
    /// <summary>What waits for its disks to be connected.</summary>
    private (IReadOnlyList<CielPrehrania> Ciele, HashSet<long> Disky)? _cakajuce;

    /// <summary>The self-test takes the prepared start instead of starting a player.</summary>
    internal Action<Spustenie>? SpustacPrehravania { get; set; }

    /// <summary>The files or folders selected in the middle of the window (in the order of the list).</summary>
    private IReadOnlyList<CielPrehrania> VybraneCiele()
    {
        if (PanelVysledkov.Visibility == Visibility.Visible)
        {
            return ZoznamVysledkov.SelectedItems.OfType<PolozkaVysledku>()
                .OrderBy(p => ZoznamVysledkov.Items.IndexOf(p))
                .Select(p => p.JePriecinok
                    ? new CielPrehrania(p.Disk.Id, p.Polozka.Id, null, "")
                    : new CielPrehrania(p.Disk.Id, p.Polozka.PriecinokId, p.Polozka.Nazov, p.Polozka.Pripona))
                .ToList();
        }

        if (PanelSuborov.Visibility == Visibility.Visible && _zobrazenyDisk is { } disk && _zobrazenyPriecinok is { } priecinok)
        {
            return ZoznamSuborov.SelectedItems.OfType<PolozkaZoznamu>()
                .OrderBy(p => ZoznamSuborov.Items.IndexOf(p))
                .Select(p => p.JePriecinok ? new CielPrehrania(disk, p.Id, null, "") : new CielPrehrania(disk, priecinok, p.Nazov, p.Pripona))
                .ToList();
        }

        return [];
    }

    private async void PrehratVybrane_Click(object sender, RoutedEventArgs e) => await PrehrajVybraneAsync();

    /// <summary>Plays what is selected in the middle of the window (the button "Prehrať"; the self-test calls it).</summary>
    internal Task<bool> PrehrajVybraneAsync() => PrehrajAsync(VybraneCiele());

    private async void PrehratZoStromu_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || VybratyUzol is not { Druh: DruhUzla.Disk or DruhUzla.Priecinok } uzol)
        {
            return;
        }

        long? priecinok = uzol.Druh == DruhUzla.Priecinok ? uzol.Id : null;
        if (priecinok is null)
        {
            using var c = kniznica.OtvorCitanie();
            priecinok = KatalogyADisky.Koren(c, uzol.Id);
        }

        if (priecinok is { } id)
        {
            await PrehrajAsync([new CielPrehrania(uzol.DiskId, id, null, "")]);
        }
    }

    /// <summary>
    /// Plays what the user chose; returns whether a player started. Files that do not play are left out; a disk that is
    /// not connected is waited for.
    /// </summary>
    internal async Task<bool> PrehrajAsync(IReadOnlyList<CielPrehrania> ciele)
    {
        var hratelne = ciele.Where(c => c.DaSaPrehrat).ToList();
        if (hratelne.Count == 0 || Kniznica is not { } kniznica || _stav is null)
        {
            return false;
        }

        var chybajuce = hratelne.Select(c => c.DiskId).Distinct().Where(d => !_stav.Pripojene.ContainsKey(d)).ToList();
        if (chybajuce.Count > 0)
        {
            CakajNaDisky(hratelne, chybajuce);
            return false;
        }

        var polozky = await Task.Run(() =>
        {
            using var c = kniznica.OtvorCitanie();
            return PolozkyPrehravania(c, hratelne);
        });
        if (polozky.Count == 0)
        {
            MessageBox.Show(this, "V priečinku nie je žiadne video.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var nedostupna = polozky.FirstOrDefault(p => p.Druh == DruhPolozkyPrehravania.Subor ? !File.Exists(p.Cesta) : !Directory.Exists(p.Cesta));
        if (nedostupna is not null)
        {
            MessageBox.Show(this,
                $"„{Path.GetFileName(nedostupna.Cesta.TrimEnd('\\'))}“ na disku nie je (bol presunutý alebo zmazaný). Aktualizuj disk.",
                Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return Spusti(polozky);
    }

    /// <summary>Full paths of what plays: a file, a disc structure, or the videos of a folder (a season, say).</summary>
    private List<PolozkaPrehravania> PolozkyPrehravania(SqliteConnection c, IReadOnlyList<CielPrehrania> ciele)
    {
        var vysledok = new List<PolozkaPrehravania>();
        foreach (var ciel in ciele)
        {
            if (_stav?.Cesta(ciel.DiskId) is not { } koren)
            {
                continue;
            }

            var priecinok = PlnaCesta(koren, KatalogyADisky.Cesta(c, ciel.PriecinokId));
            if (ciel.Subor is { } subor)
            {
                vysledok.Add(new PolozkaPrehravania(Path.Combine(priecinok, subor), DruhPolozkyPrehravania.Subor));
                continue;
            }

            // BDMV or VIDEO_TS itself: the disc is the folder above it
            if (PrikazyPrehravania.StrukturaDisku([Path.GetFileName(priecinok.TrimEnd('\\'))]) is { } druhSamotny &&
                Path.GetDirectoryName(priecinok.TrimEnd('\\')) is { } nad)
            {
                vysledok.Add(new PolozkaPrehravania(nad, druhSamotny));
                continue;
            }

            if (PrikazyPrehravania.StrukturaDisku(KatalogyADisky.Podpriecinky(c, ciel.PriecinokId).Select(p => p.Nazov)) is { } druh)
            {
                vysledok.Add(new PolozkaPrehravania(priecinok, druh));
                continue;
            }

            vysledok.AddRange(KatalogyADisky.Subory(c, ciel.PriecinokId)
                .Where(s => DruhySuborov.JeVideo(s.Pripona))
                .Select(s => new PolozkaPrehravania(Path.Combine(priecinok, s.Nazov), DruhPolozkyPrehravania.Subor)));
        }

        return vysledok;
    }

    private bool Spusti(IReadOnlyList<PolozkaPrehravania> polozky)
    {
        var prehravac = _nastavenia.SkutocnyPrehravac;
        var priecinokPlaylistov = Path.Combine(Path.GetTempPath(), Produkt.Nazov);
        var playlist = Path.Combine(priecinokPlaylistov, $"playlist-{DateTime.Now:yyyyMMdd-HHmmss}.m3u8");
        var spustenie = PrikazyPrehravania.Priprav(prehravac, polozky, playlist);
        try
        {
            if (spustenie.Playlist is { } obsah)
            {
                Directory.CreateDirectory(priecinokPlaylistov);
                UpracPlaylisty(priecinokPlaylistov);
                File.WriteAllText(playlist, obsah, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (SpustacPrehravania is { } namiesto)
            {
                namiesto(spustenie);
            }
            else
            {
                Process.Start(new ProcessStartInfo(spustenie.Program, spustenie.Argumenty) { UseShellExecute = spustenie.CezShell });
            }

            StavUloh.Text = polozky.Count == 1
                ? $"Prehrávam {Path.GetFileName(polozky[0].Cesta.TrimEnd('\\'))} ({prehravac.Nazov})"
                : $"Prehrávam {Format.Mnozne(polozky.Count, "položku", "položky", "položiek")} ({prehravac.Nazov})";
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var rada = polozky.Any(p => p.Druh != DruhPolozkyPrehravania.Subor) && !prehravac.PrehraStrukturuDisku
                ? "\n\nPriečinky Blu-ray a DVD vie prehrať VLC, MPC-HC, MPC-BE, PotPlayer alebo mpv – vyber ho v Nastaveniach."
                : "";
            MessageBox.Show(this, $"Prehrávanie sa nepodarilo spustiť ({prehravac.Nazov}): {ex.Message}{rada}", Produkt.Nazov,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Playlists are needed only while the player opens them: those older than a day go away.</summary>
    private static void UpracPlaylisty(string priecinok)
    {
        foreach (var subor in Directory.EnumerateFiles(priecinok, "playlist-*.m3u8"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(subor) < DateTime.UtcNow.AddDays(-1))
                {
                    File.Delete(subor);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a playlist a player still holds stays for the next time
            }
        }
    }

    private void CakajNaDisky(IReadOnlyList<CielPrehrania> ciele, IReadOnlyList<long> disky)
    {
        _cakajuce = (ciele, disky.ToHashSet());
        string popis;
        using (var c = Kniznica!.OtvorCitanie())
        {
            popis = string.Join(", ", disky
                .Select(id => KatalogyADisky.NajdiDisk(c, id))
                .OfType<Disk>()
                .Select(d => $"{d.CisloText} – {d.Nazov}" + (d.Umiestnenie is { Length: > 0 } u ? $" (leží: {u})" : "")));
        }

        _diskOznamu = null;
        OznamText.Text = (disky.Count == 1 ? $"Na prehratie pripoj disk {popis}." : $"Na prehratie pripoj disky {popis}.") +
                         " Prehrávanie sa spustí samo, keď ho Windows rozpozná.";
        OznamAkcia.Visibility = Visibility.Collapsed;
        OznamZavriet.Content = "Zrušiť";
        Oznam.Visibility = Visibility.Visible;
    }

    /// <summary>After a change of connected disks: plays what waited, when all its disks are here now.</summary>
    private async Task<bool> PrehrajCakajuceAsync()
    {
        if (_cakajuce is not { } cakajuce || _stav is null || !cakajuce.Disky.All(_stav.Pripojene.ContainsKey))
        {
            return false;
        }

        _cakajuce = null;
        Oznam.Visibility = Visibility.Collapsed;
        await PrehrajAsync(cakajuce.Ciele);
        return true;
    }

    /// <summary>A file that does not play opens in the program Windows has for it.</summary>
    private void OtvorSubor(CielPrehrania ciel)
    {
        if (Kniznica is not { } kniznica || ciel.Subor is not { } subor)
        {
            return;
        }

        if (_stav?.Cesta(ciel.DiskId) is not { } koren)
        {
            MessageBox.Show(this, PripojDisk(ciel.DiskId), Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string cesta;
        using (var c = kniznica.OtvorCitanie())
        {
            cesta = Path.Combine(PlnaCesta(koren, KatalogyADisky.Cesta(c, ciel.PriecinokId)), subor);
        }

        try
        {
            Process.Start(new ProcessStartInfo(cesta) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            MessageBox.Show(this, $"„{subor}“ sa nepodarilo otvoriť: {ex.Message}", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
