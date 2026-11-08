using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A loan: a disk or a title lent to someone (open while <see cref="Do"/> is null).</summary>
public sealed record Vypozicka(
    long Id,
    string Komu,
    long Od,
    long? Do,
    long? DiskId,
    long? TitulId,
    string Co,
    string? Poznamka)
{
    public bool Otvorena => Do is null;
}

/// <summary>Loans (N6): what is lent, to whom and since when; returned loans stay as history.</summary>
public static class Vypozicky
{
    /// <summary>Lends a disk or a title (exactly one of them); returns the loan's id. A thing lent already cannot be lent again.</summary>
    public static long Pozicaj(SqliteConnection spojenie, long? diskId, long? titulId, string komu, string? poznamka, long od)
    {
        if ((diskId is null) == (titulId is null))
        {
            throw new ArgumentException("Požičia sa disk alebo titul.");
        }

        komu = komu.Trim();
        if (komu.Length == 0)
        {
            throw new ArgumentException("Komu sa požičiava?", nameof(komu));
        }

        if (Otvorena(spojenie, diskId, titulId) is not null)
        {
            throw new InvalidOperationException(diskId is null ? "Titul je už požičaný." : "Disk je už požičaný.");
        }

        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT INTO vypozicka(komu, kluc, od, disk_id, titul_id, poznamka) VALUES ($komu, $kluc, $od, $d, $t, $p) RETURNING id
            """);
        prikaz.Parameters.AddWithValue("$komu", komu);
        prikaz.Parameters.AddWithValue("$kluc", Normalizacia.Nazov(komu));
        prikaz.Parameters.AddWithValue("$od", od);
        prikaz.Parameters.AddWithValue("$d", (object?)diskId ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$t", (object?)titulId ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$p", string.IsNullOrWhiteSpace(poznamka) ? DBNull.Value : poznamka.Trim());
        return (long)prikaz.ExecuteScalar()!;
    }

    /// <summary>Marks a loan returned (a returned one stays as it was).</summary>
    public static void Vrat(SqliteConnection spojenie, long id, long kedy)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE vypozicka SET do = max($kedy, od) WHERE id = $id AND do IS NULL");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.Parameters.AddWithValue("$kedy", kedy);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Removes a loan from the history altogether (a mistake).</summary>
    public static void Zmaz(SqliteConnection spojenie, long id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM vypozicka WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", id);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>The open loan of a disk or a title, if it is lent now.</summary>
    public static Vypozicka? Otvorena(SqliteConnection spojenie, long? diskId, long? titulId) =>
        Citaj(spojenie, "WHERE v.do IS NULL AND (v.disk_id = $d OR v.titul_id = $t)", ("$d", diskId), ("$t", titulId)).FirstOrDefault();

    /// <summary>Everything lent now, the oldest loans first.</summary>
    public static IReadOnlyList<Vypozicka> Otvorene(SqliteConnection spojenie) =>
        Citaj(spojenie, "WHERE v.do IS NULL ORDER BY v.od, v.id");

    /// <summary>The loans of a disk or a title, the newest first.</summary>
    public static IReadOnlyList<Vypozicka> Historia(SqliteConnection spojenie, long? diskId, long? titulId) =>
        Citaj(spojenie, "WHERE v.disk_id = $d OR v.titul_id = $t ORDER BY v.od DESC, v.id DESC", ("$d", diskId), ("$t", titulId));

    /// <summary>All loans, open ones first, then returned ones from the newest.</summary>
    public static IReadOnlyList<Vypozicka> Vsetky(SqliteConnection spojenie) =>
        Citaj(spojenie, "ORDER BY v.do IS NOT NULL, coalesce(v.do, v.od) DESC, v.id DESC");

    /// <summary>The names lent to before, the most frequent first (offered when lending).</summary>
    public static IReadOnlyList<string> Mena(SqliteConnection spojenie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT komu FROM vypozicka GROUP BY kluc ORDER BY count(*) DESC, max(od) DESC LIMIT 50");
        using var citac = prikaz.ExecuteReader();
        var mena = new List<string>();
        while (citac.Read())
        {
            mena.Add(citac.GetString(0));
        }

        return mena;
    }

    private static List<Vypozicka> Citaj(SqliteConnection spojenie, string podmienka, params (string Meno, long? Hodnota)[] parametre)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT v.id, v.komu, v.od, v.do, v.disk_id, v.titul_id, v.poznamka,
                   CASE WHEN v.disk_id IS NOT NULL THEN printf('D%03d %s', d.cislo, d.nazov)
                        ELSE t.nazov || coalesce(' (' || t.rok || ')', '') END
            FROM vypozicka v
            LEFT JOIN disk d ON d.id = v.disk_id
            LEFT JOIN titul t ON t.id = v.titul_id
            {podmienka}
            """);
        foreach (var (meno, hodnota) in parametre)
        {
            prikaz.Parameters.AddWithValue(meno, (object?)hodnota ?? DBNull.Value);
        }

        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<Vypozicka>();
        while (citac.Read())
        {
            vysledok.Add(new Vypozicka(
                citac.GetInt64(0),
                citac.GetString(1),
                citac.GetInt64(2),
                citac.IsDBNull(3) ? null : citac.GetInt64(3),
                citac.IsDBNull(4) ? null : citac.GetInt64(4),
                citac.IsDBNull(5) ? null : citac.GetInt64(5),
                citac.GetString(7),
                citac.IsDBNull(6) ? null : citac.GetString(6)));
        }

        return vysledok;
    }
}
