using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Media;
using Katalog.Ulohy;

namespace Katalog.Okno;

/// <summary>What the stop button of the status line stops.</summary>
internal sealed record ZastavenieUlohy(DruhUlohy Druh, long DiskId);

/// <summary>
/// Technical data of videos (stage E3): reading them in the background when a disk is connected or scanned, the
/// interrupted jobs of the queue, and showing them in the detail panel with the preview.
/// </summary>
public partial class HlavneOkno
{
    private SpravcaCitania? _citania;
    private UlozisteObrazkov? _obrazky;
    private NastrojeFfmpeg? _ffmpeg;

    /// <summary>The FFmpeg in use (the self-test checks it is KATALOG's own, verified by its manifest).</summary>
    internal NastrojeFfmpeg? Ffmpeg => _ffmpeg;

    private async Task InicializujCitanieAsync(Kniznica kniznica)
    {
        _obrazky = UlozisteObrazkov.PreKniznicu(kniznica.Cesta);
        _citania = new SpravcaCitania(kniznica, _obrazky);
        _citania.Zmena += ZobrazPriebehSkenov;
        _citania.Skoncilo += PoCitani;
        await NajdiFfmpegAsync();

        // jobs that ran when the program ended wait again; finished ones older than a month go away
        await kniznica.Zapis.ZapisAsync(s =>
        {
            UlohyKniznice.ObnovPrerusene(s);
            UlohyKniznice.Vycisti(s, Cas.Z(DateTime.UtcNow.AddDays(-30)));
        });
    }

    /// <summary>Finds FFmpeg again (after the settings changed). Checking KATALOG's own copy reads it whole, so not on the window's thread.</summary>
    private async Task NajdiFfmpegAsync()
    {
        var priecinok = _nastavenia.PriecinokFfmpeg;
        _ffmpeg = await Task.Run(() => HladanieFfmpeg.Najdi(priecinok, AppContext.BaseDirectory));
    }

