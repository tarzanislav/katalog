using System.Globalization;
using Katalog.Data;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

public sealed class KniznicaTesty
{
    [Test]
    public async Task NovaKniznicaMaPoslednuSchemu()
    {
        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("Kniznica.katalog");
        await using (var kniznica = await Kniznica.OtvorAsync(cesta))
        {
            Over.Rovne(Migracie.Posledna, kniznica.VerziaSchemy);
            Over.Pravda(kniznica.Kontrola.JeVPoriadku, "nová knižnica je v poriadku");
            Over.Rovne<string?>(null, kniznica.ZalohaPredMigraciou, "nová knižnica sa nezálohuje");
            using var spojenie = kniznica.OtvorCitanie();
            Over.Rovne((long)Kniznica.IdAplikacie, Cislo(spojenie, "PRAGMA application_id"));
            Over.Rovne((long)Migracie.Posledna, Cislo(spojenie, "PRAGMA user_version"));
            Over.Rovne("wal", Text(spojenie, "PRAGMA journal_mode"));
            Over.Pravda(Cislo(spojenie, "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'verzia_subor'") == 1, "tabuľka verzia_subor");
        }

        // opening again changes nothing and makes no backup
        await using var znova = await Kniznica.OtvorAsync(cesta);
        Over.Rovne<string?>(null, znova.ZalohaPredMigraciou);
        Over.Rovne(0, ZalohyKniznice.Vedla(cesta).Zoznam().Count);
    }

    [Test]
    public async Task CudziSuborSaNeotvori()
    {
        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("cudzi.db");
        using (var spojenie = new SqliteConnection("Data Source=" + cesta + ";Pooling=False"))
        {
            spojenie.Open();
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "CREATE TABLE x (a INTEGER)";
            prikaz.ExecuteNonQuery();
        }

        await Over.VyhodiAsync<CudziSuborException>(async () => await (await Kniznica.OtvorAsync(cesta)).DisposeAsync(), "cudzia databáza");
    }

    [Test]
    public async Task NovsiaKniznicaSaNeotvori()
    {
        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("Kniznica.katalog");
        await (await Kniznica.OtvorAsync(cesta)).DisposeAsync();
        using (var spojenie = new SqliteConnection("Data Source=" + cesta + ";Pooling=False"))
        {
            spojenie.Open();
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {Migracie.Posledna + 1}");
            prikaz.ExecuteNonQuery();
        }

        var chyba = await Over.VyhodiAsync<NovsiaKniznicaException>(async () => await (await Kniznica.OtvorAsync(cesta)).DisposeAsync(), "novšia schéma");
        Over.Rovne(Migracie.Posledna + 1, chyba.VerziaSuboru);
    }

    [Test]
    public async Task ZalohyStupajuAOstavaIchDesat()
    {
        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("Kniznica.katalog");
        await using var kniznica = await Kniznica.OtvorAsync(cesta);
        var zalohy = new ZalohyKniznice(priecinok.Subor("Zalohy"), pocet: 10);
        using var spojenie = kniznica.OtvorCitanie();
        for (var i = 0; i < 12; i++)
        {
            zalohy.Vytvor(spojenie, kniznica.VerziaSchemy);
        }

        var zoznam = zalohy.Zoznam();
        Over.Postupnost(Enumerable.Range(3, 10), zoznam.Select(z => z.Cislo), "čísla záloh");
        Over.Pravda(zoznam.All(z => Path.GetFileName(z.Cesta).StartsWith("KATALOG-zaloha-", StringComparison.Ordinal)), "názov zálohy");

        // a backup is a complete library
        await using var obnovena = await Kniznica.OtvorAsync(zoznam[^1].Cesta, new ZalohyKniznice(priecinok.Subor("Ine")));
        Over.Pravda(obnovena.Kontrola.JeVPoriadku, "záloha je v poriadku");
    }

