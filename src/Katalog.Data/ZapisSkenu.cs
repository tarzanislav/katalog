using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>Counts of one written folder (they add up to the scan's record).</summary>
public readonly record struct PocetZmien(int Pridane, int Zmenene, int Zmazane)
{
    public static PocetZmien operator +(PocetZmien a, PocetZmien b) => new(a.Pridane + b.Pridane, a.Zmenene + b.Zmenene, a.Zmazane + b.Zmazane);
}

/// <summary>
/// The library side of a scan: the stored children of a folder, and writing one folder's difference. A write never loses
/// anything: missing entries are marked as deleted with the date (a deleted folder takes its whole subtree along), and an
/// entry that appears again keeps its id, so its versions, watched marks and history stay.
/// </summary>
public static class ZapisSkenu
{
    /// <summary>Stored children (folders and files, also deleted ones) of a folder.</summary>
    public static IReadOnlyList<UlozenaPolozka> UlozeneDeti(SqliteConnection spojenie, long priecinokId)
    {
        var vysledok = new List<UlozenaPolozka>();
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = "SELECT id, nazov, stav FROM priecinok WHERE rodic_id = $p";
            prikaz.Parameters.AddWithValue("$p", priecinokId);
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                vysledok.Add(new UlozenaPolozka(citac.GetInt64(0), citac.GetString(1), true, 0, 0, citac.GetInt32(2) == 1));
            }
        }

        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = "SELECT id, nazov, velkost, zmeneny, stav FROM subor WHERE priecinok_id = $p";
            prikaz.Parameters.AddWithValue("$p", priecinokId);
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                vysledok.Add(new UlozenaPolozka(citac.GetInt64(0), citac.GetString(1), false, citac.GetInt64(2), citac.GetInt64(3), citac.GetInt32(4) == 1));
            }
        }

        return vysledok;
    }

    /// <summary>
    /// Writes one folder's difference (inside the writer). New subfolders are created with the ids in
    /// <paramref name="idNovychPriecinkov"/> (same order as <see cref="RozdielPriecinka.NovePriecinky"/>).
    /// </summary>
    public static PocetZmien Zapis(SqliteConnection spojenie, long diskId, long priecinokId, RozdielPriecinka rozdiel, IReadOnlyList<long> idNovychPriecinkov, long teraz)
    {
        var pridane = 0;
        var zmenene = 0;
        var zmazane = 0;

        if (rozdiel.NovePriecinky.Count > 0)
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "INSERT INTO priecinok(id, disk_id, rodic_id, nazov, zmeneny) VALUES ($id, $disk, $rodic, $nazov, $zmeneny)";
            var id = prikaz.Parameters.Add("$id", SqliteType.Integer);
            prikaz.Parameters.AddWithValue("$disk", diskId);
            prikaz.Parameters.AddWithValue("$rodic", priecinokId);
            var nazov = prikaz.Parameters.Add("$nazov", SqliteType.Text);
            var zmeneny = prikaz.Parameters.Add("$zmeneny", SqliteType.Integer);
            for (var i = 0; i < rozdiel.NovePriecinky.Count; i++)
            {
                id.Value = idNovychPriecinkov[i];
                nazov.Value = rozdiel.NovePriecinky[i].Nazov;
                zmeneny.Value = rozdiel.NovePriecinky[i].Zmeneny;
                prikaz.ExecuteNonQuery();
            }
        }

        foreach (var id in rozdiel.VratenePriecinky)
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "UPDATE priecinok SET stav = 0, zmazany = NULL WHERE id = $id";
            prikaz.Parameters.AddWithValue("$id", id);
            prikaz.ExecuteNonQuery();
        }

        foreach (var id in rozdiel.ZmazanePriecinky)
        {
            // the whole subtree is marked: folders first, then their files (counted as deleted)
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = """
                WITH RECURSIVE strom(id) AS (SELECT $id UNION ALL SELECT p.id FROM priecinok p JOIN strom s ON p.rodic_id = s.id)
                UPDATE priecinok SET stav = 1, zmazany = $teraz WHERE id IN (SELECT id FROM strom) AND stav = 0;
                WITH RECURSIVE strom(id) AS (SELECT $id UNION ALL SELECT p.id FROM priecinok p JOIN strom s ON p.rodic_id = s.id)
                UPDATE subor SET stav = 1, zmazany = $teraz WHERE priecinok_id IN (SELECT id FROM strom) AND stav = 0;
                SELECT changes();
                """;
            prikaz.Parameters.AddWithValue("$id", id);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            zmazane += Convert.ToInt32(prikaz.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (rozdiel.NoveSubory.Count > 0)
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = """
                INSERT INTO subor(priecinok_id, disk_id, nazov, pripona, velkost, zmeneny, vytvoreny, atributy, prvy_raz)
                VALUES ($priecinok, $disk, $nazov, $pripona, $velkost, $zmeneny, $vytvoreny, $atributy, $teraz)
                """;
            prikaz.Parameters.AddWithValue("$priecinok", priecinokId);
            prikaz.Parameters.AddWithValue("$disk", diskId);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            var p = Parametre(prikaz);
            foreach (var subor in rozdiel.NoveSubory)
            {
                p.Nastav(subor);
                prikaz.ExecuteNonQuery();
                pridane++;
            }
        }

        foreach (var (id, subor) in rozdiel.ZmeneneSubory)
        {
            ZabudniObsah(spojenie, id, subor, ajAkSaNezmenil: true);
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = """
                UPDATE subor SET nazov = $nazov, pripona = $pripona, velkost = $velkost, zmeneny = $zmeneny, vytvoreny = $vytvoreny,
                                 atributy = $atributy, odtlacok = NULL
                WHERE id = $id
                """;
            prikaz.Parameters.AddWithValue("$id", id);
            Parametre(prikaz).Nastav(subor);
            prikaz.ExecuteNonQuery();
            zmenene++;
        }

        foreach (var (id, subor) in rozdiel.VrateneSubory)
        {
            // a file back after a deletion keeps its fingerprint and media data only when it is the same file
            ZabudniObsah(spojenie, id, subor, ajAkSaNezmenil: false);
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = """
                UPDATE subor SET stav = 0, zmazany = NULL,
                                 odtlacok = CASE WHEN velkost = $velkost AND zmeneny = $zmeneny THEN odtlacok END,
                                 nazov = $nazov, pripona = $pripona, velkost = $velkost, zmeneny = $zmeneny, vytvoreny = $vytvoreny,
                                 atributy = $atributy
                WHERE id = $id
                """;
            prikaz.Parameters.AddWithValue("$id", id);
            Parametre(prikaz).Nastav(subor);
            prikaz.ExecuteNonQuery();
            pridane++;
        }

        if (rozdiel.ZmazaneSubory.Count > 0)
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "UPDATE subor SET stav = 1, zmazany = $teraz WHERE id = $id AND stav = 0";
            var id = prikaz.Parameters.Add("$id", SqliteType.Integer);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            foreach (var subor in rozdiel.ZmazaneSubory)
            {
                id.Value = subor;
                zmazane += prikaz.ExecuteNonQuery();
            }
        }

        return new PocetZmien(pridane, zmenene, zmazane);
    }

    /// <summary>Forgets what was read from a file's content (media data, tracks, previews) when the file changed.</summary>
    private static void ZabudniObsah(SqliteConnection spojenie, long id, PolozkaUloziska subor, bool ajAkSaNezmenil)
    {
        using var prikaz = spojenie.CreateCommand();
        var podmienka = ajAkSaNezmenil
            ? "1"
            : "EXISTS (SELECT 1 FROM subor WHERE id = $id AND (velkost <> $velkost OR zmeneny <> $zmeneny))";
        prikaz.CommandText = $"""
            DELETE FROM medialne_udaje WHERE subor_id = $id AND {podmienka};
            DELETE FROM stopa WHERE subor_id = $id AND {podmienka};
            DELETE FROM nahlad WHERE subor_id = $id AND {podmienka};
            """;
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$velkost", subor.Velkost);
        prikaz.Parameters.AddWithValue("$zmeneny", subor.Zmeneny);
        prikaz.ExecuteNonQuery();
    }

    private static ParametreSuboru Parametre(SqliteCommand prikaz) => new(
        prikaz.Parameters.Add("$nazov", SqliteType.Text),
        prikaz.Parameters.Add("$pripona", SqliteType.Text),
        prikaz.Parameters.Add("$velkost", SqliteType.Integer),
        prikaz.Parameters.Add("$zmeneny", SqliteType.Integer),
        prikaz.Parameters.Add("$vytvoreny", SqliteType.Integer),
        prikaz.Parameters.Add("$atributy", SqliteType.Integer));

    private sealed record ParametreSuboru(
        SqliteParameter Nazov, SqliteParameter Pripona, SqliteParameter Velkost, SqliteParameter Zmeneny, SqliteParameter Vytvoreny, SqliteParameter Atributy)
    {
        public void Nastav(PolozkaUloziska subor)
        {
            Nazov.Value = subor.Nazov;
            Pripona.Value = NazovSuboru.Pripona(subor.Nazov);
            Velkost.Value = subor.Velkost;
            Zmeneny.Value = subor.Zmeneny;
            Vytvoreny.Value = (object?)subor.Vytvoreny ?? DBNull.Value;
            Atributy.Value = subor.Atributy;
        }
    }
}
