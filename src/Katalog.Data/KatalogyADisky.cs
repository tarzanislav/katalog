using System.Globalization;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>Data of a new disk: where its volume is and how the user names it.</summary>
public sealed record NovyDisk(long KatalogId, string Nazov, IdentitaZvazku Identita, string Koren, long? Volne, int? Cislo = null);

/// <summary>
/// Catalogues and disks of the library, and browsing their folders without the disk being connected. Reads take any
/// connection; methods that change something are meant to run inside <see cref="ZapisovaciaFronta"/>.
/// </summary>
public static class KatalogyADisky
{
    private const string StlpceDisku = """
        id, katalog_id, cislo, nazov, druh, seriove_cislo_zvazku, guid_zvazku, seriove_cislo_disku, menovka, suborovy_system,
        velkost, volne, adresa, koren, umiestnenie, poznamka, pridany, posledny_sken, posledne_pripojenie
        """;

    // ------------------------------------------------------------------ catalogues

    public static IReadOnlyList<KatalogKniznice> Katalogy(SqliteConnection spojenie)
    {
        using var prikaz = Prikaz(spojenie, "SELECT id, nazov, farba, poradie FROM katalog ORDER BY poradie, nazov COLLATE NOCASE");
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<KatalogKniznice>();
        while (citac.Read())
        {
            vysledok.Add(new KatalogKniznice(citac.GetInt64(0), citac.GetString(1), citac.IsDBNull(2) ? null : citac.GetString(2), citac.GetInt32(3)));
        }

        return vysledok;
    }

