using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// Self-test of the installed program (Katalog.exe --samotest), run by GitHub Actions on Windows: it creates an invented
/// library, writes and searches in it, opens the window in the light and the dark theme and takes screenshots. Its log
/// (selftest.log) and pictures go to KATALOG_SELFTEST_DIR. Only invented names are used; the user's settings and library
/// are never touched.
/// </summary>
internal static class Samotest
{
    private static string _vystup = "";
    private static int _chyby;

    public static async Task<int> SpustiAsync()
    {
        _vystup = Environment.GetEnvironmentVariable("KATALOG_SELFTEST_DIR") is { Length: > 0 } priecinok
            ? priecinok
            : Path.Combine(Path.GetTempPath(), Produkt.Nazov + "-samotest");
        Directory.CreateDirectory(_vystup);
        File.WriteAllText(Log, "");
        Zapis($"Samotest {Produkt.Nazov} {Produkt.VerziaText}");

        try
        {
            var cesta = Path.Combine(_vystup, "Samotest" + Produkt.PriponaKniznice);
            foreach (var subor in new[] { cesta, cesta + "-wal", cesta + "-shm" })
            {
                File.Delete(subor);
            }

            await KniznicaAsync(cesta);
            await OknoAsync(cesta);
        }
        catch (Exception ex)
        {
            Chyba("neočakávaná výnimka: " + ex);
        }

        Zapis(_chyby == 0 ? "Samotest OK" : $"Samotest: {_chyby} chýb");
        return _chyby == 0 ? 0 : 1;
    }

    private static string Log => Path.Combine(_vystup, "selftest.log");

    private static async Task KniznicaAsync(string cesta)
    {
        await using var kniznica = await Kniznica.OtvorAsync(cesta, new ZalohyKniznice(Path.Combine(_vystup, "Zalohy")));
        Over(kniznica.VerziaSchemy == Migracie.Posledna, $"knižnica má schému v{Migracie.Posledna}");
        Over(kniznica.Kontrola.JeVPoriadku, "kontrola integrity");

        await kniznica.Zapis.ZapisAsync(s =>
        {
            using var prikaz = s.CreateCommand();
            prikaz.CommandText = """
                INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Vymyslená videotéka', 0);
                INSERT INTO disk(id, katalog_id, cislo, nazov, pridany) VALUES (1, 1, 1, 'Vymyslený disk 001', 0);
                INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (1, 1, NULL, ''), (2, 1, 1, 'MOVIES');
                INSERT INTO subor(id, priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, prvy_raz)
                  VALUES (1, 2, 1, 'Vymyslený Šťastný film (2024) - CZ EN FHD H264 SMALL.mkv', 'mkv', 1000, 0, 0);
                INSERT INTO titul(id, druh, nazov, rok, vytvoreny, zmeneny) VALUES (1, 1, 'Vymyslený Šťastný film', 2024, 0, 0);
                INSERT INTO verzia(id, titul_id, potvrdena, vytvorena) VALUES (1, 1, 1, 0);
                INSERT INTO verzia_subor(verzia_id, subor_id) VALUES (1, 1);
                """;
            prikaz.ExecuteNonQuery();
        });

        using var citanie = await kniznica.OtvorCitanieAsync();
        var najdene = Fulltext.Najdi(citanie, "stastny film");
        Over(najdene.Contains((DruhHladania.Subor, 1L)) && najdene.Contains((DruhHladania.Titul, 1L)), "hľadanie bez diakritiky nájde súbor aj titul");

        var zalohy = new ZalohyKniznice(Path.Combine(_vystup, "Zalohy"));
        var zaloha = zalohy.Vytvor(citanie, kniznica.VerziaSchemy);
        Over(File.Exists(zaloha) && Path.GetFileName(zaloha).StartsWith(Produkt.Nazov + "-zaloha-0001-", StringComparison.Ordinal), "číslovaná záloha");
    }

    private static async Task OknoAsync(string cesta)
    {
        var nastavenia = Nastavenia.Nacitaj(Path.Combine(_vystup, "nastavenia"));
        // the self-test never asks the internet
        nastavenia.AutomatickeAktualizacie = false;
        var okno = new HlavneOkno(nastavenia, cesta);
        okno.Show();
        await okno.Pripravene;
        Over(okno.Kniznica is { Kontrola.JeVPoriadku: true }, "okno otvorilo knižnicu");
        Over(okno.Title == Produkt.Nazov, "názov okna");

        App.NastavMotiv("Light");
        await SnimkaAsync(okno, "hlavne-okno-svetle.png");
        App.NastavMotiv("Dark");
        await SnimkaAsync(okno, "hlavne-okno-tmave.png");

        var oPrograme = new OProgrameOkno { Owner = okno };
        oPrograme.Show();
        await SnimkaAsync(oPrograme, "o-programe-tmave.png");
        oPrograme.Close();

        App.NastavMotiv("System");
        okno.Close();
        Over(okno.Kniznica is null, "okno pri zatvorení zavrelo knižnicu");
    }

    private static async Task SnimkaAsync(Window okno, string nazov)
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        if (okno.Content is not FrameworkElement obsah || obsah.ActualWidth < 1)
        {
            Chyba($"snímka {nazov}: nič sa nevykreslilo");
            return;
        }

        var sirka = obsah.ActualWidth + obsah.Margin.Left + obsah.Margin.Right;
        var vyska = obsah.ActualHeight + obsah.Margin.Top + obsah.Margin.Bottom;
        var dpi = VisualTreeHelper.GetDpi(obsah);
        var obrazok = new RenderTargetBitmap(
            (int)(sirka * dpi.DpiScaleX), (int)(vyska * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var pozadie = new DrawingVisual();
        using (var kreslenie = pozadie.RenderOpen())
        {
            kreslenie.DrawRectangle(Pozadie(okno), null, new Rect(0, 0, sirka, vyska));
        }

        obrazok.Render(pozadie);
        obrazok.Render(obsah);
        var koder = new PngBitmapEncoder();
        koder.Frames.Add(BitmapFrame.Create(obrazok));
        await using var prud = File.Create(Path.Combine(_vystup, nazov));
        koder.Save(prud);
        Zapis("snímka " + nazov);
    }

    /// <summary>
    /// Background of a screenshot: with the Fluent theme a window is transparent (Windows draws the Mica backdrop behind it),
    /// so the picture gets the theme's own application background instead.
    /// </summary>
    private static Brush Pozadie(Window okno)
    {
        if (okno.Background is SolidColorBrush { Color.A: 255 } plna)
        {
            return plna;
        }

        if (okno.TryFindResource("ApplicationBackgroundBrush") is SolidColorBrush { Color.A: 255 } tema)
        {
            return tema;
        }

        return Application.Current.ThemeMode == ThemeMode.Dark ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) : Brushes.White;
    }

    private static void Over(bool podmienka, string co)
    {
        if (podmienka)
        {
            Zapis("OK " + co);
        }
        else
        {
            Chyba(co);
        }
    }

    private static void Chyba(string co)
    {
        _chyby++;
        Zapis(" ERROR " + co);
    }

    private static void Zapis(string riadok) =>
        File.AppendAllText(Log, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + riadok + Environment.NewLine);
}