    /// <summary>
    /// After the connected disks changed: a scan the program left unfinished goes on (reading follows it), other disks
    /// get their videos read.
    /// </summary>
    private async Task SpustiCakajuceUlohyAsync()
    {
        if (Kniznica is not { } kniznica || _stav is null)
        {
            return;
        }

        IReadOnlyList<Uloha> cakajuce;
        using (var c = kniznica.OtvorCitanie())
        {
            cakajuce = UlohyKniznice.Cakajuce(c);
        }

        foreach (var diskId in _stav.Pripojene.Keys.ToList())
        {
            if (_skeny?.Bezi(diskId) == true)
            {
                continue;
            }

            if (cakajuce.Any(u => u.Druh == DruhUlohy.Skenovanie && u.DiskId == diskId))
            {
                StavUloh.Text = "Dokončujem prerušený sken…";
                _ = SkenujAsync(diskId);
            }
            else
            {
                _ = CitajAsync(diskId, pouzivatel: false);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Reads the technical data of the videos of a connected disk that do not have them yet (the self-test awaits it);
    /// by itself only when the settings allow it and the user did not stop it.
    /// </summary>
    internal async Task<VysledokCitania?> CitajAsync(long diskId, bool pouzivatel)
    {
        if (Kniznica is not { } kniznica || _citania is null || _stav?.Cesta(diskId) is not { } koren ||
            (!pouzivatel && !_nastavenia.CitatTechnickeUdaje))
        {
            return null;
        }

        if (_ffmpeg is not { } nastroje)
        {
            if (pouzivatel)
            {
                MessageBox.Show(this,
                    "FFmpeg sa nenašiel – ani z UNIVERSUM, ani nainštalovaný s KATALOGOM. Vyber jeho priečinok v Nastaveniach " +
                    "alebo KATALOG nainštaluj znova.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return null;
        }

        int zostava;
        string nazov;
        bool nahlady;
        using (var c = kniznica.OtvorCitanie())
        {
            zostava = TechnickeUdaje.Pocty(c, diskId).Zostava;
            var d = KatalogyADisky.NajdiDisk(c, diskId);
            nazov = d is null ? "" : $"{d.CisloText} {d.Nazov}";
            nahlady = d?.Nahlady == true;
        }

        if (zostava == 0)
        {
            if (pouzivatel)
            {
                StavUloh.Text = $"Všetky videá disku {nazov} už technické údaje majú.";
            }

            return null;
        }

        return await _citania.SpustiAsync(diskId, nazov, koren, nastroje, nahlady, pouzivatel);
    }

    private async void CitatTechnickeUdaje_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is { } diskId)
        {
            if (_stav?.Pripojene.ContainsKey(diskId) != true)
            {
                MessageBox.Show(this, PripojDisk(diskId), Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await CitajAsync(diskId, pouzivatel: true);
        }
    }

    private void PoCitani(long diskId, VysledokCitania vysledok)
    {
        StavUloh.Text = vysledok.Stav switch
        {
            StavUlohy.Hotova => $"Technické údaje prečítané: {Format.Mnozne(vysledok.Precitane, "video", "videá", "videí")}" +
                                (vysledok.Necitatelne > 0 ? $", nečitateľné {vysledok.Necitatelne} (nájdeš ich cez poskodene:ano)" : ""),
            StavUlohy.Zrusena => "Čítanie technických údajov zastavené. Prečítané ostáva, zvyšok cez pravé tlačidlo na disku.",
            StavUlohy.CakaNaDisk => "Disk sa odpojil – čítanie technických údajov pokračuje, keď ho pripojíš.",
            _ => "Technické údaje sa nepodarilo čítať: " + (vysledok.Sprava ?? "neznáma chyba"),
        };
        if (PanelVysledkov.Visibility == Visibility.Visible)
        {
            ObnovStred();
        }
        else if (_zobrazenyDisk == diskId && ZoznamSuborov.SelectedItem is PolozkaZoznamu { JePriecinok: false } polozka && Kniznica is { } kniznica)
        {
            Disk? disk;
            using (var c = kniznica.OtvorCitanie())
            {
                disk = KatalogyADisky.NajdiDisk(c, diskId);
            }

            if (disk is not null)
            {
                ZobrazDetail(disk, polozka, CestaZobrazenehoPriecinka());
            }
        }
    }

    /// <summary>The path inside its disk of the folder shown in the middle ("" for the root).</summary>
    private string CestaZobrazenehoPriecinka()
    {
        if (Kniznica is not { } kniznica || _zobrazenyPriecinok is not { } priecinok)
        {
            return "";
        }

        using var c = kniznica.OtvorCitanie();
        return KatalogyADisky.Cesta(c, priecinok);
    }

    /// <summary>The lines of the detail panel about the technical side of a video, and its preview.</summary>
    private void PridajTechnickeUdaje(List<RiadokDetailu> riadky, PolozkaZoznamu polozka)
    {
        DetailNahlad.Source = null;
        DetailNahlad.Visibility = Visibility.Collapsed;
        if (Kniznica is not { } kniznica || polozka.JePriecinok || DruhySuborov.Urci(polozka.Pripona) is not (DruhSuboru.Video or DruhSuboru.Zvuk))
        {
            return;
        }

        TechnickeUdajeSuboru? udaje;
        using (var c = kniznica.OtvorCitanie())
        {
            udaje = TechnickeUdaje.Citaj(c, polozka.Id);
        }

        if (udaje is null)
        {
            riadky.Add(new RiadokDetailu("Technické údaje", _ffmpeg is null
                ? "Ešte neprečítané – FFmpeg sa nenašiel (Nastavenia)."
                : "Ešte neprečítané – prečítajú sa na pozadí, keď bude disk pripojený."));
            return;
        }

        var u = udaje.Udaje;
        if (u.Chyby is { } chyby)
        {
            riadky.Add(new RiadokDetailu("Súbor sa nedá prečítať", chyby));
        }

        if (u.VideoKodek is { } kodek)
        {
            var casti = new List<string> { PopisMedii.Kodek(kodek) };
            if (u.Sirka is { } s && u.Vyska is { } v)
            {
                casti.Add($"{s}×{v} ({Kvalita.Skratka(u.Trieda)})");
            }

            if (PopisMedii.Hdr(u.Hdr) is { Length: > 0 } hdr)
            {
                casti.Add(hdr);
            }

            if (u.Snimky is { } snimky)
            {
                casti.Add(snimky.ToString("0.###", Format.Slovensky) + " snímok/s");
            }

            riadky.Add(new RiadokDetailu("Obraz", string.Join(" · ", casti)));
        }

        if (u.DlzkaMs is { } dlzka)
        {
            var tok = u.DatovyTok is { } b ? " · " + (b / 1_000_000.0).ToString("0.0", Format.Slovensky) + " Mb/s" : "";
            riadky.Add(new RiadokDetailu("Dĺžka", PopisMedii.Dlzka(dlzka) + tok + (u.Kontajner is { } k ? " · " + k.ToUpperInvariant() : "")));
        }

        var zvuky = udaje.Stopy.Where(t => t.Druh == DruhStopy.Zvuk).Select(PopisStopy).ToList();
        if (zvuky.Count > 0)
        {
            riadky.Add(new RiadokDetailu(zvuky.Count == 1 ? "Zvuk" : $"Zvuk ({zvuky.Count})", string.Join("\n", zvuky)));
        }

        var titulky = udaje.Stopy.Where(t => t.Druh == DruhStopy.Titulky).Select(PopisStopy).ToList();
        riadky.Add(new RiadokDetailu(titulky.Count <= 1 ? "Titulky" : $"Titulky ({titulky.Count})", titulky.Count == 0 ? "žiadne" : string.Join("\n", titulky)));

        if (udaje.Nahlady.FirstOrDefault() is { } nahlad && _obrazky is { } obrazky)
        {
            try
            {
                var obrazok = new BitmapImage();
                obrazok.BeginInit();
                obrazok.CacheOption = BitmapCacheOption.OnLoad;
                obrazok.UriSource = new Uri(obrazky.Cesta(nahlad.Meno));
                obrazok.EndInit();
                obrazok.Freeze();
                DetailNahlad.Source = obrazok;
                DetailNahlad.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                // a missing picture leaves the detail without it
            }
        }
    }

    /// <summary>"čeština · DTS-HD MA 5.1 · predvolený" or "slovenčina · SRT · vynútené · externý súbor".</summary>
    private static string PopisStopy(Stopa stopa)
    {
        var casti = new List<string> { stopa.Jazyk is { } j ? Jazyky.Nazov(j) : "jazyk neuvedený" };
        if (PopisMedii.Kodek(stopa.Kodek) is { Length: > 0 } kodek)
        {
            casti.Add(stopa.Kanaly is { } k ? kodek + " " + PopisMedii.Kanaly(k) : kodek);
        }

        if (stopa.Predvolena)
        {
            casti.Add(stopa.Druh == DruhStopy.Zvuk ? "predvolený" : "predvolené");
        }

        if (stopa.Vynutena)
        {
            casti.Add("vynútené");
        }

        if (stopa.Sdh)
        {
            casti.Add("pre nepočujúcich");
        }

        if (stopa.ExternySubor is not null)
        {
            casti.Add("externý súbor");
        }
        else if (stopa.Nazov is { Length: > 0 } nazov)
        {
            casti.Add("„" + nazov + "“");
        }

        return string.Join(" · ", casti);
    }
}