    public static long VytvorKatalog(SqliteConnection spojenie, string nazov)
    {
        using var prikaz = Prikaz(spojenie, """
            INSERT INTO katalog(nazov, poradie, vytvoreny)
            VALUES ($nazov, (SELECT coalesce(max(poradie), 0) + 1 FROM katalog), $teraz) RETURNING id
            """);
        prikaz.Parameters.AddWithValue("$nazov", nazov.Trim());
        prikaz.Parameters.AddWithValue("$teraz", Cas.Teraz());
        return Convert.ToInt64(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static void PremenujKatalog(SqliteConnection spojenie, long id, string nazov)
    {
        using var prikaz = Prikaz(spojenie, "UPDATE katalog SET nazov = $nazov WHERE id = $id");
        prikaz.Parameters.AddWithValue("$nazov", nazov.Trim());
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Removes a catalogue with all its disks, folders and files from the library (the disks themselves stay untouched).</summary>
    public static void ZmazKatalog(SqliteConnection spojenie, long id)
    {
        using var prikaz = Prikaz(spojenie, "DELETE FROM katalog WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ disks

    public static IReadOnlyList<Disk> Disky(SqliteConnection spojenie)
    {
        using var prikaz = Prikaz(spojenie, $"SELECT {StlpceDisku} FROM disk ORDER BY cislo");
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Disk>();
        while (citac.Read())
        {
            vysledok.Add(CitajDisk(citac));
        }

        return vysledok;
    }

    public static Disk? NajdiDisk(SqliteConnection spojenie, long id)
    {
        using var prikaz = Prikaz(spojenie, $"SELECT {StlpceDisku} FROM disk WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        using var citac = prikaz.ExecuteReader();
        return citac.Read() ? CitajDisk(citac) : null;
    }

    /// <summary>The next disk number for a label (one above the highest; numbers of removed disks are not reused).</summary>
    public static int DalsieCislo(SqliteConnection spojenie) =>
        Convert.ToInt32(Skalar(spojenie, "SELECT coalesce(max(cislo), 0) + 1 FROM disk"), CultureInfo.InvariantCulture);

    /// <summary>Adds a disk and its root folder (its id from <paramref name="idKorena"/>); returns the disk's id.</summary>
    public static long PridajDisk(SqliteConnection spojenie, NovyDisk disk, long idKorena)
    {
        var i = disk.Identita;
        using var prikaz = Prikaz(spojenie, """
            INSERT INTO disk(katalog_id, cislo, nazov, druh, seriove_cislo_zvazku, guid_zvazku, seriove_cislo_disku, menovka,
                             suborovy_system, velkost, volne, adresa, koren, pridany, posledne_pripojenie)
            VALUES ($katalog, coalesce($cislo, (SELECT coalesce(max(cislo), 0) + 1 FROM disk)), $nazov, $druh, $serZvazku, $guid,
                    $serDisku, $menovka, $fs, $velkost, $volne, $adresa, $koren, $teraz, $teraz)
            RETURNING id
            """);
        var teraz = Cas.Teraz();
        prikaz.Parameters.AddWithValue("$katalog", disk.KatalogId);
        prikaz.Parameters.AddWithValue("$cislo", (object?)disk.Cislo ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$nazov", disk.Nazov.Trim());
        prikaz.Parameters.AddWithValue("$druh", (int)i.Druh);
        prikaz.Parameters.AddWithValue("$serZvazku", (object?)i.SerioveCisloZvazku ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$guid", (object?)i.GuidZvazku ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$serDisku", (object?)i.SerioveCisloDisku ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$menovka", (object?)i.Menovka ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$fs", (object?)i.SuborovySystem ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$velkost", (object?)i.Velkost ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$volne", (object?)disk.Volne ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$adresa", (object?)i.Adresa ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$koren", disk.Koren);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        var id = Convert.ToInt64(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var koren = Prikaz(spojenie, "INSERT INTO priecinok(id, disk_id, rodic_id, nazov) VALUES ($id, $disk, NULL, '')");
        koren.Parameters.AddWithValue("$id", idKorena);
        koren.Parameters.AddWithValue("$disk", id);
        koren.ExecuteNonQuery();
        return id;
    }

    /// <summary>Changes what the user may change about a disk: name, catalogue, number, place, note.</summary>
    public static void UpravDisk(SqliteConnection spojenie, long id, string nazov, long katalogId, int cislo, string? umiestnenie, string? poznamka)
    {
        using var prikaz = Prikaz(spojenie, """
            UPDATE disk SET nazov = $nazov, katalog_id = $katalog, cislo = $cislo, umiestnenie = $umiestnenie, poznamka = $poznamka
            WHERE id = $id
            """);
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$nazov", nazov.Trim());
        prikaz.Parameters.AddWithValue("$katalog", katalogId);
        prikaz.Parameters.AddWithValue("$cislo", cislo);
        prikaz.Parameters.AddWithValue("$umiestnenie", string.IsNullOrWhiteSpace(umiestnenie) ? DBNull.Value : umiestnenie.Trim());
        prikaz.Parameters.AddWithValue("$poznamka", string.IsNullOrWhiteSpace(poznamka) ? DBNull.Value : poznamka.Trim());
        prikaz.ExecuteNonQuery();
    }

    /// <summary>
    /// Records that the disk is connected now: the time, the free space and what may change on the volume (its label, size).
    /// The stored identity parts are kept; missing ones are filled in.
    /// </summary>
    public static void ZapisPripojenie(SqliteConnection spojenie, long id, IdentitaZvazku identita, long? volne)
    {
        using var prikaz = Prikaz(spojenie, """
            UPDATE disk SET posledne_pripojenie = $teraz, volne = coalesce($volne, volne), menovka = coalesce($menovka, menovka),
                            velkost = coalesce($velkost, velkost), guid_zvazku = coalesce(guid_zvazku, $guid),
                            seriove_cislo_disku = coalesce(seriove_cislo_disku, $serDisku), druh = CASE WHEN druh = 0 THEN $druh ELSE druh END
            WHERE id = $id
            """);
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$teraz", Cas.Teraz());
        prikaz.Parameters.AddWithValue("$volne", (object?)volne ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$menovka", (object?)identita.Menovka ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$velkost", (object?)identita.Velkost ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$guid", (object?)identita.GuidZvazku ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$serDisku", (object?)identita.SerioveCisloDisku ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$druh", (int)identita.Druh);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Removes a disk and everything known about its files from the library (the disk itself stays untouched).</summary>
    public static void ZmazDisk(SqliteConnection spojenie, long id)
    {
        using var prikaz = Prikaz(spojenie, "DELETE FROM disk WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ browsing

    public static long? Koren(SqliteConnection spojenie, long diskId)
    {
        using var prikaz = Prikaz(spojenie, "SELECT id FROM priecinok WHERE disk_id = $disk AND rodic_id IS NULL");
        prikaz.Parameters.AddWithValue("$disk", diskId);
        return prikaz.ExecuteScalar() is long id ? id : null;
    }

    public static IReadOnlyList<Priecinok> Podpriecinky(SqliteConnection spojenie, long priecinokId, bool ajZmazane = false)
    {
        using var prikaz = Prikaz(spojenie, """
            SELECT p.id, p.disk_id, p.rodic_id, p.nazov, p.stav,
                   EXISTS (SELECT 1 FROM priecinok d WHERE d.rodic_id = p.id AND d.stav = 0)
            FROM priecinok p
            WHERE p.rodic_id = $rodic AND ($aj OR p.stav = 0)
            ORDER BY p.nazov COLLATE NOCASE
            """);
        prikaz.Parameters.AddWithValue("$rodic", priecinokId);
        prikaz.Parameters.AddWithValue("$aj", ajZmazane);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Priecinok>();
        while (citac.Read())
        {
            vysledok.Add(new Priecinok(citac.GetInt64(0), citac.GetInt64(1), citac.GetInt64(2), citac.GetString(3), citac.GetInt32(4) == 1, citac.GetBoolean(5)));
        }

        return vysledok;
    }

    public static IReadOnlyList<Subor> Subory(SqliteConnection spojenie, long priecinokId, bool ajZmazane = false)
    {
        using var prikaz = Prikaz(spojenie, """
            SELECT id, priecinok_id, nazov, pripona, velkost, zmeneny, atributy, stav
            FROM subor
            WHERE priecinok_id = $priecinok AND ($aj OR stav = 0)
            ORDER BY nazov COLLATE NOCASE
            """);
        prikaz.Parameters.AddWithValue("$priecinok", priecinokId);
        prikaz.Parameters.AddWithValue("$aj", ajZmazane);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Subor>();
        while (citac.Read())
        {
            vysledok.Add(new Subor(citac.GetInt64(0), citac.GetInt64(1), citac.GetString(2), citac.GetString(3), citac.GetInt64(4),
                citac.GetInt64(5), citac.GetInt32(6), citac.GetInt32(7) == 1));
        }

        return vysledok;
    }

    /// <summary>Path of a folder inside its disk ("Filmy/Nové"; "" for the root).</summary>
    public static string Cesta(SqliteConnection spojenie, long priecinokId)
    {
        using var prikaz = Prikaz(spojenie, """
            WITH RECURSIVE hore(id, rodic_id, nazov, hlbka) AS (
              SELECT id, rodic_id, nazov, 0 FROM priecinok WHERE id = $id
              UNION ALL
              SELECT p.id, p.rodic_id, p.nazov, h.hlbka + 1 FROM priecinok p JOIN hore h ON p.id = h.rodic_id
            )
            SELECT nazov FROM hore WHERE nazov <> '' ORDER BY hlbka DESC
            """);
        prikaz.Parameters.AddWithValue("$id", priecinokId);
        using var citac = prikaz.ExecuteReader();
        var casti = new List<string>();
        while (citac.Read())
        {
            casti.Add(citac.GetString(0));
        }

        return string.Join('/', casti);
    }

    /// <summary>What a disk holds now (files and folders that are not deleted).</summary>
    public static Suhrn SuhrnDisku(SqliteConnection spojenie, long diskId)
    {
        using var prikaz = Prikaz(spojenie, """
            SELECT (SELECT count(*) FROM subor WHERE disk_id = $disk AND stav = 0),
                   (SELECT count(*) FROM priecinok WHERE disk_id = $disk AND stav = 0 AND rodic_id IS NOT NULL),
                   (SELECT coalesce(sum(velkost), 0) FROM subor WHERE disk_id = $disk AND stav = 0)
            """);
        prikaz.Parameters.AddWithValue("$disk", diskId);
        using var citac = prikaz.ExecuteReader();
        citac.Read();
        return new Suhrn(citac.GetInt64(0), citac.GetInt64(1), citac.GetInt64(2));
    }

    private static Disk CitajDisk(SqliteDataReader c)
    {
        string? Text(int i) => c.IsDBNull(i) ? null : c.GetString(i);
        long? Cislo(int i) => c.IsDBNull(i) ? null : c.GetInt64(i);
        var identita = new IdentitaZvazku((DruhDisku)c.GetInt32(4), Text(5), Text(6), Text(7), Text(8), Text(9), Cislo(10), Text(12));
        return new Disk(c.GetInt64(0), c.GetInt64(1), c.GetInt32(2), c.GetString(3), identita, c.GetString(13), Cislo(11), Text(14), Text(15),
            c.GetInt64(16), Cislo(17), Cislo(18));
    }

    private static SqliteCommand Prikaz(SqliteConnection spojenie, string sql)
    {
        var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        return prikaz;
    }

    private static object? Skalar(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Prikaz(spojenie, sql);
        return prikaz.ExecuteScalar();
    }
}
