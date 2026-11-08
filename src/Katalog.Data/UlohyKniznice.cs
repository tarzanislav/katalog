using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A job of the queue as the library keeps it.</summary>
public sealed record Uloha(long Id, DruhUlohy Druh, StavUlohy Stav, string Pruh, long? DiskId, double Priebeh, string? Chyba, int Pokusy);

/// <summary>
/// The persistent job queue (decision A7): what is to be done stays in the library, so a scan or reading of technical
/// data interrupted by closing the program goes on the next time its disk is connected.
/// </summary>
public static class UlohyKniznice
{
    /// <summary>A job of the kind for the disk; the one already waiting (or running) when there is one.</summary>
    public static long Naplanuj(SqliteConnection spojenie, DruhUlohy druh, string pruh, long? diskId, long teraz, int priorita = 0)
    {
        using (var hladaj = spojenie.CreateCommand())
        {
            hladaj.CommandText = "SELECT id FROM uloha WHERE druh = $druh AND disk_id IS $disk AND stav IN (0, 1, 5) ORDER BY id LIMIT 1";
            hladaj.Parameters.AddWithValue("$druh", (int)druh);
            hladaj.Parameters.AddWithValue("$disk", (object?)diskId ?? DBNull.Value);
            if (hladaj.ExecuteScalar() is long existujuca)
            {
                return existujuca;
            }
        }

        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            INSERT INTO uloha(druh, stav, priorita, pruh, disk_id, vytvorena) VALUES ($druh, 0, $priorita, $pruh, $disk, $teraz) RETURNING id
            """;
        prikaz.Parameters.AddWithValue("$druh", (int)druh);
        prikaz.Parameters.AddWithValue("$priorita", priorita);
        prikaz.Parameters.AddWithValue("$pruh", pruh);
        prikaz.Parameters.AddWithValue("$disk", (object?)diskId ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        return (long)prikaz.ExecuteScalar()!;
    }

    public static void Zacni(SqliteConnection spojenie, long id, long teraz) =>
        Vykonaj(spojenie, "UPDATE uloha SET stav = 1, zacala = $teraz, pokusy = pokusy + 1, chyba = NULL WHERE id = $id", id, teraz);

    public static void Priebeh(SqliteConnection spojenie, long id, double priebeh)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "UPDATE uloha SET priebeh = $p WHERE id = $id";
        prikaz.Parameters.AddWithValue("$p", Math.Clamp(priebeh, 0, 1));
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>
    /// The end of a run: done, failed, cancelled by the user, or back to waiting (its disk was disconnected, or the
    /// program closes – it goes on next time).
    /// </summary>
    public static void Skonci(SqliteConnection spojenie, long id, StavUlohy stav, string? chyba, long teraz)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            UPDATE uloha SET stav = $stav, chyba = $chyba,
                skoncila = CASE WHEN $stav IN (2, 3, 4) THEN $teraz ELSE NULL END,
                priebeh = CASE WHEN $stav = 2 THEN 1 ELSE priebeh END
            WHERE id = $id
            """;
        prikaz.Parameters.AddWithValue("$stav", (int)stav);
        prikaz.Parameters.AddWithValue("$chyba", (object?)chyba ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>When the library opens: jobs that were running when the program ended wait again.</summary>
    public static int ObnovPrerusene(SqliteConnection spojenie)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "UPDATE uloha SET stav = 0 WHERE stav = 1";
        return prikaz.ExecuteNonQuery();
    }

    /// <summary>Jobs waiting to run (also those waiting for their disk), oldest first.</summary>
    public static IReadOnlyList<Uloha> Cakajuce(SqliteConnection spojenie)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            SELECT id, druh, stav, pruh, disk_id, priebeh, chyba, pokusy FROM uloha WHERE stav IN (0, 1, 5) ORDER BY priorita DESC, id
            """;
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Uloha>();
        while (citac.Read())
        {
            vysledok.Add(new Uloha(citac.GetInt64(0), (DruhUlohy)citac.GetInt32(1), (StavUlohy)citac.GetInt32(2), citac.GetString(3),
                citac.IsDBNull(4) ? null : citac.GetInt64(4), citac.GetDouble(5), citac.IsDBNull(6) ? null : citac.GetString(6), citac.GetInt32(7)));
        }

        return vysledok;
    }

    /// <summary>Finished jobs older than the given time go away (the history of scans stays in the table sken).</summary>
    public static int Vycisti(SqliteConnection spojenie, long starsieAko)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "DELETE FROM uloha WHERE stav IN (2, 3, 4) AND skoncila < $cas";
        prikaz.Parameters.AddWithValue("$cas", starsieAko);
        return prikaz.ExecuteNonQuery();
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql, long id, long teraz)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.ExecuteNonQuery();
    }
}
