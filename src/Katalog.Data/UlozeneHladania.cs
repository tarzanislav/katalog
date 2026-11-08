using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A saved search: a name and the text of its query (the query language, decision A4).</summary>
public sealed record UlozeneHladanie(long Id, string Nazov, string Dotaz, int Poradie);

/// <summary>Saved searches of the library, in the order the user keeps them.</summary>
public static class UlozeneHladania
{
    public static IReadOnlyList<UlozeneHladanie> Zoznam(SqliteConnection spojenie)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT id, nazov, dotaz, poradie FROM ulozene_hladanie ORDER BY poradie, nazov COLLATE NOCASE";
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<UlozeneHladanie>();
        while (citac.Read())
        {
            vysledok.Add(new UlozeneHladanie(citac.GetInt64(0), citac.GetString(1), citac.GetString(2), citac.GetInt32(3)));
        }

        return vysledok;
    }

    /// <summary>Saves a query under a name; a search of the same name (in any letter case) gets the new query.</summary>
    public static long Uloz(SqliteConnection spojenie, string nazov, string dotaz)
    {
        var meno = nazov.Trim();
        ArgumentException.ThrowIfNullOrEmpty(meno, nameof(nazov));
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            INSERT INTO ulozene_hladanie(nazov, dotaz, poradie)
            VALUES ($nazov, $dotaz, (SELECT coalesce(max(poradie), 0) + 1 FROM ulozene_hladanie))
            ON CONFLICT(nazov) DO UPDATE SET dotaz = excluded.dotaz
            RETURNING id
            """;
        prikaz.Parameters.AddWithValue("$nazov", meno);
        prikaz.Parameters.AddWithValue("$dotaz", dotaz.Trim());
        return (long)prikaz.ExecuteScalar()!;
    }

    public static void Premenuj(SqliteConnection spojenie, long id, string nazov)
    {
        var meno = nazov.Trim();
        ArgumentException.ThrowIfNullOrEmpty(meno, nameof(nazov));
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "UPDATE ulozene_hladanie SET nazov = $nazov WHERE id = $id";
        prikaz.Parameters.AddWithValue("$nazov", meno);
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    public static void Zmaz(SqliteConnection spojenie, long id)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "DELETE FROM ulozene_hladanie WHERE id = $id";
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }
}
