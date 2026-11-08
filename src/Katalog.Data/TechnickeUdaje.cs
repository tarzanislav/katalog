using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A video whose technical data are still to be read, with what the scan saw (to know it did not change since).</summary>
/// <param name="MaUdaje">Its technical data are read already; only its fingerprint is missing (stage E5).</param>
public sealed record VideoNaCitanie(long SuborId, long PriecinokId, string Nazov, long Velkost, long Zmeneny, bool MaUdaje = false);

/// <summary>A preview of a video: the name of its picture file and the time in the video.</summary>
public sealed record NahladVidea(string Meno, long CasMs, int Sirka, int Vyska);

/// <summary>What the library knows about the technical side of one file.</summary>
public sealed record TechnickeUdajeSuboru(MedialneUdaje Udaje, IReadOnlyList<Stopa> Stopy, IReadOnlyList<NahladVidea> Nahlady, long Precitane);

/// <summary>Technical data of videos in the library (stage E3): which are still to be read, writing and reading them.</summary>
public static class TechnickeUdaje
{
    /// <summary>The extensions of videos as an SQL list (constants of the program, nothing the user wrote).</summary>
    private static readonly string Videa = string.Join(", ", DruhySuborov.Pripony(DruhSuboru.Video).Select(p => "'" + p + "'"));

    /// <summary>
    /// Videos of a disk without technical data or without a fingerprint (present ones only), in the order of the scan,
    /// after <paramref name="poId"/>.
    /// </summary>
    public static IReadOnlyList<VideoNaCitanie> NaCitanie(SqliteConnection spojenie, long diskId, int najviac, long poId = 0)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"""
            SELECT s.id, s.priecinok_id, s.nazov, s.velkost, s.zmeneny, EXISTS (SELECT 1 FROM medialne_udaje m WHERE m.subor_id = s.id) FROM subor s
            WHERE s.disk_id = $disk AND s.stav = 0 AND s.pripona IN ({Videa}) AND s.id > $po
              AND (s.odtlacok IS NULL OR NOT EXISTS (SELECT 1 FROM medialne_udaje m WHERE m.subor_id = s.id))
            ORDER BY s.id LIMIT $najviac
            """;
        prikaz.Parameters.AddWithValue("$disk", diskId);
        prikaz.Parameters.AddWithValue("$po", poId);
        prikaz.Parameters.AddWithValue("$najviac", najviac);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<VideoNaCitanie>();
        while (citac.Read())
        {
            vysledok.Add(new VideoNaCitanie(citac.GetInt64(0), citac.GetInt64(1), citac.GetString(2), citac.GetInt64(3), citac.GetInt64(4), citac.GetInt32(5) == 1));
        }

