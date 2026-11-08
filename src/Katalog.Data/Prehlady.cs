using System.Globalization;
using Katalog.Jadro.Model;
using Katalog.Jadro.Zdravie;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>The last health reading of a disk (SMART).</summary>
public sealed record ZdravieDisku(long Kedy, StavZdravia Stav, int? Teplota, int? Hodiny, int? Realokovane, int? Cakajuce, int? Opotrebenie);

/// <summary>One disk in the overview: its space, contents and health.</summary>
/// <param name="Tituly">Titles with a present version on this disk.</param>
/// <param name="LenTu">Of them, titles that lie on no other disk (N1: lost with this disk).</param>
public sealed record RiadokPrehladu(
    Disk Disk, string Katalog, long Suborov, long VelkostSuborov, int Videi, int Tituly, int LenTu, ZdravieDisku? Zdravie);

/// <summary>A count in a statistic ("2010 – 2019": 312).</summary>
public sealed record PolozkaStatistiky(string Nazov, long Pocet);

/// <summary>Numbers about the library (N5).</summary>
/// <param name="Hodiny">Hours of watching of all the films and episodes the user has (each title once).</param>
public sealed record Statistiky(
    int Filmy, int Serialy, int Epizody, double Hodiny, long Suborov, long Velkost, int Diskov,
    IReadOnlyList<PolozkaStatistiky> Desatrocia, IReadOnlyList<PolozkaStatistiky> Zanre, IReadOnlyList<PolozkaStatistiky> Kvalita,
    IReadOnlyList<PolozkaStatistiky> Disky);

/// <summary>
/// Overviews (stage E5): disks with their space, contents and health, where new files fit (N2), statistics of the library
/// (N5) and the health readings of disks.
/// </summary>
public static class Prehlady
{
    /// <summary>Every disk with what lies on it, in the order of their numbers.</summary>
    public static IReadOnlyList<RiadokPrehladu> Disky(SqliteConnection spojenie)
    {
        var disky = KatalogyADisky.Disky(spojenie);
        var katalogy = KatalogyADisky.Katalogy(spojenie).ToDictionary(k => k.Id, k => k.Nazov);
        var pocty = new Dictionary<long, (long Suborov, long Velkost, int Videi)>();
        using (var prikaz = Kniznica.Prikaz(spojenie, $"""
                   SELECT disk_id, count(*), coalesce(sum(velkost), 0),
                          count(*) FILTER (WHERE pripona IN ({string.Join(", ", DruhySuborov.Pripony(DruhSuboru.Video).Select(p => "'" + p + "'"))}))
                   FROM subor WHERE stav = 0 GROUP BY disk_id
                   """))
        {
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                pocty[c.GetInt64(0)] = (c.GetInt64(1), c.GetInt64(2), c.GetInt32(3));
            }
        }

