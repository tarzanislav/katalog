using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Jadro.Zdravie;

namespace Katalog.Okno;

/// <summary>A disk as a row of the overview.</summary>
internal sealed record RiadokPrehladuOkna(RiadokPrehladu Riadok, bool Pripojeny, bool Zvyrazneny)
{
    public string Nazov => $"{Riadok.Disk.CisloText}  {Riadok.Disk.Nazov}";

    public FontWeight Vaha => Zvyrazneny ? FontWeights.Bold : FontWeights.Normal;

    public string Katalog => Riadok.Katalog;

    /// <summary>Used share of the disk (0 – 1) at its last scan; 0 when its size is not known.</summary>
    public double Obsadene => Riadok.Disk.Identita.Velkost is > 0 and var v && Riadok.Disk.Volne is { } volne ? Math.Clamp(1 - ((double)volne / v), 0, 1) : 0;

    public string MiestoText => Riadok.Disk.Identita.Velkost is > 0 and var v
        ? $"voľné {Format.Velkost(Riadok.Disk.Volne ?? 0)} z {Format.Velkost(v)}"
        : Riadok.Disk.Volne is { } volne ? $"voľné {Format.Velkost(volne)}" : "neznáme";

    public string SuboryText => Format.Pocet(Riadok.Suborov);

    public string TitulyText => Riadok.Tituly > 0 ? Format.Pocet(Riadok.Tituly) : "";

    public string LenTuText => Riadok.LenTu > 0 ? Format.Pocet(Riadok.LenTu) : "";

    public string SkenText => Riadok.Disk.PoslednySken is { } s ? Format.Cas(s) : "ešte nie";

    public string ZdravieText => Riadok.Zdravie is not { } z ? ""
        : (z.Stav switch
        {
            StavZdravia.Dobry => "v poriadku",
            StavZdravia.Varovanie => "⚠ varovanie",
            StavZdravia.Zly => "✖ hrozí zlyhanie",
            _ => "nezistené",
        }) + (z.Teplota is { } t ? $" · {t} °C" : "") + (z.Hodiny is { } h ? $" · {Format.Pocet(h)} h" : "");
}

/// <summary>A section of the statistics: its bars.</summary>
internal sealed record SekciaStatistik(string Nadpis, IReadOnlyList<StlpecStatistiky> Riadky);

/// <summary>One bar of the statistics, its width relative to the biggest of its section.</summary>
internal sealed record StlpecStatistiky(string Nazov, long Pocet, double Sirka)
{
    public string PocetText => Format.Pocet(Pocet);
}

/// <summary>
/// Overviews (stage E5): the disks with their space, contents and health, where new files fit (N2), reading the health of
/// disks with the helper (it alone gets administrator rights, for the moment of reading), and statistics (N5).
/// </summary>
public partial class HlavneOkno
{
    private IReadOnlyList<RiadokPrehladu> _prehlad = [];

    /// <summary>The self-test's stand-in for the elevated helper: gets the output file, returns the exit code.</summary>
    internal Func<string, Task<int>>? SpustacZdravia { get; set; }

    /// <summary>The rows of the overview shown now (the self-test reads them).</summary>
    internal IReadOnlyList<RiadokPrehladu> Prehlad => _prehlad;

