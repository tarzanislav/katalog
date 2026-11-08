using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Vystupy;

namespace Katalog.Okno;

/// <summary>The private web catalogue (stage E9): the library's titles and disks as encrypted static pages in a folder.</summary>
public partial class HlavneOkno
{
    /// <summary>Posters on the web are this wide (sharp on phones, small in size).</summary>
    private const int SirkaPlagatuWebu = 240;

    private void WebovyKatalog_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is null)
        {
            return;
        }

        new WebKatalogOkno(_nastavenia, (priecinok, nazov, heslo, plagaty, cesty, priebeh) => VytvorWebAsync(priecinok, nazov, heslo, plagaty, cesty, priebeh))
        {
            Owner = this,
        }.ShowDialog();
    }

    /// <summary>
    /// Makes the web catalogue: titles the user has or wants, with their versions on disks, and the disks; posters made
    /// small here (the self-test calls it with fewer key rounds).
    /// </summary>
    internal async Task<VysledokWebu> VytvorWebAsync(string priecinok, string nazov, string heslo, bool plagaty, bool cesty,
        IProgress<string>? priebeh, int iteracie = WebovyKatalog.Iteracie)
    {
        var kniznica = Kniznica ?? throw new InvalidOperationException("Knižnica nie je otvorená.");
        var kontext = KontextHladania.Teraz(_stav?.Pripojene.Keys.ToList() ?? []);
        var obrazky = _obrazky;
        priebeh?.Report("Čítam knižnicu…");
        var (tituly, disky) = await Task.Run(() =>
        {
            using var c = kniznica.OtvorCitanie();
            return (PodkladyWebu.Tituly(c, kontext), Prehlady.Disky(c));
        });

        string? Kvalita(TriedaKvality k) => k switch { TriedaKvality.UHD4K => "4K", TriedaKvality.Nezistena => null, _ => k.ToString() };
        var webTituly = tituly.Select(t => new TitulWebu(
            t.Titul.Id, t.Titul.Druh == DruhTitulu.Serial, t.Titul.Nazov, t.Titul.Original is { } o && o != t.Titul.Nazov ? o : null, t.Titul.Rok, t.Dej,
            t.Zanre, t.Rezia, t.Herci, t.Kolekcie, t.Stitky, t.Titul.Hodnotenie, t.Titul.MojeHodnotenie, t.Titul.Videne, t.Titul.Zelany, t.Titul.Mam,
            Kvalita(t.Titul.Kvalita), t.Epizod,
            t.Verzie.Select(v => new VerziaWebu(v.DiskId, cesty ? v.Cesta : "", v.Velkost, Kvalita(v.Kvalita), v.Seria, v.Cislo)).ToList(),
            plagaty && t.Titul.Plagat is { } p ? p : null)).ToList();
        var webDisky = disky.Select(r => new DiskWebu(
            r.Disk.Id, "D" + r.Disk.CisloText, r.Disk.Nazov, r.Katalog, Format.Druh(r.Disk.Druh), r.Disk.Identita.Velkost, r.Disk.Volne,
            r.Suborov, r.Tituly, r.Disk.Umiestnenie, r.Disk.PoslednySken is { } s ? Format.Datum(s) : null)).ToList();
        var obsah = new ObsahWebu(nazov, DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture), Produkt.Nazov, webTituly, webDisky);

        // posters: each once, made small on the way (only those whose file is here)
        var kluce = plagaty && obrazky is not null ? webTituly.Select(t => t.Plagat).OfType<string>().Distinct().ToList() : [];
        IEnumerable<PlagatWebu> Plagaty()
        {
            foreach (var kluc in kluce)
            {
                if (ZmensenyPlagat(obrazky!.Cesta(kluc)) is { } jpeg)
                {
                    yield return new PlagatWebu(kluc, jpeg);
                }
            }
        }

        var vysledok = await Task.Run(() => WebovyKatalog.Vytvor(priecinok, obsah, Plagaty(), heslo, priebeh, iteracie));
        StavUloh.Text = $"Webový katalóg je v priečinku {priecinok}.";
        return vysledok;
    }

    /// <summary>A poster as a small JPEG (240 pixels wide); null when its file is missing or cannot be read.</summary>
    private static byte[]? ZmensenyPlagat(string subor)
    {
        if (!File.Exists(subor))
        {
            return null;
        }

        try
        {
            var obrazok = new BitmapImage();
            obrazok.BeginInit();
            obrazok.CacheOption = BitmapCacheOption.OnLoad;
            obrazok.DecodePixelWidth = SirkaPlagatuWebu;
            obrazok.UriSource = new Uri(subor);
            obrazok.EndInit();
            obrazok.Freeze();
            var koder = new JpegBitmapEncoder { QualityLevel = 82 };
            koder.Frames.Add(BitmapFrame.Create(obrazok));
            using var pamat = new MemoryStream();
            koder.Save(pamat);
            return pamat.ToArray();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