        // titles by disk, and on how many disks each title is (a film by its versions, a series by each episode)
        var tituly = new Dictionary<long, (int Tituly, int LenTu)>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   WITH kde AS (
                     SELECT DISTINCT coalesce(v.titul_id, e.titul_id) AS titul_id, coalesce(v.epizoda_id, 0) AS epizoda_id, s.disk_id
                     FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id
                     JOIN verzia_subor vs ON vs.verzia_id = v.id JOIN subor s ON s.id = vs.subor_id
                     WHERE s.stav = 0
                   ),
                   pocet AS (SELECT titul_id, epizoda_id, count(*) AS diskov FROM kde GROUP BY titul_id, epizoda_id)
                   SELECT kde.disk_id, count(DISTINCT kde.titul_id),
                          count(DISTINCT kde.titul_id) FILTER (WHERE pocet.diskov = 1)
                   FROM kde JOIN pocet ON pocet.titul_id = kde.titul_id AND pocet.epizoda_id = kde.epizoda_id
                   GROUP BY kde.disk_id
                   """))
        {
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                tituly[c.GetInt64(0)] = (c.GetInt32(1), c.GetInt32(2));
            }
        }

        var zdravie = PosledneZdravie(spojenie);
        return disky.Select(d =>
        {
            var (suborov, velkost, videi) = pocty.GetValueOrDefault(d.Id);
            var (t, lenTu) = tituly.GetValueOrDefault(d.Id);
            return new RiadokPrehladu(d, katalogy.GetValueOrDefault(d.KatalogId, ""), suborov, velkost, videi, t, lenTu, zdravie.GetValueOrDefault(d.Id));
        }).ToList();
    }

    /// <summary>
    /// Where <paramref name="potrebne"/> bytes fit (N2): disks with enough free space at their last scan, the tightest fit
    /// first (big free disks stay free for big things); connected disks first among equals.
    /// </summary>
    public static IReadOnlyList<RiadokPrehladu> KamSaZmesti(IEnumerable<RiadokPrehladu> disky, long potrebne, IReadOnlySet<long> pripojene) =>
        disky.Where(d => d.Disk.Volne is { } v && v >= potrebne)
            .OrderBy(d => d.Disk.Volne!.Value - potrebne)
            .ThenByDescending(d => pripojene.Contains(d.Disk.Id))
            .ToList();

    /// <summary>The numbers of the library (N5).</summary>
    public static Statistiky Spocitaj(SqliteConnection spojenie)
    {
        int Cislo(string sql) => Convert.ToInt32(Skalar(spojenie, sql), CultureInfo.InvariantCulture);
        var pritomnaVerzia = "EXISTS (SELECT 1 FROM verzia_subor vs JOIN subor s ON s.id = vs.subor_id WHERE vs.verzia_id = v.id AND s.stav = 0)";
        var filmy = Cislo($"SELECT count(DISTINCT v.titul_id) FROM verzia v WHERE v.titul_id IS NOT NULL AND {pritomnaVerzia}");
        var epizody = Cislo($"SELECT count(DISTINCT v.epizoda_id) FROM verzia v WHERE v.epizoda_id IS NOT NULL AND {pritomnaVerzia}");
        var serialy = Cislo($"SELECT count(DISTINCT e.titul_id) FROM verzia v JOIN epizoda e ON e.id = v.epizoda_id WHERE {pritomnaVerzia}");

        // a title (or episode) counts once with its longest present version
        var minuty = Convert.ToDouble(Skalar(spojenie, $"""
            SELECT coalesce(sum(ms), 0) / 60000.0 FROM (
              SELECT coalesce(v.titul_id, -v.epizoda_id) AS kto, max(dlzka) AS ms FROM (
                SELECT v.id, sum(m.dlzka_ms) AS dlzka FROM verzia v JOIN verzia_subor vs ON vs.verzia_id = v.id
                JOIN subor s ON s.id = vs.subor_id AND s.stav = 0 JOIN medialne_udaje m ON m.subor_id = s.id GROUP BY v.id
              ) d JOIN verzia v ON v.id = d.id GROUP BY kto)
            """), CultureInfo.InvariantCulture);
        var suborov = Convert.ToInt64(Skalar(spojenie, "SELECT count(*) FROM subor WHERE stav = 0"), CultureInfo.InvariantCulture);
        var velkost = Convert.ToInt64(Skalar(spojenie, "SELECT coalesce(sum(velkost), 0) FROM subor WHERE stav = 0"), CultureInfo.InvariantCulture);
        var diskov = Cislo("SELECT count(*) FROM disk");

        var titulyKtoreMam = $"""
            SELECT t.* FROM titul t WHERE EXISTS (SELECT 1 FROM verzia v WHERE v.titul_id = t.id AND {pritomnaVerzia})
               OR EXISTS (SELECT 1 FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = t.id AND {pritomnaVerzia})
            """;
        var desatrocia = Zoznam(spojenie, $"""
            SELECT (t.rok / 10 * 10) || ' – ' || (t.rok / 10 * 10 + 9), count(*) FROM ({titulyKtoreMam}) t
            WHERE t.rok IS NOT NULL GROUP BY t.rok / 10 ORDER BY t.rok / 10
            """);
        var zanre = Zoznam(spojenie, $"""
            SELECT z.nazov, count(*) FROM ({titulyKtoreMam}) t JOIN titul_zaner tz ON tz.titul_id = t.id JOIN zaner z ON z.id = tz.zaner_id
            GROUP BY z.id ORDER BY 2 DESC, 1 LIMIT 15
            """);
        var kvalita = Zoznam(spojenie, $"""
            SELECT CASE trieda WHEN 4 THEN '4K' WHEN 3 THEN 'FHD' WHEN 2 THEN 'HD' WHEN 1 THEN 'SD' ELSE 'nezistená' END, count(*) FROM (
              SELECT coalesce(v.titul_id, e.titul_id) AS titul, max(coalesce(m.trieda_kvality, 0)) AS trieda
              FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id JOIN verzia_subor vs ON vs.verzia_id = v.id
              JOIN subor s ON s.id = vs.subor_id AND s.stav = 0 LEFT JOIN medialne_udaje m ON m.subor_id = s.id
              GROUP BY titul)
            GROUP BY trieda ORDER BY trieda DESC
            """);
        var disky = Zoznam(spojenie, """
            SELECT printf('D%03d %s', d.cislo, d.nazov), count(DISTINCT coalesce(v.titul_id, e.titul_id))
            FROM disk d JOIN subor s ON s.disk_id = d.id AND s.stav = 0 JOIN verzia_subor vs ON vs.subor_id = s.id
            JOIN verzia v ON v.id = vs.verzia_id LEFT JOIN epizoda e ON e.id = v.epizoda_id
            GROUP BY d.id ORDER BY d.cislo
            """);
        return new Statistiky(filmy, serialy, epizody, Math.Round(minuty / 60, 1), suborov, velkost, diskov, desatrocia, zanre, kvalita, disky);
    }

    /// <summary>
    /// Which disks of the library the helper's readings belong to: by the serial number of the physical disk (all the
    /// volumes and folders of one physical disk share its health). Readings of disks the library does not know are left out.
    /// </summary>
    public static IReadOnlyList<(long DiskId, VysledokZdravia Zdravie)> Prirad(IEnumerable<Disk> disky, IEnumerable<VysledokZdravia> vysledky)
    {
        var podlaSerioveho = vysledky.Where(v => !string.IsNullOrWhiteSpace(v.Seriove))
            .GroupBy(v => v.Seriove!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return disky
            .Where(d => d.Identita.SerioveCisloDisku is { Length: > 0 } s && podlaSerioveho.ContainsKey(s.Trim()))
            .Select(d => (d.Id, podlaSerioveho[d.Identita.SerioveCisloDisku!.Trim()]))
            .ToList();
    }

    /// <summary>A reading of the helper as it is kept (its raw numbers go along as JSON).</summary>
    public static ZdravieDisku ZoVysledku(VysledokZdravia v, long kedy) =>
        new(kedy, (StavZdravia)Math.Clamp(v.Stav, 0, 3), v.Teplota, v.Hodiny, v.Realokovane, v.Cakajuce, v.Opotrebenie);

    /// <summary>Writes a health reading of a disk.</summary>
    public static void ZapisZdravie(SqliteConnection spojenie, long diskId, ZdravieDisku zdravie, string? surove)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT INTO zdravie_zaznam(disk_id, kedy, teplota, hodiny, realokovane, cakajuce, opotrebenie, stav, surove)
            VALUES ($d, $k, $t, $h, $r, $c, $o, $s, $j)
            """);
        prikaz.Parameters.AddWithValue("$d", diskId);
        prikaz.Parameters.AddWithValue("$k", zdravie.Kedy);
        prikaz.Parameters.AddWithValue("$t", (object?)zdravie.Teplota ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$h", (object?)zdravie.Hodiny ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$r", (object?)zdravie.Realokovane ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$c", (object?)zdravie.Cakajuce ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$o", zdravie.Opotrebenie is >= 0 and <= 100 ? zdravie.Opotrebenie : DBNull.Value);
        prikaz.Parameters.AddWithValue("$s", (int)zdravie.Stav);
        prikaz.Parameters.AddWithValue("$j", (object?)surove ?? DBNull.Value);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>The health readings of a disk, newest first (its history).</summary>
    public static IReadOnlyList<ZdravieDisku> HistoriaZdravia(SqliteConnection spojenie, long diskId, int najviac = 50)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT kedy, stav, teplota, hodiny, realokovane, cakajuce, opotrebenie FROM zdravie_zaznam WHERE disk_id = $d ORDER BY kedy DESC LIMIT $n
            """);
        prikaz.Parameters.AddWithValue("$d", diskId);
        prikaz.Parameters.AddWithValue("$n", najviac);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<ZdravieDisku>();
        while (c.Read())
        {
            vysledok.Add(CitajZdravie(c, 0));
        }

        return vysledok;
    }

    private static Dictionary<long, ZdravieDisku> PosledneZdravie(SqliteConnection spojenie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT z.disk_id, z.kedy, z.stav, z.teplota, z.hodiny, z.realokovane, z.cakajuce, z.opotrebenie FROM zdravie_zaznam z
            WHERE z.id = (SELECT id FROM zdravie_zaznam y WHERE y.disk_id = z.disk_id ORDER BY y.kedy DESC, y.id DESC LIMIT 1)
            """);
        using var c = prikaz.ExecuteReader();
        var vysledok = new Dictionary<long, ZdravieDisku>();
        while (c.Read())
        {
            vysledok[c.GetInt64(0)] = CitajZdravie(c, 1);
        }

        return vysledok;
    }

    private static ZdravieDisku CitajZdravie(SqliteDataReader c, int od)
    {
        int? Cislo(int i) => c.IsDBNull(od + i) ? null : c.GetInt32(od + i);
        return new ZdravieDisku(c.GetInt64(od), (StavZdravia)c.GetInt32(od + 1), Cislo(2), Cislo(3), Cislo(4), Cislo(5), Cislo(6));
    }

    private static object? Skalar(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, sql);
        return prikaz.ExecuteScalar();
    }

    private static List<PolozkaStatistiky> Zoznam(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, sql);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<PolozkaStatistiky>();
        while (c.Read())
        {
            vysledok.Add(new PolozkaStatistiky(c.GetString(0), c.GetInt64(1)));
        }

        return vysledok;
    }
}
