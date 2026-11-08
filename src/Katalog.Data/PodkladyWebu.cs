using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A file of a version as the web catalogue shows it: its disk, path on the disk, size and quality.</summary>
public sealed record VerziaPreWeb(long DiskId, string Cesta, long Velkost, TriedaKvality Kvalita, int? Seria, int? Cislo);

/// <summary>A title with everything the web catalogue shows of it.</summary>
public sealed record TitulPreWeb(
    PolozkaKniznice Titul,
    string? Dej,
    IReadOnlyList<string> Zanre,
    IReadOnlyList<string> Rezia,
    IReadOnlyList<string> Herci,
    IReadOnlyList<string> Kolekcie,
    IReadOnlyList<string> Stitky,
    IReadOnlyList<VerziaPreWeb> Verzie,
    int Epizod);

/// <summary>
/// What the private web catalogue (stage E9) is made of, read in a few set queries (not title by title): the titles the
/// user has or wants, with their genres, people, collections, tags and the files of their versions, and the disks.
/// </summary>
public static class PodkladyWebu
{
    public static IReadOnlyList<TitulPreWeb> Tituly(SqliteConnection spojenie, KontextHladania kontext)
    {
        var tituly = KniznicaTitulov.Najdi(spojenie, Dotaz.Citaj(""), kontext, PohladKniznice.Vsetko).Where(t => t.Mam || t.Zelany).ToList();
        var deje = Mapa(spojenie, "SELECT id, dej FROM titul WHERE dej IS NOT NULL");
        var zanre = Zoznamy(spojenie, "SELECT tz.titul_id, z.nazov FROM titul_zaner tz JOIN zaner z ON z.id = tz.zaner_id ORDER BY tz.titul_id, z.nazov");
        var rezia = Zoznamy(spojenie, $"SELECT tos.titul_id, o.meno FROM titul_osoba tos JOIN osoba o ON o.id = tos.osoba_id WHERE tos.rola = {(int)RolaOsoby.Rezia} ORDER BY tos.titul_id, tos.poradie");
        var herci = Zoznamy(spojenie, $"SELECT tos.titul_id, o.meno FROM titul_osoba tos JOIN osoba o ON o.id = tos.osoba_id WHERE tos.rola = {(int)RolaOsoby.Herec} ORDER BY tos.titul_id, tos.poradie");
        var kolekcie = Zoznamy(spojenie, "SELECT kt.titul_id, k.nazov FROM kolekcia_titul kt JOIN kolekcia k ON k.id = kt.kolekcia_id ORDER BY kt.titul_id, k.nazov");
        var stitky = Zoznamy(spojenie, "SELECT ts.titul_id, s.nazov FROM titul_stitok ts JOIN stitok s ON s.id = ts.stitok_id ORDER BY ts.titul_id, s.kluc");
        var epizod = new Dictionary<long, int>();
        using (var prikaz = Kniznica.Prikaz(spojenie, "SELECT titul_id, count(*) FROM epizoda GROUP BY titul_id"))
        using (var citac = prikaz.ExecuteReader())
        {
            while (citac.Read())
            {
                epizod[citac.GetInt64(0)] = citac.GetInt32(1);
            }
        }

        var verzie = new Dictionary<long, List<VerziaPreWeb>>();
        var cesty = new Dictionary<long, string>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT coalesce(v.titul_id, e.titul_id), e.seria, e.cislo, s.disk_id, s.priecinok_id, s.nazov, s.velkost, coalesce(m.trieda_kvality, 0)
            FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id
            JOIN verzia_subor vs ON vs.verzia_id = v.id JOIN subor s ON s.id = vs.subor_id
            LEFT JOIN medialne_udaje m ON m.subor_id = s.id
            WHERE s.stav = 0
            ORDER BY 1, e.seria, e.cislo, v.id, vs.poradie
            """))
        using (var citac = prikaz.ExecuteReader())
        {
            while (citac.Read())
            {
                var priecinok = citac.GetInt64(4);
                if (!cesty.TryGetValue(priecinok, out var cesta))
                {
                    cesta = KatalogyADisky.Cesta(spojenie, priecinok);
                    cesty[priecinok] = cesta;
                }

                var titul = citac.GetInt64(0);
                if (!verzie.TryGetValue(titul, out var zoznam))
                {
                    verzie[titul] = zoznam = [];
                }

                zoznam.Add(new VerziaPreWeb(
                    citac.GetInt64(3),
                    cesta.Length == 0 ? citac.GetString(5) : cesta + "/" + citac.GetString(5),
                    citac.GetInt64(6),
                    (TriedaKvality)citac.GetInt32(7),
                    citac.IsDBNull(1) ? null : citac.GetInt32(1),
                    citac.IsDBNull(2) ? null : citac.GetInt32(2)));
            }
        }

        return tituly.Select(t => new TitulPreWeb(
            t,
            deje.GetValueOrDefault(t.Id),
            zanre.GetValueOrDefault(t.Id) ?? [],
            rezia.GetValueOrDefault(t.Id) ?? [],
            (herci.GetValueOrDefault(t.Id) ?? []).Take(8).ToList(),
            kolekcie.GetValueOrDefault(t.Id) ?? [],
            stitky.GetValueOrDefault(t.Id) ?? [],
            verzie.GetValueOrDefault(t.Id) ?? [],
            epizod.GetValueOrDefault(t.Id))).ToList();
    }

    private static Dictionary<long, string> Mapa(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, sql);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new Dictionary<long, string>();
        while (citac.Read())
        {
            vysledok[citac.GetInt64(0)] = citac.GetString(1);
        }

        return vysledok;
    }

    private static Dictionary<long, IReadOnlyList<string>> Zoznamy(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, sql);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new Dictionary<long, IReadOnlyList<string>>();
        while (citac.Read())
        {
            var id = citac.GetInt64(0);
            if (!vysledok.TryGetValue(id, out var zoznam))
            {
                vysledok[id] = zoznam = new List<string>();
            }

            ((List<string>)zoznam).Add(citac.GetString(1));
        }

        return vysledok;
    }
}
