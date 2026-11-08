using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A tag of titles and how many titles have it.</summary>
public sealed record StitokTitulov(long Id, string Nazov, string? Farba, int Titulov);

/// <summary>The user's own tags of titles ("Vianoce", "Pre deti"): any number per title, searched with stitok:.</summary>
public static class StitkyTitulov
{
    /// <summary>All tags by name, with their counts.</summary>
    public static IReadOnlyList<StitokTitulov> Zoznam(SqliteConnection spojenie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT s.id, s.nazov, s.farba, (SELECT count(*) FROM titul_stitok ts WHERE ts.stitok_id = s.id)
            FROM stitok s ORDER BY s.kluc, s.id
            """);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<StitokTitulov>();
        while (citac.Read())
        {
            vysledok.Add(new StitokTitulov(citac.GetInt64(0), citac.GetString(1), citac.IsDBNull(2) ? null : citac.GetString(2), citac.GetInt32(3)));
        }

        return vysledok;
    }

    /// <summary>The tags of a title, by name.</summary>
    public static IReadOnlyList<string> Titulu(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT s.nazov FROM titul_stitok ts JOIN stitok s ON s.id = ts.stitok_id WHERE ts.titul_id = $t ORDER BY s.kluc
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<string>();
        while (citac.Read())
        {
            vysledok.Add(citac.GetString(0));
        }

        return vysledok;
    }

    /// <summary>
    /// Sets the tags of titles to exactly these names (new names become tags; letter case of an existing tag wins).
    /// Tags no title has any more disappear.
    /// </summary>
    public static void Nastav(SqliteConnection spojenie, IEnumerable<long> tituly, IEnumerable<string> nazvy)
    {
        var idcka = nazvy.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => Stitok(spojenie, n)).ToList();
        foreach (var titulId in tituly.Distinct())
        {
            using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM titul_stitok WHERE titul_id = $t"))
            {
                zmaz.Parameters.AddWithValue("$t", titulId);
                zmaz.ExecuteNonQuery();
            }

            foreach (var stitokId in idcka)
            {
                Pridaj(spojenie, titulId, stitokId);
            }
        }

        OdstranNepouzite(spojenie);
    }

    /// <summary>Adds one tag to titles (the ones they have stay).</summary>
    public static void Pridaj(SqliteConnection spojenie, IEnumerable<long> tituly, string nazov)
    {
        var stitokId = Stitok(spojenie, nazov.Trim());
        foreach (var titulId in tituly.Distinct())
        {
            Pridaj(spojenie, titulId, stitokId);
        }
    }

    /// <summary>Takes one tag off titles.</summary>
    public static void Odober(SqliteConnection spojenie, IEnumerable<long> tituly, long stitokId)
    {
        foreach (var titulId in tituly.Distinct())
        {
            using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM titul_stitok WHERE titul_id = $t AND stitok_id = $s");
            prikaz.Parameters.AddWithValue("$t", titulId);
            prikaz.Parameters.AddWithValue("$s", stitokId);
            prikaz.ExecuteNonQuery();
        }

        OdstranNepouzite(spojenie);
    }

    /// <summary>Renames a tag; to the name of another tag, the two become one.</summary>
    public static void Premenuj(SqliteConnection spojenie, long stitokId, string nazov)
    {
        nazov = nazov.Trim();
        if (nazov.Length == 0)
        {
            throw new ArgumentException("Štítok potrebuje názov.", nameof(nazov));
        }

        if (Najdi(spojenie, nazov) is { } ine && ine != stitokId)
        {
            using var presun = Kniznica.Prikaz(spojenie, """
                INSERT OR IGNORE INTO titul_stitok(titul_id, stitok_id) SELECT titul_id, $ine FROM titul_stitok WHERE stitok_id = $s;
                DELETE FROM stitok WHERE id = $s;
                """);
            presun.Parameters.AddWithValue("$ine", ine);
            presun.Parameters.AddWithValue("$s", stitokId);
            presun.ExecuteNonQuery();
            return;
        }

        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE stitok SET nazov = $n, kluc = $k WHERE id = $s");
        prikaz.Parameters.AddWithValue("$n", nazov);
        prikaz.Parameters.AddWithValue("$k", Normalizacia.Nazov(nazov));
        prikaz.Parameters.AddWithValue("$s", stitokId);
        prikaz.ExecuteNonQuery();
    }

    public static void Zmaz(SqliteConnection spojenie, long stitokId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM stitok WHERE id = $s");
        prikaz.Parameters.AddWithValue("$s", stitokId);
        prikaz.ExecuteNonQuery();
    }

    private static long? Najdi(SqliteConnection spojenie, string nazov)
    {
        // the name compares without letter case (as the column's collation does) – "Vianoce" is "vianoce"
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT id FROM stitok WHERE nazov = $n");
        prikaz.Parameters.AddWithValue("$n", nazov);
        return prikaz.ExecuteScalar() is long id ? id : null;
    }

    private static long Stitok(SqliteConnection spojenie, string nazov)
    {
        if (Najdi(spojenie, nazov) is { } id)
        {
            return id;
        }

        using var prikaz = Kniznica.Prikaz(spojenie, "INSERT INTO stitok(nazov, kluc) VALUES ($n, $k) RETURNING id");
        prikaz.Parameters.AddWithValue("$n", nazov);
        prikaz.Parameters.AddWithValue("$k", Normalizacia.Nazov(nazov));
        return (long)prikaz.ExecuteScalar()!;
    }

    private static void Pridaj(SqliteConnection spojenie, long titulId, long stitokId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "INSERT OR IGNORE INTO titul_stitok(titul_id, stitok_id) VALUES ($t, $s)");
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.Parameters.AddWithValue("$s", stitokId);
        prikaz.ExecuteNonQuery();
    }

    private static void OdstranNepouzite(SqliteConnection spojenie) =>
        Kniznica.Vykonaj(spojenie, "DELETE FROM stitok WHERE id NOT IN (SELECT stitok_id FROM titul_stitok)");
}