    [Test]
    public async Task KodyZhodujuSaSoSchemou()
    {
        // every enum value is accepted by its CHECK and the next number is refused: code and schema never drift apart
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, "INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Vymyslený', 0)"));

        await OverKody<DruhDisku>(kniznica, h => $"INSERT INTO disk(katalog_id, cislo, nazov, druh, pridany) VALUES (1, {100 + h}, 'd', {h}, 0)");
        await OverKody<DruhTitulu>(kniznica, h => $"INSERT INTO titul(druh, nazov, vytvoreny, zmeneny) VALUES ({h}, 't', 0, 0)");
        await OverKody<StavUlohy>(kniznica, h => $"INSERT INTO uloha(druh, stav, pruh, vytvorena) VALUES (1, {h}, 'cpu', 0)");
        await OverKody<DruhUlohy>(kniznica, h => $"INSERT INTO uloha(druh, pruh, vytvorena) VALUES ({h}, 'cpu', 0)");
        await OverKody<DruhPlaylistu>(kniznica, h => $"INSERT INTO playlist(nazov, druh, dotaz, vytvoreny) VALUES ('p', {h}, 'x', 0)");
        await OverKody<DruhObrazka>(kniznica, h => $"INSERT INTO obrazok(odtlacok, druh, sirka, vyska, velkost, pridany) VALUES (randomblob(32), {h}, 1, 1, 0, 0)");
        await OverKody<StavZdravia>(kniznica, h => $"INSERT INTO zdravie_zaznam(disk_id, kedy, stav) SELECT min(id), 0, {h} FROM disk");
    }

    [Test]
    public async Task VazbyStrazTriggery()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var z = kniznica.Zapis;
        await z.ZapisAsync(s => Vykonaj(s, """
            INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Videotéka', 0);
            INSERT INTO disk(id, katalog_id, cislo, nazov, pridany) VALUES (1, 1, 1, 'Vymyslený disk A', 0), (2, 1, 2, 'Vymyslený disk B', 0);
            INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES (1, 1, NULL, ''), (2, 1, 1, 'Filmy'), (3, 2, NULL, '');
            INSERT INTO titul(id, druh, nazov, vytvoreny, zmeneny) VALUES (1, 1, 'Vymyslený film', 0, 0), (2, 2, 'Vymyslený seriál', 0, 0);
            """));

        await Over.VyhodiAsync<SqliteException>(() => z.ZapisAsync(s => Vykonaj(s,
            "INSERT INTO subor(priecinok_id, disk_id, nazov, velkost, zmeneny, prvy_raz) VALUES (2, 2, 'x.mkv', 1, 0, 0)")), "súbor na inom disku ako priečinok");
        await Over.VyhodiAsync<SqliteException>(() => z.ZapisAsync(s => Vykonaj(s,
            "INSERT INTO priecinok(disk_id, rodic_id, nazov) VALUES (2, 2, 'Cudzí')")), "priečinok pod priečinkom iného disku");
        await Over.VyhodiAsync<SqliteException>(() => z.ZapisAsync(s => Vykonaj(s,
            "INSERT INTO epizoda(titul_id, seria, cislo) VALUES (1, 1, 1)")), "epizóda filmu");
        await Over.VyhodiAsync<SqliteException>(() => z.ZapisAsync(s => Vykonaj(s,
            "INSERT INTO verzia(titul_id, epizoda_id, vytvorena) VALUES (NULL, NULL, 0)")), "verzia bez titulu aj epizódy");

        // a version of an episode, then the disk leaves the library: files, version and index entries go with it
        await z.ZapisAsync(s => Vykonaj(s, """
            INSERT INTO epizoda(id, titul_id, seria, cislo, nazov) VALUES (1, 2, 1, 1, 'Pilot');
            INSERT INTO subor(id, priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, prvy_raz) VALUES (1, 2, 1, 'Vymyslený seriál S01E01.mkv', 'mkv', 1, 0, 0);
            INSERT INTO verzia(id, epizoda_id, vytvorena) VALUES (1, 1, 0);
            INSERT INTO verzia_subor(verzia_id, subor_id) VALUES (1, 1);
            INSERT INTO pozretie(epizoda_id, kedy) VALUES (1, 0);
            """));
        using (var citanie = kniznica.OtvorCitanie())
        {
            Over.Rovne(1, Fulltext.Najdi(citanie, "seriál s01e01").Count(v => v.Druh == DruhHladania.Subor), "súbor v indexe");
        }

        await z.ZapisAsync(s => Vykonaj(s, "DELETE FROM disk WHERE id = 1"));
        using var spojenie = kniznica.OtvorCitanie();
        Over.Rovne(0L, Cislo(spojenie, "SELECT count(*) FROM subor"), "súbory disku");
        Over.Rovne(0L, Cislo(spojenie, "SELECT count(*) FROM verzia"), "verzia bez súborov");
        Over.Rovne(1L, Cislo(spojenie, "SELECT count(*) FROM pozretie"), "pozretie patrí epizóde, nie súboru");
        Over.Rovne(0, Fulltext.Najdi(spojenie, "S01E01").Count(v => v.Druh == DruhHladania.Subor), "index bez súboru");
        using var kontrola = spojenie.CreateCommand();
        kontrola.CommandText = "PRAGMA foreign_key_check";
        using var citac = kontrola.ExecuteReader();
        Over.Pravda(!citac.Read(), "žiadna porušená väzba");
    }

    [Test]
    public async Task HladanieBezDiakritikyAKdekolvekVSlove()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, """
            INSERT INTO titul(id, druh, nazov, original, vytvoreny, zmeneny) VALUES (1, 1, 'Šťastný pelikán', 'Happy Pelican', 0, 0);
            INSERT INTO osoba(id, meno) VALUES (1, 'Vymyslená Herečka');
            """));
        using var spojenie = kniznica.OtvorCitanie();
        Over.Postupnost<(DruhHladania, long)>([(DruhHladania.Titul, 1L)], Fulltext.Najdi(spojenie, "stastny"), "bez diakritiky");
        Over.Postupnost<(DruhHladania, long)>([(DruhHladania.Titul, 1L)], Fulltext.Najdi(spojenie, "LIKÁN"), "časť slova, veľké písmená");
        Over.Postupnost<(DruhHladania, long)>([(DruhHladania.Titul, 1L)], Fulltext.Najdi(spojenie, "pelican"), "pôvodný názov");
        Over.Postupnost<(DruhHladania, long)>([(DruhHladania.Osoba, 1L)], Fulltext.Najdi(spojenie, "herecka"), "osoba");
        Over.Rovne(0, Fulltext.Najdi(spojenie, "\" OR *").Count, "znaky dotazu nie sú operátory");
        Over.Rovne<string?>(null, Fulltext.Vyraz("ab"), "kratšie ako trigram");
    }

    [Test]
    public async Task ChybnyZapisNezrusiOstatneVDavke()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var ulohy = Enumerable.Range(1, 300)
            .Select(i => i == 150
                ? kniznica.Zapis.ZapisAsync(s => Vykonaj(s, "INSERT INTO katalog(nazov, vytvoreny) VALUES ('', 0)"))
                : kniznica.Zapis.ZapisAsync(s => Vykonaj(s, string.Create(CultureInfo.InvariantCulture, $"INSERT INTO katalog(nazov, vytvoreny) VALUES ('K{i}', 0)"))))
            .ToList();
        var vysledky = await Task.WhenAll(ulohy.Select(async u =>
        {
            try
            {
                await u;
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }));

        Over.Rovne(299, vysledky.Count(v => v), "úspešné zápisy");
        using var spojenie = kniznica.OtvorCitanie();
        Over.Rovne(299L, Cislo(spojenie, "SELECT count(*) FROM katalog"));
        Over.Pravda(kniznica.Zapis.PocetDavok < 300, "zápisy sa spájajú do dávok");
        var id = await kniznica.Zapis.ZapisAsync(s => Cislo(s, "INSERT INTO katalog(nazov, vytvoreny) VALUES ('Posledný', 0) RETURNING id"));
        Over.Pravda(id > 0, "zápis vracia hodnotu");
    }

    [Test]
    public async Task CitanieNemozeZapisovat()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        using (var citanie = kniznica.OtvorCitanie())
        {
            Over.Vyhodi<SqliteException>(() => Vykonaj(citanie, "INSERT INTO katalog(nazov, vytvoreny) VALUES ('x', 0)"), "spojenie na čítanie");
        }

        // the pooled connection does not stay read-only for the writer
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, "INSERT INTO katalog(nazov, vytvoreny) VALUES ('Vymyslený', 0)"));
        await using var citanie2 = await kniznica.OtvorCitanieAsync();
        Over.Rovne(1L, Cislo(citanie2, "SELECT count(*) FROM katalog"));
    }

    private static async Task OverKody<T>(Kniznica kniznica, Func<int, string> sql)
        where T : struct, Enum
    {
        var hodnoty = Enum.GetValues<T>().Select(h => Convert.ToInt32(h, CultureInfo.InvariantCulture)).ToList();
        foreach (var hodnota in hodnoty)
        {
            await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, sql(hodnota)));
        }

        await Over.VyhodiAsync<SqliteException>(() => kniznica.Zapis.ZapisAsync(s => Vykonaj(s, sql(hodnoty.Max() + 1))), $"{typeof(T).Name} {hodnoty.Max() + 1}");
    }

    internal static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }

    internal static long Cislo(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        return Convert.ToInt64(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Text(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        return Convert.ToString(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }
}