    /// <summary>The overview of disks (the self-test awaits it).</summary>
    internal async Task ZobrazPrehladAsync(IReadOnlySet<long>? zvyraznit = null)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        UkazStred(PanelPrehladu);
        _zobrazenyDisk = null;
        _zobrazenyPriecinok = null;
        try
        {
            _prehlad = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return Prehlady.Disky(c);
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        ZoznamPrehladu.ItemsSource = _prehlad.Select(r => new RiadokPrehladuOkna(r, pripojene.Contains(r.Disk.Id), zvyraznit?.Contains(r.Disk.Id) == true)).ToList();
        var obsadene = _prehlad.Sum(r => r.VelkostSuborov);
        var volne = _prehlad.Sum(r => r.Disk.Volne ?? 0);
        PocetPrehladu.Text = $"{Format.Mnozne(_prehlad.Count, "disk", "disky", "diskov")} · súbory {Format.Velkost(obsadene)} · voľné {Format.Velkost(volne)}";
        var ohrozene = _prehlad.Where(r => r.Zdravie?.Stav is StavZdravia.Varovanie or StavZdravia.Zly).ToList();
        if (zvyraznit is null)
        {
            VysledokPrehladu.Text = ohrozene.Count > 0
                ? "Pozor: " + string.Join(", ", ohrozene.Select(r => $"{r.Disk.CisloText} {r.Disk.Nazov}")) +
                  " – zdravie disku hlási problém. Tituly „Len tu“ by si s ním stratil."
                : "";
            VysledokPrehladu.Visibility = VysledokPrehladu.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        ZobrazDetail(null);
    }

    private void ZoznamPrehladu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoznamPrehladu.SelectedItem is RiadokPrehladuOkna riadok)
        {
            ZobrazDetail(riadok.Riadok.Disk);
            if (Kniznica is { } kniznica && riadok.Riadok.Zdravie is not null)
            {
                using var c = kniznica.OtvorCitanie();
                var historia = Prehlady.HistoriaZdravia(c, riadok.Riadok.Disk.Id, 10);
                var riadky = (DetailRiadky.ItemsSource as IEnumerable<RiadokDetailu> ?? []).ToList();
                riadky.Add(new RiadokDetailu("Zdravie (posledné merania)", string.Join("\n", historia.Select(z =>
                    $"{Format.Cas(z.Kedy)}: {z.Stav switch { StavZdravia.Dobry => "v poriadku", StavZdravia.Varovanie => "varovanie", StavZdravia.Zly => "hrozí zlyhanie", _ => "nezistené" }}" +
                    (z.Teplota is { } t ? $", {t} °C" : "") + (z.Hodiny is { } h ? $", {Format.Pocet(h)} h" : "") +
                    (z.Realokovane is > 0 ? $", presunuté sektory {z.Realokovane}" : "") + (z.Opotrebenie is { } o ? $", opotrebenie {o} %" : "")))));
                riadky.Add(new RiadokDetailu("Len na tomto disku", Format.Mnozne(riadok.Riadok.LenTu, "titul", "tituly", "titulov")));
                DetailRiadky.ItemsSource = riadky;
            }
        }
    }

    private async void KamSaZmesti_Click(object sender, RoutedEventArgs e)
    {
        if (CitajVelkost(PotrebneMiesto.Text) is not { } potrebne)
        {
            VysledokPrehladu.Text = "Napíš veľkosť, napr. 50 GB, 700 MB alebo 1,5 TB.";
            VysledokPrehladu.Visibility = Visibility.Visible;
            return;
        }

        await ZobrazPrehladAsync();
        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var kam = Prehlady.KamSaZmesti(_prehlad, potrebne, pripojene);
        VysledokPrehladu.Text = kam.Count == 0
            ? $"{Format.Velkost(potrebne)} sa nezmestí na žiadny disk (podľa voľného miesta pri poslednom skene)."
            : $"{Format.Velkost(potrebne)} sa zmestí na: " + string.Join(", ", kam.Take(5).Select(r =>
                $"{r.Disk.CisloText} {r.Disk.Nazov} (zostane {Format.Velkost(r.Disk.Volne!.Value - potrebne)}{(pripojene.Contains(r.Disk.Id) ? ", pripojený" : "")})")) +
              ". Najtesnejšie miesto je prvé – veľké voľné disky ostanú pre veľké veci.";
        VysledokPrehladu.Visibility = Visibility.Visible;
        await ZobrazPrehladAsync(kam.Take(5).Select(r => r.Disk.Id).ToHashSet());
    }

    /// <summary>"50 GB", "700 MB", "1,5 TB", "50" (GB) – in bytes, by 1024 as Windows counts sizes.</summary>
    internal static long? CitajVelkost(string text)
    {
        var t = text.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace(',', '.');
        var (cislo, nasobok) = t switch
        {
            _ when t.EndsWith("TB", StringComparison.Ordinal) => (t[..^2], 1L << 40),
            _ when t.EndsWith("GB", StringComparison.Ordinal) => (t[..^2], 1L << 30),
            _ when t.EndsWith("MB", StringComparison.Ordinal) => (t[..^2], 1L << 20),
            _ when t.EndsWith('T') => (t[..^1], 1L << 40),
            _ when t.EndsWith('G') => (t[..^1], 1L << 30),
            _ when t.EndsWith('M') => (t[..^1], 1L << 20),
            _ => (t, 1L << 30),
        };
        return double.TryParse(cislo, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var x) && x > 0 && x < 1e6
            ? (long)(x * nasobok)
            : null;
    }

    private async void ZistitZdravie_Click(object sender, RoutedEventArgs e) => await ZistiZdravieAsync();

    /// <summary>
    /// Reads the health of the disks with the helper Katalog.Zdravie: Windows asks for administrator rights for it alone;
    /// it writes only numbers to a new file, which this program reads and deletes. Returns how many disks of the library
    /// got a reading (the self-test awaits it).
    /// </summary>
    internal async Task<int> ZistiZdravieAsync()
    {
        if (Kniznica is not { } kniznica)
        {
            return 0;
        }

        var vystup = Path.Combine(Path.GetTempPath(), $"{Produkt.Nazov}-zdravie-{Guid.NewGuid():N}.json");
        TlacidloZdravie.IsEnabled = false;
        StavUloh.Text = "Čítam zdravie diskov…";
        try
        {
            int kod;
            if (SpustacZdravia is { } nahradny)
            {
                kod = await nahradny(vystup);
            }
            else
            {
                var pomocnik = Path.Combine(AppContext.BaseDirectory, "Katalog.Zdravie.exe");
                if (!File.Exists(pomocnik))
                {
                    MessageBox.Show(this, "Chýba program Katalog.Zdravie.exe – nainštaluj KATALOG znova.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 0;
                }

                using var proces = Process.Start(new ProcessStartInfo(pomocnik)
                {
                    ArgumentList = { "--json", "--vystup", vystup },
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                if (proces is null)
                {
                    return 0;
                }

                await proces.WaitForExitAsync();
                kod = proces.ExitCode;
            }

            if (kod != 0 || !File.Exists(vystup))
            {
                StavUloh.Text = $"Zdravie diskov sa neprečítalo (kód {kod}).";
                return 0;
            }

            var precitane = JsonSerializer.Deserialize<VystupZdravia>(await File.ReadAllBytesAsync(vystup));
            if (precitane is null)
            {
                return 0;
            }

            var priradene = await kniznica.Zapis.ZapisAsync(s =>
            {
                var teraz = Cas.Teraz();
                var zhoda = Prehlady.Prirad(KatalogyADisky.Disky(s), precitane.Disky);
                foreach (var (diskId, zdravie) in zhoda)
                {
                    Prehlady.ZapisZdravie(s, diskId, Prehlady.ZoVysledku(zdravie, teraz), JsonSerializer.Serialize(zdravie));
                }

                return zhoda;
            });
            var problemy = priradene.Where(p => p.Zdravie.Stav >= Smart.Varovanie).ToList();
            var bezSmart = precitane.Disky.Count(d => d.Chyba is not null);
            StavUloh.Text = $"Zdravie: prečítané {Format.Mnozne(precitane.Disky.Count, "fyzický disk", "fyzické disky", "fyzických diskov")}, " +
                            $"z knižnice {Format.Mnozne(priradene.Select(p => p.DiskId).Distinct().Count(), "disk", "disky", "diskov")}" +
                            (bezSmart > 0 ? $"; {bezSmart} bez SMART (napr. USB rámček)" : "") + ".";
            await ZobrazPrehladAsync();
            if (problemy.Count > 0)
            {
                VysledokPrehladu.Text = "Pozor: " + string.Join("; ", problemy.Select(p => $"{p.Zdravie.Model ?? "disk " + p.Zdravie.Cislo}: {p.Zdravie.Dovod}")) +
                                        ". Tituly „Len tu“ si zálohuj na iný disk.";
                VysledokPrehladu.Visibility = Visibility.Visible;
            }

            return priradene.Count;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StavUloh.Text = "Bez povolenia Windows sa zdravie diskov nedá prečítať.";
            return 0;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or JsonException or UnauthorizedAccessException)
        {
            StavUloh.Text = "Zdravie diskov sa neprečítalo: " + ex.Message;
            return 0;
        }
        finally
        {
            TlacidloZdravie.IsEnabled = true;
            try
            {
                File.Delete(vystup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The statistics of the library (the self-test awaits it).</summary>
    internal async Task ZobrazStatistikyAsync()
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        UkazStred(PanelStatistik);
        _zobrazenyDisk = null;
        _zobrazenyPriecinok = null;
        ZobrazDetail(null);
        Statistiky st;
        try
        {
            st = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return Prehlady.Spocitaj(c);
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        SuhrnStatistik.Text = $"{Format.Mnozne(st.Filmy, "film", "filmy", "filmov")}, {Format.Mnozne(st.Serialy, "seriál", "seriály", "seriálov")} " +
                              $"({Format.Mnozne(st.Epizody, "epizóda", "epizódy", "epizód")}) – spolu {st.Hodiny.ToString("N0", Format.Slovensky)} hodín sledovania.\n" +
                              $"{Format.Mnozne(st.Suborov, "súbor", "súbory", "súborov")} ({Format.Velkost(st.Velkost)}) na {Format.Mnozne(st.Diskov, "disku", "diskoch", "diskoch")}.";
        static SekciaStatistik Sekcia(string nadpis, IReadOnlyList<PolozkaStatistiky> polozky)
        {
            var najviac = Math.Max(1, polozky.Count == 0 ? 1 : polozky.Max(p => p.Pocet));
            return new SekciaStatistik(nadpis, polozky.Select(p => new StlpecStatistiky(p.Nazov, p.Pocet, Math.Max(2, 320.0 * p.Pocet / najviac))).ToList());
        }

        SekcieStatistik.ItemsSource = new[]
        {
            Sekcia("Kvalita (najlepšia verzia titulu)", st.Kvalita),
            Sekcia("Desaťročia", st.Desatrocia),
            Sekcia("Najčastejšie žánre", st.Zanre),
            Sekcia("Tituly na diskoch", st.Disky),
        }.Where(s => s.Riadky.Count > 0).ToList();
    }
}
