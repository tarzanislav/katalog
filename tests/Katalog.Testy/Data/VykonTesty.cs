using System.Diagnostics;
using System.Globalization;
using Katalog.Data;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

/// <summary>
/// Speed targets of the design (section "Výkonové ciele") measured on an invented library of 1 million files. Runs only
/// with KATALOG_VYKON=1 (in GitHub Actions); the results appear as notices of the run.
/// </summary>
public sealed class VykonTesty
{
    private const int PocetSuborov = 1_000_000;
    private const int PocetPriecinkov = 2_000;

    private static readonly string[] Slova =
        ["alfa", "beta", "gama", "delta", "omega", "kino", "noc", "deň", "more", "hora", "les", "mesto", "vlak", "cesta", "zima", "leto", "Šťastie", "údolie"];

    [Test]
    public async Task MilionSuborovAHladanie()
    {
        if (Environment.GetEnvironmentVariable("KATALOG_VYKON") != "1")
        {
            Over.Preskoc("len s KATALOG_VYKON=1");
        }

        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("Vykon.katalog");
        var kniznica = await Kniznica.OtvorAsync(cesta);
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, """
            INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Výkon', 0);
            INSERT INTO disk(id, katalog_id, cislo, nazov, pridany) VALUES (1, 1, 1, 'Vymyslený disk', 0);
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (1, 1, NULL, '');
            """));
        await kniznica.Zapis.ZapisAsync(s =>
        {
            using var prikaz = s.CreateCommand();
            prikaz.CommandText = "INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES ($id, 1, 1, $nazov)";
            var id = prikaz.Parameters.Add("$id", SqliteType.Integer);
            var nazov = prikaz.Parameters.Add("$nazov", SqliteType.Text);
            for (var i = 0; i < PocetPriecinkov; i++)
            {
                id.Value = i + 2;
                nazov.Value = string.Create(CultureInfo.InvariantCulture, $"Priečinok {i}");
                prikaz.ExecuteNonQuery();
            }
        });

        // the scan writes in batches of thousands of rows, each batch one queued write; the first 100 000 files are timed
        // on their own (design target: a first scan of 100 000 files within a minute, of which writing is a part)
        var nahoda = new Random(8112026);
        var prvych = await ZapisSuboryAsync(kniznica, nahoda, 0, 100_000);
        var ostatnych = await ZapisSuboryAsync(kniznica, nahoda, 100_000, PocetSuborov);

        double najhorsie;
        await using (var spojenie = await kniznica.OtvorCitanieAsync())
        {
            string[] dotazy = ["vlak cesta", "123456", "stastie udolie", "zima", "les 1999"];
            // first query warms the cache, as in the program after opening the library
            Fulltext.Najdi(spojenie, "kino");
            var casy = new List<double>();
            foreach (var dotaz in dotazy)
            {
                var hladanie = Stopwatch.StartNew();
                var vysledky = Fulltext.Najdi(spojenie, dotaz);
                casy.Add(hladanie.Elapsed.TotalMilliseconds);
                Over.Pravda(vysledky.Count > 0, $"„{dotaz}“ niečo nájde");
                Over.Pravda(vysledky.All(r => r.Druh == DruhHladania.Subor || r.Druh == DruhHladania.Priecinok), "len súbory a priečinky");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"hľadanie „{dotaz}“: {vysledky.Count} výsledkov za {casy[^1]:F1} ms"));
            }

            najhorsie = casy.Max();
        }

        // closing merges the WAL into the library file: its size is what the user keeps on the disk
        await kniznica.DisposeAsync();
        var velkostMb = new FileInfo(cesta).Length / 1_000_000.0;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"::notice title=Výkon (1 milión súborov)::prvých 100 000 súborov {prvych.TotalSeconds:F1} s, ďalších 900 000 {ostatnych.TotalSeconds:F1} s, hľadanie najviac {najhorsie:F1} ms, knižnica {velkostMb:F0} MB"));

        Over.Pravda(najhorsie < 100, $"hľadanie do 100 ms (bolo {najhorsie:F1} ms)");
        Over.Pravda(prvych < TimeSpan.FromSeconds(30), $"zápis 100 000 súborov do 30 s (bolo {prvych.TotalSeconds:F1} s)");
        Over.Pravda(velkostMb < 400, $"knižnica do 400 MB pri milióne súborov (bola {velkostMb:F0} MB)");
    }

    [Test]
    public async Task SkenStoTisicSuborov()
    {
        if (Environment.GetEnvironmentVariable("KATALOG_VYKON") != "1")
        {
            Over.Preskoc("len s KATALOG_VYKON=1");
        }

        using var priecinok = new DocasnyPriecinok();
        var disk = Path.Combine(priecinok.Cesta, "disk");
        for (var p = 0; p < 1000; p++)
        {
            var cesta = Path.Combine(disk, string.Create(CultureInfo.InvariantCulture, $"Priečinok {p / 100}"), string.Create(CultureInfo.InvariantCulture, $"Podpriečinok {p}"));
            Directory.CreateDirectory(cesta);
            for (var s = 0; s < 100; s++)
            {
                File.Create(Path.Combine(cesta, string.Create(CultureInfo.InvariantCulture, $"Vymyslený súbor {p}-{s}.mkv"))).Dispose();
            }
        }

        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Sken.katalog"));
        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Výkon"));
        var koren = kniznica.NovyIdPriecinka();
        var identita = new Katalog.Jadro.Rozhrania.IdentitaZvazku(Katalog.Jadro.Model.DruhDisku.Interny, null, null, null, null, null, null, disk);
        var diskId = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, "Vymyslený", identita, "", null), koren));
        var ulozisko = new Katalog.Skenovanie.LokalneUlozisko(disk, Katalog.Jadro.Model.DruhDisku.Interny, Katalog.Skenovanie.Zvazky.Systemove);

        var prvy = await Katalog.Ulohy.SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        var druhy = await Katalog.Ulohy.SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"::notice title=Výkon skenovania (100 000 súborov)::prvý sken {prvy.Trvanie.TotalSeconds:F1} s, sken bez zmien {druhy.Trvanie.TotalSeconds:F1} s"));

        Over.Rovne(100_000, prvy.Zmeny.Pridane, "prvý sken pridal všetky súbory");
        Over.Rovne(new PocetZmien(0, 0, 0), druhy.Zmeny, "sken bez zmien nič nezapísal");
        Over.Pravda(prvy.Trvanie < TimeSpan.FromMinutes(1), $"prvý sken do 1 min (bol {prvy.Trvanie.TotalSeconds:F1} s)");
        Over.Pravda(druhy.Trvanie < TimeSpan.FromSeconds(10), $"sken bez zmien do 10 s (bol {druhy.Trvanie.TotalSeconds:F1} s)");
    }

    private static async Task<TimeSpan> ZapisSuboryAsync(Kniznica kniznica, Random nahoda, int od, int po)
    {
        const int vDavke = 10_000;
        var cas = Stopwatch.StartNew();
        var zapisy = new List<Task>();
        for (var zaciatok = od; zaciatok < po; zaciatok += vDavke)
        {
            var riadky = Enumerable.Range(zaciatok, Math.Min(vDavke, po - zaciatok))
                // a scan writes folder by folder, so the files of one folder come together
                .Select(i => (Priecinok: 2 + (int)((long)i * PocetPriecinkov / PocetSuborov), Nazov: string.Create(CultureInfo.InvariantCulture,
                    $"{Slova[nahoda.Next(Slova.Length)]} {Slova[nahoda.Next(Slova.Length)]} {i} ({1950 + (i % 75)}).mkv"), Velkost: (long)i * 1000))
                .ToList();
            zapisy.Add(kniznica.Zapis.ZapisAsync(s =>
            {
                using var prikaz = s.CreateCommand();
                prikaz.CommandText = "INSERT INTO subor(priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, prvy_raz) VALUES ($p, 1, $n, 'mkv', $v, 0, 0)";
                var p = prikaz.Parameters.Add("$p", SqliteType.Integer);
                var n = prikaz.Parameters.Add("$n", SqliteType.Text);
                var v = prikaz.Parameters.Add("$v", SqliteType.Integer);
                prikaz.Prepare();
                foreach (var riadok in riadky)
                {
                    p.Value = riadok.Priecinok;
                    n.Value = riadok.Nazov;
                    v.Value = riadok.Velkost;
                    prikaz.ExecuteNonQuery();
                }
            }));
        }

        await Task.WhenAll(zapisy);
        return cas.Elapsed;
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }
}
