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

        double najhorsie, najhorsieSoSlovami, najhorsieLenPolia;
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

            // the query language: words with fields (while typing), and fields alone (they may read the whole table)
            var kontext = new KontextHladania([1L], DateTime.Now);
            string[] sPolami = ["vlak typ:video velkost:>500MB", "zima -leto pripona:srt", "stastie disk:1 pripojene:ano zmenene:2001", "les 19"];
            string[] lenPolia = ["velkost:950MB..951MB", "pripona:mp4 zmenene:2001-03", "typ:titulky velkost:>950MB"];
            var casyPoli = new List<double>();
            foreach (var text in sPolami.Concat(lenPolia))
            {
                var dotaz = Katalog.Jadro.Dotazy.Dotaz.Citaj(text);
                Over.Rovne(0, dotaz.Upozornenia.Count, text);
                var vysledky = Hladanie.Najdi(spojenie, dotaz, kontext);
                casyPoli.Add(vysledky.Trvanie.TotalMilliseconds);
                Over.Pravda(vysledky.Polozky.Count > 0, $"„{text}“ niečo nájde");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"dotaz „{text}“: {vysledky.Polozky.Count}{(vysledky.Viac ? "+" : "")} výsledkov za {casyPoli[^1]:F1} ms"));
            }

            najhorsieSoSlovami = casyPoli.Take(sPolami.Length).Max();
            najhorsieLenPolia = casyPoli.Skip(sPolami.Length).Max();
        }

        // closing merges the WAL into the library file: its size is what the user keeps on the disk
        await kniznica.DisposeAsync();
        var velkostMb = new FileInfo(cesta).Length / 1_000_000.0;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"::notice title=Výkon (1 milión súborov)::prvých 100 000 súborov {prvych.TotalSeconds:F1} s, ďalších 900 000 {ostatnych.TotalSeconds:F1} s, hľadanie najviac {najhorsie:F1} ms, dotazy so slovami {najhorsieSoSlovami:F1} ms, len polia {najhorsieLenPolia:F1} ms, knižnica {velkostMb:F0} MB"));

        Over.Pravda(najhorsie < 100, $"hľadanie do 100 ms (bolo {najhorsie:F1} ms)");
        // a common word with a field that rules out most of its matches reads tens of thousands of them (measured ~100 ms
        // on GitHub's runner); the window waits 220 ms for a pause in typing anyway
        Over.Pravda(najhorsieSoSlovami < 150, $"dotaz so slovami a poľami do 150 ms (bolo {najhorsieSoSlovami:F1} ms)");
        Over.Pravda(najhorsieLenPolia < 500, $"dotaz len s poliami do 500 ms (bolo {najhorsieLenPolia:F1} ms)");
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

    /// <summary>
    /// The library of titles at its target size: 12 000 films (10 000 titles, some on two disks) and 50 series with 40
    /// episodes – recognised from names, then the poster wall of films and a filter (design target: a wall of 10 000
    /// titles scrolls smoothly, so its list must come quickly).
    /// </summary>
    [Test]
    public async Task DesatTisicTitulov()
    {
        if (Environment.GetEnvironmentVariable("KATALOG_VYKON") != "1")
        {
            Over.Preskoc("len s KATALOG_VYKON=1");
        }

        using var priecinok = new DocasnyPriecinok();
        var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Tituly.katalog"));
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, """
            INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Výkon', 0);
            INSERT INTO disk(id, katalog_id, cislo, nazov, pridany) VALUES (1, 1, 1, 'Vymyslený disk', 0);
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (1, 1, NULL, '');
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (2, 1, 1, 'MOVIES');
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (3, 1, 1, 'Filmy');
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (4, 1, 1, 'TVSHOWS');
            """));
        var nahoda = new Random(8112026);
        await kniznica.Zapis.ZapisAsync(s =>
        {
            using var priecinky = s.CreateCommand();
            priecinky.CommandText = "INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES ($id, 1, $r, $n)";
            var pid = priecinky.Parameters.Add("$id", SqliteType.Integer);
            var pr = priecinky.Parameters.Add("$r", SqliteType.Integer);
            var pn = priecinky.Parameters.Add("$n", SqliteType.Text);
            using var subory = s.CreateCommand();
            subory.CommandText = "INSERT INTO subor(priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, prvy_raz) VALUES ($p, 1, $n, 'mkv', 1000, 0, 0)";
            var sp = subory.Parameters.Add("$p", SqliteType.Integer);
            var sn = subory.Parameters.Add("$n", SqliteType.Text);
            var id = 10;
            for (var i = 0; i < 10_000; i++)
            {
                var nazov = string.Create(CultureInfo.InvariantCulture, $"{Slova[nahoda.Next(Slova.Length)]} {Slova[nahoda.Next(Slova.Length)]} {i} ({1950 + (i % 75)})");
                // films of UNIVERSUM's library, each in its own folder; every fifth also as another file elsewhere
                pid.Value = ++id;
                pr.Value = 2;
                pn.Value = nazov;
                priecinky.ExecuteNonQuery();
                sp.Value = id;
                sn.Value = nazov + " - CZ FHD H264 SMALL.mkv";
                subory.ExecuteNonQuery();
                if (i % 5 == 0)
                {
                    sp.Value = 3;
                    sn.Value = nazov.Replace(' ', '.') + ".1080p.mkv";
                    subory.ExecuteNonQuery();
                }
            }

            for (var serial = 0; serial < 50; serial++)
            {
                pid.Value = ++id;
                pr.Value = 4;
                pn.Value = string.Create(CultureInfo.InvariantCulture, $"Vymyslený seriál {serial} (2020)");
                priecinky.ExecuteNonQuery();
                for (var e = 1; e <= 40; e++)
                {
                    sp.Value = id;
                    sn.Value = string.Create(CultureInfo.InvariantCulture, $"Vymyslený seriál {serial} S{1 + e / 11:00}E{e:00} - CZ HD H264 SMALL.mkv");
                    subory.ExecuteNonQuery();
                }
            }
        });

        var rozpoznanie = Stopwatch.StartNew();
        var vysledok = await Katalog.Ulohy.RozpoznanieDisku.SpustiAsync(kniznica, 1, "", CancellationToken.None);
        rozpoznanie.Stop();
        Over.Rovne(12_000 + 2_000, vysledok.Verzie, "verzie filmov a epizód");

        double stena, filter, pocty;
        await using (var c = await kniznica.OtvorCitanieAsync())
        {
            var kontext = new KontextHladania([1L], DateTime.Now);
            KniznicaTitulov.Najdi(c, Katalog.Jadro.Dotazy.Dotaz.Citaj(""), kontext, PohladKniznice.Filmy);
            var cas = Stopwatch.StartNew();
            var filmy = KniznicaTitulov.Najdi(c, Katalog.Jadro.Dotazy.Dotaz.Citaj(""), kontext, PohladKniznice.Filmy);
            stena = cas.Elapsed.TotalMilliseconds;
            Over.Rovne(10_000, filmy.Count, "10 000 filmov (druhé súbory sú verzie)");
            cas.Restart();
            var najdene = KniznicaTitulov.Najdi(c, Katalog.Jadro.Dotazy.Dotaz.Citaj("vlak rok:1990..1999 pripona:mkv"), kontext, PohladKniznice.Filmy);
            filter = cas.Elapsed.TotalMilliseconds;
            Over.Pravda(najdene.Count > 0, "filter knižnice niečo nájde");
            cas.Restart();
            KniznicaTitulov.Pocty(c);
            pocty = cas.Elapsed.TotalMilliseconds;
        }

        await kniznica.DisposeAsync();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"::notice title=Výkon knižnice (10 000 titulov)::rozpoznanie 14 000 videí {rozpoznanie.Elapsed.TotalSeconds:F1} s, stena filmov {stena:F0} ms, filter {filter:F0} ms, počty {pocty:F0} ms"));
        Over.Pravda(stena < 1000, $"stena 10 000 filmov do 1 s (bola {stena:F0} ms)");
        Over.Pravda(filter < 1000, $"filter knižnice do 1 s (bol {filter:F0} ms)");
        Over.Pravda(rozpoznanie.Elapsed < TimeSpan.FromMinutes(1), $"rozpoznanie 14 000 videí do 1 min (bolo {rozpoznanie.Elapsed.TotalSeconds:F1} s)");
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
                    $"{Slova[nahoda.Next(Slova.Length)]} {Slova[nahoda.Next(Slova.Length)]} {i} ({1950 + (i % 75)}).{Pripona(i)}"),
                    Velkost: (long)i * 1000, Pripona: Pripona(i)))
                .ToList();
            zapisy.Add(kniznica.Zapis.ZapisAsync(s =>
            {
                using var prikaz = s.CreateCommand();
                prikaz.CommandText = "INSERT INTO subor(priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, prvy_raz) VALUES ($p, 1, $n, $x, $v, $z, 0)";
                var p = prikaz.Parameters.Add("$p", SqliteType.Integer);
                var n = prikaz.Parameters.Add("$n", SqliteType.Text);
                var x = prikaz.Parameters.Add("$x", SqliteType.Text);
                var v = prikaz.Parameters.Add("$v", SqliteType.Integer);
                var z = prikaz.Parameters.Add("$z", SqliteType.Integer);
                prikaz.Prepare();
                foreach (var riadok in riadky)
                {
                    p.Value = riadok.Priecinok;
                    n.Value = riadok.Nazov;
                    x.Value = riadok.Pripona;
                    v.Value = riadok.Velkost;
                    // one file a minute from 1 January 2000 (UTC ticks, as the scan stores times)
                    z.Value = Zaciatok + riadok.Velkost / 1000 * TimeSpan.TicksPerMinute;
                    prikaz.ExecuteNonQuery();
                }
            }));
        }

        await Task.WhenAll(zapisy);
        return cas.Elapsed;
    }

    private static readonly long Zaciatok = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>Mostly videos, every tenth file subtitles, every seventh another video format.</summary>
    private static string Pripona(int i) => i % 10 == 0 ? "srt" : i % 7 == 0 ? "mp4" : "mkv";

    private static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }
}
