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
        await using var kniznica = await Kniznica.OtvorAsync(cesta);
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

        // the scan writes in batches of thousands of rows, each batch one queued write
        var nahoda = new Random(8112026);
        var cas = Stopwatch.StartNew();
        const int vDavke = 10_000;
        var zapisy = new List<Task>();
        for (var zaciatok = 0; zaciatok < PocetSuborov; zaciatok += vDavke)
        {
            var riadky = Enumerable.Range(zaciatok, vDavke)
                .Select(i => (Priecinok: 2 + (i % PocetPriecinkov), Nazov: string.Create(CultureInfo.InvariantCulture,
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
        var zapis = cas.Elapsed;

        using var spojenie = kniznica.OtvorCitanie();
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
        }

        await kniznica.Zapis.VyprazdniAsync();
        var velkostMb = (new FileInfo(cesta).Length + (File.Exists(cesta + "-wal") ? new FileInfo(cesta + "-wal").Length : 0)) / 1_000_000.0;
        var najhorsie = casy.Max();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"::notice title=Výkon (1 milión súborov)::zápis {zapis.TotalSeconds:F1} s, hľadanie najviac {najhorsie:F1} ms, knižnica {velkostMb:F0} MB"));

        Over.Pravda(najhorsie < 100, $"hľadanie do 100 ms (bolo {najhorsie:F1} ms)");
        Over.Pravda(zapis < TimeSpan.FromMinutes(3), $"zápis milióna súborov do 3 min (bolo {zapis.TotalSeconds:F0} s)");
        Over.Pravda(velkostMb < 400, $"knižnica do 400 MB pri milióne súborov (bola {velkostMb:F0} MB)");
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }
}
