using System.Globalization;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>
/// A playlist: hand-made (titles in the user's order), smart (a query of the one query language, A4, chronological) or
/// random (a number of titles drawn from a query; the seed keeps the draw until the user draws again).
/// </summary>
public sealed record Playlist(long Id, string Nazov, DruhPlaylistu Druh, string? Dotaz, int? Pocet, long Semienko, int Poloziek, long Vytvoreny);

/// <summary>A playlist the program suggests from the library (saved as a smart one when the user wants it).</summary>
public sealed record NavrhPlaylistu(string Nazov, string Dotaz, string Popis, int Titulov);

/// <summary>Playlists (stage E7): keeping them, their titles, drawing at random, suggestions and what plays next.</summary>
public static class Playlisty
{
    /// <summary>A random playlist draws 10 titles when the user says no number.</summary>
    public const int PredvolenyPocet = 10;

    public static IReadOnlyList<Playlist> Zoznam(SqliteConnection spojenie) => Citaj(spojenie, null);

    public static Playlist? Najdi(SqliteConnection spojenie, long id) => Citaj(spojenie, id).FirstOrDefault();

    private static List<Playlist> Citaj(SqliteConnection spojenie, long? id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT p.id, p.nazov, p.druh, p.dotaz, p.pocet, p.semienko, (SELECT count(*) FROM playlist_polozka pp WHERE pp.playlist_id = p.id), p.vytvoreny
            FROM playlist p {(id is null ? "" : "WHERE p.id = $id")} ORDER BY p.nazov COLLATE NOCASE, p.id
            """);
        if (id is { } i)
        {
            prikaz.Parameters.AddWithValue("$id", i);
        }

        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Playlist>();
        while (citac.Read())
        {
            vysledok.Add(new Playlist(
                citac.GetInt64(0), citac.GetString(1), (DruhPlaylistu)citac.GetInt32(2), citac.IsDBNull(3) ? null : citac.GetString(3),
                citac.IsDBNull(4) ? null : citac.GetInt32(4), citac.GetInt64(5), citac.GetInt32(6), citac.GetInt64(7)));
        }

        return vysledok;
    }

    /// <summary>A new playlist; a smart or random one needs its query, a random one gets a number and a seed.</summary>
    public static long Vytvor(SqliteConnection spojenie, string nazov, DruhPlaylistu druh, string? dotaz, int? pocet, long teraz)
    {
        nazov = Over(nazov, druh, ref dotaz, ref pocet);
        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT INTO playlist(nazov, druh, dotaz, pocet, semienko, vytvoreny) VALUES ($n, $d, $q, $c, $s, $t) RETURNING id
            """);
        prikaz.Parameters.AddWithValue("$n", nazov);
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$q", (object?)dotaz ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$c", (object?)pocet ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$s", NoveSemienko());
        prikaz.Parameters.AddWithValue("$t", teraz);
        return (long)prikaz.ExecuteScalar()!;
    }

    /// <summary>Changes the name, the query and the number (the kind stays).</summary>
    public static void Uprav(SqliteConnection spojenie, long id, string nazov, string? dotaz, int? pocet)
    {
        var druh = Najdi(spojenie, id)?.Druh ?? throw new ArgumentException("Playlist neexistuje.", nameof(id));
        nazov = Over(nazov, druh, ref dotaz, ref pocet);
        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE playlist SET nazov = $n, dotaz = $q, pocet = $c WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$n", nazov);
        prikaz.Parameters.AddWithValue("$q", (object?)dotaz ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$c", (object?)pocet ?? DBNull.Value);
        prikaz.ExecuteNonQuery();
    }

    private static string Over(string nazov, DruhPlaylistu druh, ref string? dotaz, ref int? pocet)
    {
        nazov = nazov.Trim();
        if (nazov.Length == 0)
        {
            throw new ArgumentException("Playlist potrebuje názov.", nameof(nazov));
        }

        dotaz = string.IsNullOrWhiteSpace(dotaz) ? null : dotaz.Trim();
        if (druh == DruhPlaylistu.Rucny)
        {
            dotaz = null;
            pocet = null;
        }
        else
        {
            // a smart playlist of "everything" is an empty query; the column wants a text
            dotaz ??= "";
            pocet = druh == DruhPlaylistu.Nahodny ? Math.Clamp(pocet ?? PredvolenyPocet, 1, 1000) : null;
        }

        return nazov;
    }

    public static void Zmaz(SqliteConnection spojenie, long id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM playlist WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>A random playlist draws again (a new seed).</summary>
    public static void Vylosuj(SqliteConnection spojenie, long id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE playlist SET semienko = $s WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$s", NoveSemienko());
        prikaz.ExecuteNonQuery();
    }

    private static long NoveSemienko() => Random.Shared.NextInt64(1, long.MaxValue);

    /// <summary>Adds titles at the end of a hand-made playlist (a title there already stays where it is).</summary>
    public static int Pridaj(SqliteConnection spojenie, long id, IEnumerable<long> tituly)
    {
        var pridane = 0;
        foreach (var titulId in tituly.Distinct())
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                INSERT INTO playlist_polozka(playlist_id, poradie, titul_id)
                SELECT $p, coalesce((SELECT max(poradie) + 1 FROM playlist_polozka WHERE playlist_id = $p), 0), $t
                WHERE NOT EXISTS (SELECT 1 FROM playlist_polozka WHERE playlist_id = $p AND titul_id = $t)
                """);
            prikaz.Parameters.AddWithValue("$p", id);
            prikaz.Parameters.AddWithValue("$t", titulId);
            pridane += prikaz.ExecuteNonQuery();
        }

        return pridane;
    }

    /// <summary>Takes titles out of a hand-made playlist (with any of their episodes); the rest closes up.</summary>
    public static void Odober(SqliteConnection spojenie, long id, IEnumerable<long> tituly)
    {
        foreach (var titulId in tituly.Distinct())
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                DELETE FROM playlist_polozka WHERE playlist_id = $p
                  AND (titul_id = $t OR epizoda_id IN (SELECT id FROM epizoda WHERE titul_id = $t))
                """);
            prikaz.Parameters.AddWithValue("$p", id);
            prikaz.Parameters.AddWithValue("$t", titulId);
            prikaz.ExecuteNonQuery();
        }

        Precisluj(spojenie, id);
    }

    /// <summary>Moves a title one place up (-1) or down (+1) in a hand-made playlist.</summary>
    public static void Posun(SqliteConnection spojenie, long id, long titulId, int smer)
    {
        var poradie = Poradie(spojenie, id);
        var i = poradie.FindIndex(p => p.TitulId == titulId);
        var j = i + Math.Sign(smer);
        if (i < 0 || j < 0 || j >= poradie.Count)
        {
            return;
        }

        (poradie[i], poradie[j]) = (poradie[j], poradie[i]);
        Zapis(spojenie, id, poradie);
    }

    /// <summary>Puts the titles of a hand-made playlist in this order (titles not named keep their order after them).</summary>
    public static void Zorad(SqliteConnection spojenie, long id, IReadOnlyList<long> tituly)
    {
        var poradie = Poradie(spojenie, id);
        var index = tituly.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        Zapis(spojenie, id, poradie.OrderBy(p => p.TitulId is { } t && index.TryGetValue(t, out var i) ? i : int.MaxValue).ToList());
    }

    private static List<(long? TitulId, long? EpizodaId, long? VerziaId)> Poradie(SqliteConnection spojenie, long id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT titul_id, epizoda_id, verzia_id FROM playlist_polozka WHERE playlist_id = $p ORDER BY poradie");
        prikaz.Parameters.AddWithValue("$p", id);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<(long?, long?, long?)>();
        while (citac.Read())
        {
            vysledok.Add((citac.IsDBNull(0) ? null : citac.GetInt64(0), citac.IsDBNull(1) ? null : citac.GetInt64(1), citac.IsDBNull(2) ? null : citac.GetInt64(2)));
        }

        return vysledok;
    }

    private static void Precisluj(SqliteConnection spojenie, long id) => Zapis(spojenie, id, Poradie(spojenie, id));

    private static void Zapis(SqliteConnection spojenie, long id, List<(long? TitulId, long? EpizodaId, long? VerziaId)> poradie)
    {
        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM playlist_polozka WHERE playlist_id = $p"))
        {
            zmaz.Parameters.AddWithValue("$p", id);
            zmaz.ExecuteNonQuery();
        }

        using var vloz = Kniznica.Prikaz(spojenie, "INSERT INTO playlist_polozka(playlist_id, poradie, titul_id, epizoda_id, verzia_id) VALUES ($p, $i, $t, $e, $v)");
        var p = vloz.Parameters.AddWithValue("$p", id);
        var pi = vloz.Parameters.AddWithValue("$i", 0);
        var pt = vloz.Parameters.AddWithValue("$t", DBNull.Value);
        var pe = vloz.Parameters.AddWithValue("$e", DBNull.Value);
        var pv = vloz.Parameters.AddWithValue("$v", DBNull.Value);
        for (var i = 0; i < poradie.Count; i++)
        {
            pi.Value = i;
            pt.Value = (object?)poradie[i].TitulId ?? DBNull.Value;
            pe.Value = (object?)poradie[i].EpizodaId ?? DBNull.Value;
            pv.Value = (object?)poradie[i].VerziaId ?? DBNull.Value;
            vloz.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The titles of a playlist as they play: a hand-made one in its order, a smart one chronologically (year, then name),
    /// a random one as drawn. Smart and random playlists take only titles the user has.
    /// </summary>
    public static IReadOnlyList<PolozkaKniznice> Tituly(SqliteConnection spojenie, Playlist playlist, KontextHladania kontext)
    {
        if (playlist.Druh == DruhPlaylistu.Rucny)
        {
            return KniznicaTitulov.Najdi(spojenie, Dotaz.Citaj(""), kontext, PohladKniznice.Vsetko, playlistId: playlist.Id);
        }

        var kandidati = KniznicaTitulov.Najdi(spojenie, Dotaz.Citaj(playlist.Dotaz), kontext, PohladKniznice.Vsetko).Where(t => t.Mam).ToList();
        if (playlist.Druh == DruhPlaylistu.Nahodny)
        {
            return Losuj(kandidati.OrderBy(t => t.Id).ToList(), playlist.Semienko, playlist.Pocet ?? PredvolenyPocet);
        }

        var porovnanie = StringComparer.Create(CultureInfo.GetCultureInfo("sk-SK"), CompareOptions.IgnoreCase);
        return kandidati.OrderBy(t => t.Rok ?? int.MaxValue).ThenBy(t => t.Nazov, porovnanie).ToList();
    }

    /// <summary>
    /// <paramref name="pocet"/> items drawn from <paramref name="zoznam"/> in a random order fixed by the seed (the first
    /// steps of a Fisher–Yates shuffle with the program's own generator, so the draw is the same on every computer and
    /// .NET version).
    /// </summary>
    public static IReadOnlyList<T> Losuj<T>(IReadOnlyList<T> zoznam, long semienko, int pocet)
    {
        var pole = zoznam.ToArray();
        var generator = new Generator(semienko);
        var kolko = Math.Clamp(pocet, 0, pole.Length);
        for (var i = 0; i < kolko; i++)
        {
            var j = i + (int)generator.Dalsie((ulong)(pole.Length - i));
            (pole[i], pole[j]) = (pole[j], pole[i]);
        }

        return pole.Take(kolko).ToList();
    }

    /// <summary>SplitMix64 – a small generator with a fixed, documented sequence.</summary>
    private sealed class Generator(long semienko)
    {
        private ulong _stav = unchecked((ulong)semienko);

        /// <summary>A number 0 ≤ x &lt; <paramref name="hranica"/>, without the bias of a plain modulo.</summary>
        public ulong Dalsie(ulong hranica)
        {
            var prah = unchecked(0 - hranica) % hranica;
            while (true)
            {
                var x = Surove();
                if (x >= prah)
                {
                    return x % hranica;
                }
            }
        }

        private ulong Surove()
        {
            unchecked
            {
                var z = _stav += 0x9E3779B97F4A7C15;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
                return z ^ (z >> 31);
            }
        }
    }

    /// <summary>
    /// What plays of a series in a playlist or at random: its first episode the user has and has not watched (seasons in
    /// order, specials last); when all are watched, the first one. Null when no episode is on a disk.
    /// </summary>
    public static long? DalsiaEpizoda(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT e.id FROM epizoda e
            WHERE e.titul_id = $t AND EXISTS (SELECT 1 FROM verzia v JOIN verzia_subor vs ON vs.verzia_id = v.id JOIN subor s ON s.id = vs.subor_id
                                              WHERE v.epizoda_id = e.id AND s.stav = 0)
            ORDER BY EXISTS (SELECT 1 FROM pozretie p WHERE p.epizoda_id = e.id), e.seria = 0, e.seria, e.cislo
            LIMIT 1
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        return prikaz.ExecuteScalar() as long?;
    }

    /// <summary>
    /// The version to play of a film or an episode: the best present one on a connected disk (by the user's order of
    /// criteria), else the best present one anywhere (its disk is then waited for). Null when there is none.
    /// </summary>
    public static long? VerziaNaPrehratie(
        SqliteConnection spojenie, long titulId, long? epizodaId, IReadOnlySet<long> pripojene, IReadOnlyList<KriteriumVerzie> poradie)
    {
        var verzie = KniznicaTitulov.Verzie(spojenie, titulId).Where(v => v.Pritomna && v.EpizodaId == epizodaId).ToList();
        if (verzie.Count == 0)
        {
            return null;
        }

        var vlastnosti = KniznicaTitulov.VlastnostiVerzii(spojenie, titulId).ToDictionary(v => v.VerziaId);
        var naPripojenom = verzie.Where(v => v.Subory.Any(s => !s.Zmazany && pripojene.Contains(s.DiskId))).ToList();
        var z = naPripojenom.Count > 0 ? naPripojenom : verzie;
        var porovnatelne = z.Select(v => vlastnosti.GetValueOrDefault(v.Id)).OfType<VlastnostiVerzie>().ToList();
        return porovnatelne.Count == z.Count && PorovnanieVerzii.Najlepsia(porovnatelne, poradie) is { } najlepsia ? najlepsia : z[0].Id;
    }

    /// <summary>
    /// Playlists the library suggests: unwatched films, the best unwatched, series begun, short ones for an evening, the
    /// user's favourites, the largest genres and the user's tags, collections as marathons. Only those with at least two
    /// titles are offered.
    /// </summary>
    public static IReadOnlyList<NavrhPlaylistu> Navrhy(SqliteConnection spojenie, KontextHladania kontext)
    {
        var navrhy = new List<(string Nazov, string Dotaz, string Popis)>
        {
            ("Nevidené filmy", "kniznica:film videne:nie", "filmy, ktoré si ešte nevidel"),
            ("To najlepšie, čo si nevidel", "videne:nie hodnotenie:>=7.5", "hodnotenie zdroja aspoň 7,5"),
            ("Rozpozerané seriály", "rozpozerane:ano", "seriály s nevidenými epizódami, ktoré si začal"),
            ("Na krátky večer", "kniznica:film videne:nie dlzka:<=100min", "nevidené filmy do 100 minút"),
            ("Moje obľúbené", "moje:>=8", "tvoje hodnotenie aspoň 8"),
        };

        // the largest genres of the library
        using (var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT z.nazov FROM zaner z JOIN titul_zaner tz ON tz.zaner_id = z.id GROUP BY z.id ORDER BY count(*) DESC, z.nazov LIMIT 3
            """))
        using (var citac = prikaz.ExecuteReader())
        {
            while (citac.Read())
            {
                var zaner = citac.GetString(0);
                navrhy.Add(($"{zaner} – nevidené", $"zaner:{Uvodzovky(Normalizacia.Nazov(zaner))} videne:nie", $"žáner {zaner}, čo si ešte nevidel"));
            }
        }

        foreach (var stitok in StitkyTitulov.Zoznam(spojenie))
        {
            navrhy.Add(($"Štítok {stitok.Nazov}", $"stitok:{Uvodzovky(stitok.Nazov)}", "tituly s tvojím štítkom"));
        }

        foreach (var kolekcia in KniznicaTitulov.ZoznamKolekcii(spojenie).Where(k => k.Mam >= 2))
        {
            navrhy.Add(($"Maratón: {kolekcia.Nazov}", $"kolekcia:{Uvodzovky(kolekcia.Nazov)}", $"mám {kolekcia.Mam} z {kolekcia.Diely}, chronologicky"));
        }

        var vysledok = new List<NavrhPlaylistu>();
        foreach (var (nazov, dotaz, popis) in navrhy)
        {
            var pocet = KniznicaTitulov.Najdi(spojenie, Dotaz.Citaj(dotaz), kontext, PohladKniznice.Vsetko).Count(t => t.Mam);
            if (pocet >= 2)
            {
                vysledok.Add(new NavrhPlaylistu(nazov, dotaz, popis, pocet));
            }
        }

        return vysledok;
    }

    private static string Uvodzovky(string text) =>
        text.Contains(' ', StringComparison.Ordinal) ? "\"" + text.Replace("\"", "", StringComparison.Ordinal) + "\"" : text;
}
