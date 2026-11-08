using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>
/// The full-text index of all names in the library (files, folders, titles, episodes, people, collections, disks).
/// Trigram tokens: any part of a word of three or more letters is found, with or without diacritics. The query language
/// (stage E2) builds on this; here are the basic search and its quoting.
/// </summary>
public static class Fulltext
{
    /// <summary>Shortest text the index can search (one trigram).</summary>
    public const int NajkratsiText = 3;

    /// <summary>
    /// Objects whose name contains every word of <paramref name="text"/> (in any order). Not ordered by relevance: ranking
    /// every match of a common word costs more than the 100 ms the typing allows; the query language (E2) orders the
    /// limited result by kind and name.
    /// </summary>
    public static IReadOnlyList<(DruhHladania Druh, long Id)> Najdi(SqliteConnection spojenie, string text, int najviac = 200)
    {
        var vyraz = Vyraz(text);
        if (vyraz is null)
        {
            return [];
        }

        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT rowid FROM hladanie WHERE hladanie MATCH $vyraz LIMIT $najviac";
        prikaz.Parameters.AddWithValue("$vyraz", vyraz);
        prikaz.Parameters.AddWithValue("$najviac", najviac);
        var vysledok = new List<(DruhHladania, long)>();
        using var citac = prikaz.ExecuteReader();
        while (citac.Read())
        {
            vysledok.Add(KlucHladania.Rozloz(citac.GetInt64(0)));
        }

        return vysledok;
    }

    /// <summary>
    /// FTS5 expression for typed text: each word becomes a quoted phrase (so no character of the text can act as an FTS
    /// operator), words shorter than a trigram are left out; null when nothing searchable remains.
    /// </summary>
    public static string? Vyraz(string text)
    {
        var slova = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Length >= NajkratsiText)
            .Select(s => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")
            .ToList();
        return slova.Count == 0 ? null : string.Join(" AND ", slova);
    }
}