        return vysledok;
    }

    /// <summary>How many present videos a disk has, and how many of them still wait for their technical data or fingerprint.</summary>
    public static (int Videa, int Zostava) Pocty(SqliteConnection spojenie, long diskId)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"""
            SELECT count(*), count(*) FILTER (WHERE s.odtlacok IS NULL OR NOT EXISTS (SELECT 1 FROM medialne_udaje m WHERE m.subor_id = s.id))
            FROM subor s WHERE s.disk_id = $disk AND s.stav = 0 AND s.pripona IN ({Videa})
            """;
        prikaz.Parameters.AddWithValue("$disk", diskId);
        using var citac = prikaz.ExecuteReader();
        citac.Read();
        return (citac.GetInt32(0), citac.GetInt32(1));
    }

    /// <summary>
    /// Writes what was read about a video – only when the file is still the one that was read (same size and time, still
    /// present); returns whether it was written. Tracks and previews are replaced as a whole.
    /// </summary>
    /// <summary>
    /// Writes the fingerprint of a video (exact copies, stage E5) – only when the file is still the one that was read;
    /// returns whether it was written.
    /// </summary>
    public static bool ZapisOdtlacok(SqliteConnection spojenie, VideoNaCitanie video, byte[] odtlacok)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "UPDATE subor SET odtlacok = $o WHERE id = $id AND velkost = $v AND zmeneny = $z AND stav = 0";
        prikaz.Parameters.AddWithValue("$o", odtlacok);
        prikaz.Parameters.AddWithValue("$id", video.SuborId);
        prikaz.Parameters.AddWithValue("$v", video.Velkost);
        prikaz.Parameters.AddWithValue("$z", video.Zmeneny);
        return prikaz.ExecuteNonQuery() > 0;
    }

    public static bool Zapis(
        SqliteConnection spojenie, VideoNaCitanie video, MedialneUdaje udaje, IReadOnlyList<Stopa> stopy, IReadOnlyList<(long ObrazokId, long CasMs)> nahlady, long teraz)
    {
        using (var kontrola = spojenie.CreateCommand())
        {
            kontrola.CommandText = "SELECT 1 FROM subor WHERE id = $id AND velkost = $v AND zmeneny = $z AND stav = 0";
            kontrola.Parameters.AddWithValue("$id", video.SuborId);
            kontrola.Parameters.AddWithValue("$v", video.Velkost);
            kontrola.Parameters.AddWithValue("$z", video.Zmeneny);
            if (kontrola.ExecuteScalar() is null)
            {
                return false;
            }
        }

        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = """
                INSERT OR REPLACE INTO medialne_udaje(subor_id, kontajner, dlzka_ms, datovy_tok, video_kodek, sirka, vyska, hdr, snimky, trieda_kvality, chyby, precitane)
                VALUES ($id, $kontajner, $dlzka, $tok, $kodek, $sirka, $vyska, $hdr, $snimky, $trieda, $chyby, $teraz);
                DELETE FROM stopa WHERE subor_id = $id;
                DELETE FROM nahlad WHERE subor_id = $id;
                """;
            prikaz.Parameters.AddWithValue("$id", video.SuborId);
            prikaz.Parameters.AddWithValue("$kontajner", (object?)udaje.Kontajner ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$dlzka", (object?)udaje.DlzkaMs ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$tok", (object?)udaje.DatovyTok ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$kodek", (object?)udaje.VideoKodek ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$sirka", (object?)udaje.Sirka ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$vyska", (object?)udaje.Vyska ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$hdr", (int)udaje.Hdr);
            prikaz.Parameters.AddWithValue("$snimky", (object?)udaje.Snimky ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$trieda", (int)udaje.Trieda);
            prikaz.Parameters.AddWithValue("$chyby", (object?)udaje.Chyby ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            prikaz.ExecuteNonQuery();
        }

        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = """
                INSERT INTO stopa(subor_id, poradie, druh, jazyk, kodek, kanaly, predvolena, vynutena, sdh, externy_subor_id, nazov)
                VALUES ($id, $poradie, $druh, $jazyk, $kodek, $kanaly, $predvolena, $vynutena, $sdh, $externy, $nazov)
                """;
            foreach (var stopa in stopy)
            {
                prikaz.Parameters.Clear();
                prikaz.Parameters.AddWithValue("$id", video.SuborId);
                prikaz.Parameters.AddWithValue("$poradie", stopa.Poradie);
                prikaz.Parameters.AddWithValue("$druh", (int)stopa.Druh);
                prikaz.Parameters.AddWithValue("$jazyk", (object?)stopa.Jazyk ?? DBNull.Value);
                prikaz.Parameters.AddWithValue("$kodek", (object?)stopa.Kodek ?? DBNull.Value);
                prikaz.Parameters.AddWithValue("$kanaly", (object?)stopa.Kanaly ?? DBNull.Value);
                prikaz.Parameters.AddWithValue("$predvolena", stopa.Predvolena ? 1 : 0);
                prikaz.Parameters.AddWithValue("$vynutena", stopa.Vynutena ? 1 : 0);
                prikaz.Parameters.AddWithValue("$sdh", stopa.Sdh ? 1 : 0);
                prikaz.Parameters.AddWithValue("$externy", (object?)stopa.ExternySubor ?? DBNull.Value);
                prikaz.Parameters.AddWithValue("$nazov", (object?)stopa.Nazov ?? DBNull.Value);
                prikaz.ExecuteNonQuery();
            }
        }

        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = "INSERT INTO nahlad(subor_id, poradie, obrazok_id, cas_ms) VALUES ($id, $poradie, $obrazok, $cas)";
            prikaz.Parameters.AddWithValue("$id", video.SuborId);
            var poradie = prikaz.Parameters.Add("$poradie", SqliteType.Integer);
            var obrazok = prikaz.Parameters.Add("$obrazok", SqliteType.Integer);
            var cas = prikaz.Parameters.Add("$cas", SqliteType.Integer);
            for (var i = 0; i < nahlady.Count; i++)
            {
                poradie.Value = i;
                obrazok.Value = nahlady[i].ObrazokId;
                cas.Value = nahlady[i].CasMs;
                prikaz.ExecuteNonQuery();
            }
        }

        return true;
    }

    /// <summary>The technical data of a file, or null when they were not read (yet).</summary>
    public static TechnickeUdajeSuboru? Citaj(SqliteConnection spojenie, long suborId)
    {
        MedialneUdaje udaje;
        long precitane;
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = """
                SELECT kontajner, dlzka_ms, datovy_tok, video_kodek, sirka, vyska, hdr, snimky, chyby, precitane
                FROM medialne_udaje WHERE subor_id = $id
                """;
            prikaz.Parameters.AddWithValue("$id", suborId);
            using var citac = prikaz.ExecuteReader();
            if (!citac.Read())
            {
                return null;
            }

            udaje = new MedialneUdaje(
                citac.IsDBNull(0) ? null : citac.GetString(0),
                citac.IsDBNull(1) ? null : citac.GetInt64(1),
                citac.IsDBNull(2) ? null : citac.GetInt64(2),
                citac.IsDBNull(3) ? null : citac.GetString(3),
                citac.IsDBNull(4) ? null : citac.GetInt32(4),
                citac.IsDBNull(5) ? null : citac.GetInt32(5),
                (Hdr)citac.GetInt32(6),
                citac.IsDBNull(7) ? null : citac.GetDouble(7),
                citac.IsDBNull(8) ? null : citac.GetString(8));
            precitane = citac.GetInt64(9);
        }

        var stopy = new List<Stopa>();
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = """
                SELECT poradie, druh, jazyk, kodek, kanaly, predvolena, vynutena, sdh, nazov, externy_subor_id
                FROM stopa WHERE subor_id = $id ORDER BY poradie
                """;
            prikaz.Parameters.AddWithValue("$id", suborId);
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                stopy.Add(new Stopa(citac.GetInt32(0), (DruhStopy)citac.GetInt32(1), citac.IsDBNull(2) ? null : citac.GetString(2),
                    citac.IsDBNull(3) ? null : citac.GetString(3), citac.IsDBNull(4) ? null : citac.GetInt32(4), citac.GetInt32(5) == 1,
                    citac.GetInt32(6) == 1, citac.GetInt32(7) == 1, citac.IsDBNull(8) ? null : citac.GetString(8), citac.IsDBNull(9) ? null : citac.GetInt64(9)));
            }
        }

        var nahlady = new List<NahladVidea>();
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = """
                SELECT lower(hex(o.odtlacok)), n.cas_ms, o.sirka, o.vyska FROM nahlad n JOIN obrazok o ON o.id = n.obrazok_id
                WHERE n.subor_id = $id ORDER BY n.poradie
                """;
            prikaz.Parameters.AddWithValue("$id", suborId);
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                nahlady.Add(new NahladVidea(citac.GetString(0), citac.GetInt64(1), citac.GetInt32(2), citac.GetInt32(3)));
            }
        }

        return new TechnickeUdajeSuboru(udaje, stopy, nahlady, precitane);
    }
}
