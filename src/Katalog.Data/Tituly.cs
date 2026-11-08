using System.Globalization;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A video that is not yet in the library of titles, with its folder (for the names above it).</summary>
public sealed record VideoNaZaradenie(long SuborId, long PriecinokId, string Nazov);

/// <summary>One version found on a disk: what the names say and its files in order (CD1, CD2; a whole BDMV).</summary>
public sealed record JednotkaKniznice(NavrhTitulu Navrh, IReadOnlyList<long> Subory);

/// <summary>
/// The library of titles (stage E4): videos become versions of films and episodes, sources fill in their data. Every
/// field remembers where it came from (A5): a value the user typed is locked, the others follow the order of sources.
/// </summary>
public static class Tituly
{
    private static readonly string Videa = string.Join(", ", DruhySuborov.Pripony(DruhSuboru.Video).Select(p => "'" + p + "'"));

    /// <summary>Fields of a title the sources fill (the names of its columns).</summary>
    private static readonly string[] PolaTitulu = ["nazov", "original", "rok", "dej", "dlzka_min", "hodnotenie_zdroja", "plagat_id"];

    /// <summary>
    /// Present videos of a disk that belong to no version and that are not set aside as no titles, after
    /// <paramref name="poId"/> in the order of the scan.
    /// </summary>
    public static IReadOnlyList<VideoNaZaradenie> NaZaradenie(SqliteConnection spojenie, long diskId, int najviac, long poId = 0)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT s.id, s.priecinok_id, s.nazov FROM subor s
            WHERE s.disk_id = $disk AND s.stav = 0 AND s.pripona IN ({Videa}) AND s.id > $po
              AND NOT EXISTS (SELECT 1 FROM verzia_subor v WHERE v.subor_id = s.id)
              AND NOT EXISTS (SELECT 1 FROM subor_mimo_kniznice m WHERE m.subor_id = s.id)
            ORDER BY s.id LIMIT $najviac
            """);
        prikaz.Parameters.AddWithValue("$disk", diskId);
        prikaz.Parameters.AddWithValue("$po", poId);
        prikaz.Parameters.AddWithValue("$najviac", najviac);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new List<VideoNaZaradenie>();
        while (citac.Read())
        {
            vysledok.Add(new VideoNaZaradenie(citac.GetInt64(0), citac.GetInt64(1), citac.GetString(2)));
        }

        return vysledok;
    }

    /// <summary>Every folder of a disk with its parent and name (the root has the empty name), to read names above videos.</summary>
    public static IReadOnlyDictionary<long, (long? Rodic, string Nazov)> PriecinkyDisku(SqliteConnection spojenie, long diskId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT id, rodic_id, nazov FROM priecinok WHERE disk_id = $disk");
        prikaz.Parameters.AddWithValue("$disk", diskId);
        using var citac = prikaz.ExecuteReader();
        var vysledok = new Dictionary<long, (long? Rodic, string Nazov)>();
        while (citac.Read())
        {
            vysledok[citac.GetInt64(0)] = (citac.IsDBNull(1) ? null : citac.GetInt64(1), citac.GetString(2));
        }

        return vysledok;
    }

    /// <summary>How many present videos of a disk still wait to be put into the library.</summary>
    public static int PocetNaZaradenie(SqliteConnection spojenie, long diskId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT count(*) FROM subor s
            WHERE s.disk_id = $disk AND s.stav = 0 AND s.pripona IN ({Videa})
              AND NOT EXISTS (SELECT 1 FROM verzia_subor v WHERE v.subor_id = s.id)
              AND NOT EXISTS (SELECT 1 FROM subor_mimo_kniznice m WHERE m.subor_id = s.id)
            """);
        prikaz.Parameters.AddWithValue("$disk", diskId);
        return Convert.ToInt32(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Puts one version into the library: finds its title (by a TMDB id, else by the key of its name and year) or creates
    /// it, creates the episodes it holds and the version with its files. A version from UNIVERSUM's library or with a TMDB
    /// id is confirmed at once; the others wait until a source finds exactly their title or the user confirms them (R5).
    /// Returns the title's id, or null when the files were taken meanwhile.
    /// </summary>
    public static long? Zarad(SqliteConnection spojenie, JednotkaKniznice jednotka, long teraz)
    {
        var navrh = jednotka.Navrh;
        if (jednotka.Subory.Count == 0 || navrh.Extra || (navrh.Druh == DruhTitulu.Serial && navrh.Epizody.Count == 0))
        {
            throw new ArgumentException("Jednotka knižnice musí mať súbory a byť film alebo epizóda.", nameof(jednotka));
        }

        foreach (var subor in jednotka.Subory)
        {
            using var obsadeny = Kniznica.Prikaz(spojenie, "SELECT 1 FROM verzia_subor WHERE subor_id = $s");
            obsadeny.Parameters.AddWithValue("$s", subor);
            if (obsadeny.ExecuteScalar() is not null)
            {
                return null;
            }
        }

        var titulId = NajdiTitul(spojenie, navrh) ?? VytvorTitul(spojenie, navrh, teraz);
        PridajKluc(spojenie, titulId, Normalizacia.Kluc(navrh.Druh, navrh.Nazov, navrh.Rok));
        if (navrh.TmdbId is { } tmdb)
        {
            PridajIdentifikator(spojenie, ZdrojUdajov.Tmdb, DruhObjektu.Titul, TmdbIdentifikator(navrh.Druh, tmdb), titulId);
        }

        var potvrdena = navrh.Istota == IstotaNavrhu.Isty ? 1 : 0;
        long verziaId;
        if (navrh.Druh == DruhTitulu.Film)
        {
            using var verzia = Kniznica.Prikaz(spojenie, """
                INSERT INTO verzia(titul_id, potvrdena, poznamka, vytvorena) VALUES ($t, $p, $poz, $teraz) RETURNING id
                """);
            verzia.Parameters.AddWithValue("$t", titulId);
            verzia.Parameters.AddWithValue("$p", potvrdena);
            verzia.Parameters.AddWithValue("$poz", (object?)navrh.Doplnok ?? DBNull.Value);
            verzia.Parameters.AddWithValue("$teraz", teraz);
            verziaId = (long)verzia.ExecuteScalar()!;
        }
        else
        {
            var seria = navrh.Seria ?? 1;
            var epizody = navrh.Epizody.Select(c => Epizoda(spojenie, titulId, seria, c)).ToList();
            using var verzia = Kniznica.Prikaz(spojenie, """
                INSERT INTO verzia(epizoda_id, potvrdena, poznamka, vytvorena) VALUES ($e, $p, $poz, $teraz) RETURNING id
                """);
            verzia.Parameters.AddWithValue("$e", epizody[0]);
            verzia.Parameters.AddWithValue("$p", potvrdena);
            verzia.Parameters.AddWithValue("$poz", (object?)navrh.Doplnok ?? DBNull.Value);
            verzia.Parameters.AddWithValue("$teraz", teraz);
            verziaId = (long)verzia.ExecuteScalar()!;
            foreach (var dalsia in epizody.Skip(1).Distinct())
            {
                using var spolu = Kniznica.Prikaz(spojenie, "INSERT OR IGNORE INTO verzia_dalsia_epizoda(verzia_id, epizoda_id) VALUES ($v, $e)");
                spolu.Parameters.AddWithValue("$v", verziaId);
                spolu.Parameters.AddWithValue("$e", dalsia);
                spolu.ExecuteNonQuery();
            }
        }

        for (var i = 0; i < jednotka.Subory.Count; i++)
        {
            using var subor = Kniznica.Prikaz(spojenie, "INSERT INTO verzia_subor(verzia_id, subor_id, poradie) VALUES ($v, $s, $p)");
            subor.Parameters.AddWithValue("$v", verziaId);
            subor.Parameters.AddWithValue("$s", jednotka.Subory[i]);
            subor.Parameters.AddWithValue("$p", i);
            subor.ExecuteNonQuery();
        }

        return titulId;
    }

    /// <summary>Sets files aside as no titles (extras, samples, unreadable names; or because the user said so).</summary>
    public static void MimoKniznice(SqliteConnection spojenie, IEnumerable<long> subory, bool pouzivatel)
    {
        foreach (var subor in subory)
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                INSERT INTO subor_mimo_kniznice(subor_id, pouzivatel) VALUES ($s, $p)
                ON CONFLICT(subor_id) DO UPDATE SET pouzivatel = max(pouzivatel, excluded.pouzivatel)
                """);
            prikaz.Parameters.AddWithValue("$s", subor);
            prikaz.Parameters.AddWithValue("$p", pouzivatel ? 1 : 0);
            prikaz.ExecuteNonQuery();
            if (pouzivatel)
            {
                // the user said it is no title: its version goes (a version without files is removed by the trigger)
                using var verzia = Kniznica.Prikaz(spojenie, "DELETE FROM verzia_subor WHERE subor_id = $s");
                verzia.Parameters.AddWithValue("$s", subor);
                verzia.ExecuteNonQuery();
            }
        }

        OdstranOpustene(spojenie);
    }

    /// <summary>Gives files set aside back to the recognition (all of a disk, or the given ones).</summary>
    public static int VratDoKniznice(SqliteConnection spojenie, IEnumerable<long> subory)
    {
        var pocet = 0;
        foreach (var subor in subory)
        {
            using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM subor_mimo_kniznice WHERE subor_id = $s");
            prikaz.Parameters.AddWithValue("$s", subor);
            pocet += prikaz.ExecuteNonQuery();
        }

        return pocet;
    }

    /// <summary>"movie/603" or "tv/1399" – how TMDB names its titles.</summary>
    public static string TmdbIdentifikator(DruhTitulu druh, int id) =>
        (druh == DruhTitulu.Film ? "movie/" : "tv/") + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the data of a title from one source. A field is written when it has no origin yet or when the source may
    /// replace its origin (<see cref="PoradieZdrojov.MozePrepisat"/>); a locked field never changes. When the source's
    /// identifier already belongs to another title, the two are one title: this one is merged into it and the data go
    /// there. Returns the id of the title the data went to.
    /// </summary>
    public static long ZapisUdaje(
        SqliteConnection spojenie, long titulId, UdajeTitulu udaje, long? plagatId, IReadOnlyList<ZdrojUdajov> poradie, long teraz)
    {
        if (Druh(spojenie, titulId) is not { } druh)
        {
            throw new InvalidOperationException("Titul už v knižnici nie je.");
        }

        if (udaje.Druh != druh)
        {
            throw new InvalidOperationException("Údaje patria seriálu a titul je film, alebo naopak.");
        }

        var identifikatory = new List<(ZdrojUdajov Zdroj, string Hodnota)> { (udaje.Zdroj, udaje.Identifikator) };
        identifikatory.AddRange(udaje.DalsieIdentifikatory.Where(i => i.Zdroj is >= ZdrojUdajov.Tmdb and <= ZdrojUdajov.Imdb && i.Identifikator.Length > 0));
        foreach (var (zdroj, hodnota) in identifikatory)
        {
            if (Vlastnik(spojenie, zdroj, DruhObjektu.Titul, hodnota) is { } vlastnik && vlastnik != titulId)
            {
                if (Druh(spojenie, vlastnik) == druh)
                {
                    Zluc(spojenie, titulId, vlastnik, teraz);
                    titulId = vlastnik;
                }

                continue;
            }

            PridajIdentifikator(spojenie, zdroj, DruhObjektu.Titul, hodnota, titulId);
        }

        var hodnoty = new Dictionary<string, object?>
        {
            ["nazov"] = string.IsNullOrWhiteSpace(udaje.Nazov) ? null : udaje.Nazov.Trim(),
            ["original"] = string.IsNullOrWhiteSpace(udaje.Original) ? null : udaje.Original.Trim(),
            ["rok"] = udaje.Rok is >= 1870 and <= 2200 ? udaje.Rok : null,
            ["dej"] = string.IsNullOrWhiteSpace(udaje.Dej) ? null : udaje.Dej.Trim(),
            ["dlzka_min"] = udaje.DlzkaMinut is > 0 ? udaje.DlzkaMinut : null,
            ["hodnotenie_zdroja"] = udaje.Hodnotenie is >= 0 and <= 10 ? Math.Round(udaje.Hodnotenie.Value, 1) : null,
            ["plagat_id"] = plagatId,
        };
        foreach (var pole in PolaTitulu)
        {
            if (hodnoty[pole] is { } hodnota && SmieZapisat(spojenie, DruhObjektu.Titul, titulId, pole, udaje.Zdroj, poradie))
            {
                using var prikaz = Kniznica.Prikaz(spojenie, $"UPDATE titul SET {pole} = $h WHERE id = $id");
                prikaz.Parameters.AddWithValue("$h", hodnota);
                prikaz.Parameters.AddWithValue("$id", titulId);
                prikaz.ExecuteNonQuery();
                ZapisPovod(spojenie, DruhObjektu.Titul, titulId, pole, udaje.Zdroj, teraz, zamknute: false);
            }
        }

        if (udaje.Zanre.Count > 0 && SmieZapisat(spojenie, DruhObjektu.Titul, titulId, "zanre", udaje.Zdroj, poradie))
        {
            NastavZanre(spojenie, titulId, udaje.Zanre);
            ZapisPovod(spojenie, DruhObjektu.Titul, titulId, "zanre", udaje.Zdroj, teraz, zamknute: false);
        }

        if (udaje.Osoby.Count > 0 && SmieZapisat(spojenie, DruhObjektu.Titul, titulId, "osoby", udaje.Zdroj, poradie))
        {
            NastavOsoby(spojenie, titulId, udaje.Zdroj, udaje.Osoby);
            ZapisPovod(spojenie, DruhObjektu.Titul, titulId, "osoby", udaje.Zdroj, teraz, zamknute: false);
        }

        if (druh == DruhTitulu.Serial)
        {
            foreach (var e in udaje.Epizody.Where(e => e.Seria >= 0 && e.Cislo >= 0))
            {
                var epizodaId = Epizoda(spojenie, titulId, e.Seria, e.Cislo);
                var polia = new (string Pole, object? Hodnota)[]
                {
                    ("nazov", string.IsNullOrWhiteSpace(e.Nazov) ? null : e.Nazov.Trim()),
                    ("vysielanie", e.Vysielanie?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("dej", string.IsNullOrWhiteSpace(e.Dej) ? null : e.Dej.Trim()),
                };
                foreach (var (pole, hodnota) in polia)
                {
                    if (hodnota is not null && SmieZapisat(spojenie, DruhObjektu.Epizoda, epizodaId, pole, udaje.Zdroj, poradie))
                    {
                        using var prikaz = Kniznica.Prikaz(spojenie, $"UPDATE epizoda SET {pole} = $h WHERE id = $id");
                        prikaz.Parameters.AddWithValue("$h", hodnota);
                        prikaz.Parameters.AddWithValue("$id", epizodaId);
                        prikaz.ExecuteNonQuery();
                        ZapisPovod(spojenie, DruhObjektu.Epizoda, epizodaId, pole, udaje.Zdroj, teraz, zamknute: false);
                    }
                }
            }
        }

        using (var cas = Kniznica.Prikaz(spojenie, "UPDATE titul SET nacitany = $teraz, zmeneny = $teraz WHERE id = $id"))
        {
            cas.Parameters.AddWithValue("$teraz", teraz);
            cas.Parameters.AddWithValue("$id", titulId);
            cas.ExecuteNonQuery();
        }

        PridajKluceNazvov(spojenie, titulId);
        return titulId;
    }

    /// <summary>
    /// Merges title <paramref name="z"/> into <paramref name="doTitulu"/> (the same film or series twice): versions, episodes,
    /// collections, genres, people, tags, watching, loans, playlists and HISTRA lists move over; a value the user typed
    /// in <paramref name="z"/> wins over one from a source; then <paramref name="z"/> is gone.
    /// </summary>
    public static void Zluc(SqliteConnection spojenie, long z, long doTitulu, long teraz)
    {
        if (z == doTitulu)
        {
            return;
        }

        if (Druh(spojenie, z) != Druh(spojenie, doTitulu))
        {
            throw new InvalidOperationException("Film a seriál sa nedajú zlúčiť.");
        }

        // episodes both titles have: everything of the one in z moves to the one in doTitulu, then it goes
        const string Dvojica = """
            (SELECT d.id FROM epizoda e JOIN epizoda d ON d.titul_id = $do AND d.seria = e.seria AND d.cislo = e.cislo WHERE e.id = {0})
            """;
        const string ZdvojeneEpizody = """
            (SELECT e.id FROM epizoda e WHERE e.titul_id = $z
             AND EXISTS (SELECT 1 FROM epizoda d WHERE d.titul_id = $do AND d.seria = e.seria AND d.cislo = e.cislo))
            """;
        string[] prikazy =
        [
            "UPDATE verzia SET titul_id = $do WHERE titul_id = $z",
            $"UPDATE verzia SET epizoda_id = {string.Format(CultureInfo.InvariantCulture, Dvojica, "verzia.epizoda_id")} WHERE epizoda_id IN {ZdvojeneEpizody}",
            $"UPDATE OR IGNORE verzia_dalsia_epizoda SET epizoda_id = {string.Format(CultureInfo.InvariantCulture, Dvojica, "verzia_dalsia_epizoda.epizoda_id")} WHERE epizoda_id IN {ZdvojeneEpizody}",
            $"UPDATE pozretie SET epizoda_id = {string.Format(CultureInfo.InvariantCulture, Dvojica, "pozretie.epizoda_id")} WHERE epizoda_id IN {ZdvojeneEpizody}",
            $"UPDATE playlist_polozka SET epizoda_id = {string.Format(CultureInfo.InvariantCulture, Dvojica, "playlist_polozka.epizoda_id")} WHERE epizoda_id IN {ZdvojeneEpizody}",
            $"UPDATE OR IGNORE identifikator SET epizoda_id = {string.Format(CultureInfo.InvariantCulture, Dvojica, "identifikator.epizoda_id")} WHERE epizoda_id IN {ZdvojeneEpizody}",
            $"DELETE FROM epizoda WHERE id IN {ZdvojeneEpizody}",
            "UPDATE epizoda SET titul_id = $do WHERE titul_id = $z",
            "UPDATE OR IGNORE kolekcia_titul SET titul_id = $do WHERE titul_id = $z",
            "UPDATE OR IGNORE titul_zaner SET titul_id = $do WHERE titul_id = $z AND NOT EXISTS (SELECT 1 FROM titul_zaner WHERE titul_id = $do)",
            "UPDATE OR IGNORE titul_osoba SET titul_id = $do WHERE titul_id = $z AND NOT EXISTS (SELECT 1 FROM titul_osoba WHERE titul_id = $do)",
            "UPDATE OR IGNORE titul_stitok SET titul_id = $do WHERE titul_id = $z",
            "UPDATE OR IGNORE titul_kluc SET titul_id = $do WHERE titul_id = $z",
            "UPDATE OR IGNORE identifikator SET titul_id = $do WHERE titul_id = $z",
            "UPDATE pozretie SET titul_id = $do WHERE titul_id = $z",
            "UPDATE vypozicka SET titul_id = $do WHERE titul_id = $z",
            "UPDATE playlist_polozka SET titul_id = $do WHERE titul_id = $z",
            "UPDATE OR IGNORE histra_polozka SET titul_id = $do WHERE titul_id = $z",
            """
            UPDATE titul SET
              zelany = max(zelany, (SELECT zelany FROM titul WHERE id = $z)),
              moje_hodnotenie = coalesce(moje_hodnotenie, (SELECT moje_hodnotenie FROM titul WHERE id = $z)),
              moja_poznamka = coalesce(moja_poznamka, (SELECT moja_poznamka FROM titul WHERE id = $z)),
              zmeneny = $teraz
            WHERE id = $do
            """,
        ];
        foreach (var sql in prikazy)
        {
            using var prikaz = Kniznica.Prikaz(spojenie, sql);
            prikaz.Parameters.AddWithValue("$z", z);
            prikaz.Parameters.AddWithValue("$do", doTitulu);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            prikaz.ExecuteNonQuery();
        }

        // what the user typed in z wins (unless the user typed it in doTitulu too)
        foreach (var pole in PolaTitulu.Where(p => p != "plagat_id"))
        {
            using var prikaz = Kniznica.Prikaz(spojenie, $"""
                UPDATE titul SET {pole} = (SELECT {pole} FROM titul WHERE id = $z)
                WHERE id = $do
                  AND EXISTS (SELECT 1 FROM povod_pola WHERE druh = 1 AND objekt_id = $z AND pole = '{pole}' AND zdroj = 0)
                  AND NOT EXISTS (SELECT 1 FROM povod_pola WHERE druh = 1 AND objekt_id = $do AND pole = '{pole}' AND zdroj = 0)
                """);
            prikaz.Parameters.AddWithValue("$z", z);
            prikaz.Parameters.AddWithValue("$do", doTitulu);
            if (prikaz.ExecuteNonQuery() > 0)
            {
                ZapisPovod(spojenie, DruhObjektu.Titul, doTitulu, pole, ZdrojUdajov.Pouzivatel, teraz, zamknute: true);
            }
        }

        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM titul WHERE id = $z"))
        {
            zmaz.Parameters.AddWithValue("$z", z);
            zmaz.ExecuteNonQuery();
        }

        PridajKluceNazvov(spojenie, doTitulu);
    }

    /// <summary>The user confirms that the versions of a title are this title: they leave "Na potvrdenie".</summary>
    public static void Potvrd(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            UPDATE verzia SET potvrdena = 1
            WHERE potvrdena = 0 AND (titul_id = $t OR epizoda_id IN (SELECT id FROM epizoda WHERE titul_id = $t));
            DELETE FROM kandidat WHERE titul_id = $t;
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>
    /// The user says a version is another title: it moves to a new title of the given name and year (found by its key when
    /// there is one), unconfirmed so the sources look for it. Returns the new title's id.
    /// </summary>
    public static long PresunVerziu(SqliteConnection spojenie, long verziaId, DruhTitulu druh, string nazov, int? rok, long teraz)
    {
        using var citaj = Kniznica.Prikaz(spojenie, """
            SELECT v.titul_id, e.seria, e.cislo FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id WHERE v.id = $v
            """);
        citaj.Parameters.AddWithValue("$v", verziaId);
        long? stary;
        int? seria = null, cislo = null;
        using (var c = citaj.ExecuteReader())
        {
            if (!c.Read())
            {
                throw new InvalidOperationException("Verzia už v knižnici nie je.");
            }

            stary = c.IsDBNull(0) ? null : c.GetInt64(0);
            if (!c.IsDBNull(1))
            {
                seria = c.GetInt32(1);
                cislo = c.GetInt32(2);
            }
        }

        if ((druh == DruhTitulu.Serial) != (seria is not null))
        {
            throw new InvalidOperationException("Verzia filmu sa nedá presunúť k seriálu, ani epizóda k filmu.");
        }

        var navrh = new NavrhTitulu(druh, nazov.Trim(), rok, null, seria, cislo is { } x ? [x] : [], null, [], TriedaKvality.Nezistena, null,
            IstotaNavrhu.Neisty);
        var titulId = NajdiTitul(spojenie, navrh) ?? VytvorTitul(spojenie, navrh, teraz);
        PridajKluc(spojenie, titulId, Normalizacia.Kluc(druh, navrh.Nazov, rok));
        using (var presun = Kniznica.Prikaz(spojenie, druh == DruhTitulu.Film
                   ? "UPDATE verzia SET titul_id = $t, potvrdena = 0 WHERE id = $v"
                   : "UPDATE verzia SET epizoda_id = $e, potvrdena = 0 WHERE id = $v; DELETE FROM verzia_dalsia_epizoda WHERE verzia_id = $v"))
        {
            presun.Parameters.AddWithValue("$t", titulId);
            presun.Parameters.AddWithValue("$v", verziaId);
            presun.Parameters.AddWithValue("$e", seria is { } s && cislo is { } c2 ? Epizoda(spojenie, titulId, s, c2) : DBNull.Value);
            presun.ExecuteNonQuery();
        }

        if (stary is { } st)
        {
            OdstranOpusteny(spojenie, st);
        }

        OdstranOpustene(spojenie);
        return titulId;
    }

    /// <summary>Candidates the sources offered for a title (replaces those of the same source).</summary>
    public static void UlozKandidatov(SqliteConnection spojenie, long titulId, ZdrojUdajov zdroj, IReadOnlyList<Kandidat> kandidati)
    {
        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM kandidat WHERE titul_id = $t AND zdroj = $z"))
        {
            zmaz.Parameters.AddWithValue("$t", titulId);
            zmaz.Parameters.AddWithValue("$z", (int)zdroj);
            zmaz.ExecuteNonQuery();
        }

        var poradie = 0;
        foreach (var k in kandidati.Where(k => k.Zdroj == zdroj).Take(12))
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                INSERT OR REPLACE INTO kandidat(titul_id, zdroj, identifikator, druh, nazov, original, rok, zhoda, plagat, poradie)
                VALUES ($t, $z, $i, $d, $n, $o, $r, $zh, $p, $po)
                """);
            prikaz.Parameters.AddWithValue("$t", titulId);
            prikaz.Parameters.AddWithValue("$z", (int)zdroj);
            prikaz.Parameters.AddWithValue("$i", k.Identifikator);
            prikaz.Parameters.AddWithValue("$d", (int)k.Druh);
            prikaz.Parameters.AddWithValue("$n", k.Nazov);
            prikaz.Parameters.AddWithValue("$o", (object?)k.Original ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$r", (object?)k.Rok ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$zh", Math.Clamp(k.Zhoda, 0, 1));
            prikaz.Parameters.AddWithValue("$p", k.Plagat is { } p ? p.AbsoluteUri : DBNull.Value);
            prikaz.Parameters.AddWithValue("$po", poradie++);
            prikaz.ExecuteNonQuery();
        }
    }

    /// <summary>The candidates of a title, best first.</summary>
    public static IReadOnlyList<Kandidat> Kandidati(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT zdroj, identifikator, druh, nazov, original, rok, zhoda, plagat FROM kandidat WHERE titul_id = $t
            ORDER BY zhoda DESC, zdroj, poradie
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<Kandidat>();
        while (c.Read())
        {
            vysledok.Add(new Kandidat((ZdrojUdajov)c.GetInt32(0), c.GetString(1), (DruhTitulu)c.GetInt32(2), c.GetString(3),
                c.IsDBNull(4) ? null : c.GetString(4), c.IsDBNull(5) ? null : c.GetInt32(5), c.GetDouble(6),
                c.IsDBNull(7) || !Uri.TryCreate(c.GetString(7), UriKind.Absolute, out var u) ? null : u));
        }

        return vysledok;
    }

    /// <summary>Remembers that the sources were searched for a title now (it is not searched again soon).</summary>
    public static void OznacHladany(SqliteConnection spojenie, long titulId, long teraz)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE titul SET hladany = $teraz WHERE id = $t");
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Remembers that the sources had nothing more about a title (an identifier they do not know any more).</summary>
    public static void OznacNacitany(SqliteConnection spojenie, long titulId, long teraz)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE titul SET nacitany = $teraz, hladany = coalesce(hladany, $teraz) WHERE id = $t");
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>
    /// Titles the sources should look at: those with a version (or wanted) whose data were never loaded and that were not
    /// searched since <paramref name="hladatZnovaPo"/> ticks ago; and series loaded longer ago than
    /// <paramref name="obnovitSerialyPo"/> (new episodes). Those never searched come first.
    /// </summary>
    public static IReadOnlyList<TitulNaDoplnenie> NaDoplnenie(
        SqliteConnection spojenie, int najviac, long teraz, long hladatZnovaPo, long obnovitSerialyPo = long.MaxValue / 2)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT t.id, t.druh, t.nazov, t.original, t.rok,
                   (SELECT hodnota FROM identifikator i WHERE i.titul_id = t.id AND i.zdroj = 3 AND i.druh = 1 LIMIT 1),
                   (SELECT hodnota FROM identifikator i WHERE i.titul_id = t.id AND i.zdroj = 4 AND i.druh = 1 LIMIT 1),
                   t.hladany, t.nacitany,
                   EXISTS (SELECT 1 FROM verzia v WHERE v.potvrdena = 0
                           AND (v.titul_id = t.id OR v.epizoda_id IN (SELECT id FROM epizoda WHERE titul_id = t.id)))
            FROM titul t
            WHERE (t.zelany = 1
                   OR EXISTS (SELECT 1 FROM verzia v WHERE v.titul_id = t.id)
                   OR EXISTS (SELECT 1 FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = t.id))
              AND ((t.nacitany IS NULL AND (t.hladany IS NULL OR t.hladany < $znova))
                   OR (t.druh = 2 AND t.nacitany < $obnovit))
            ORDER BY t.hladany IS NOT NULL, t.id
            LIMIT $najviac
            """);
        prikaz.Parameters.AddWithValue("$znova", teraz - hladatZnovaPo);
        prikaz.Parameters.AddWithValue("$obnovit", teraz - obnovitSerialyPo);
        prikaz.Parameters.AddWithValue("$najviac", najviac);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<TitulNaDoplnenie>();
        while (c.Read())
        {
            vysledok.Add(new TitulNaDoplnenie(c.GetInt64(0), (DruhTitulu)c.GetInt32(1), c.GetString(2), c.IsDBNull(3) ? null : c.GetString(3),
                c.IsDBNull(4) ? null : c.GetInt32(4), c.IsDBNull(5) ? null : c.GetString(5), c.IsDBNull(6) ? null : c.GetString(6),
                c.IsDBNull(7) ? null : c.GetInt64(7), c.IsDBNull(8) ? null : c.GetInt64(8), c.GetInt32(9) == 1));
        }

        return vysledok;
    }

    /// <summary>
    /// The user writes a field of a title: it is locked, no source changes it any more (<paramref name="hodnota"/> null
    /// clears it, except the name). Fields: nazov, original, rok, dej, dlzka_min.
    /// </summary>
    public static void UpravPole(SqliteConnection spojenie, long titulId, string pole, object? hodnota, long teraz)
    {
        if (pole is not ("nazov" or "original" or "rok" or "dej" or "dlzka_min"))
        {
            throw new ArgumentException("Toto pole sa nedá upraviť.", nameof(pole));
        }

        if (pole == "nazov" && hodnota is not string { Length: > 0 })
        {
            throw new ArgumentException("Názov nemôže byť prázdny.", nameof(hodnota));
        }

        using (var prikaz = Kniznica.Prikaz(spojenie, $"UPDATE titul SET {pole} = $h, zmeneny = $teraz WHERE id = $id"))
        {
            prikaz.Parameters.AddWithValue("$h", hodnota ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$teraz", teraz);
            prikaz.Parameters.AddWithValue("$id", titulId);
            prikaz.ExecuteNonQuery();
        }

        ZapisPovod(spojenie, DruhObjektu.Titul, titulId, pole, ZdrojUdajov.Pouzivatel, teraz, zamknute: true);
        PridajKluceNazvov(spojenie, titulId);
    }

    /// <summary>Unlocks a field the user wrote: the next source fills it again.</summary>
    public static void Odomkni(SqliteConnection spojenie, DruhObjektu druh, long objektId, string pole)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "DELETE FROM povod_pola WHERE druh = $d AND objekt_id = $o AND pole = $p");
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$o", objektId);
        prikaz.Parameters.AddWithValue("$p", pole);
        prikaz.ExecuteNonQuery();
        using var hladat = Kniznica.Prikaz(spojenie, "UPDATE titul SET nacitany = NULL, hladany = NULL WHERE id = $o AND $d = 1");
        hladat.Parameters.AddWithValue("$d", (int)druh);
        hladat.Parameters.AddWithValue("$o", objektId);
        hladat.ExecuteNonQuery();
    }

    /// <summary>The user's own data of a title: rating 1–10, note, on the wish list.</summary>
    public static void NastavVlastne(SqliteConnection spojenie, long titulId, int? hodnotenie, string? poznamka, bool zelany, long teraz)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            UPDATE titul SET moje_hodnotenie = $h, moja_poznamka = $p, zelany = $z, zmeneny = $teraz WHERE id = $id
            """);
        prikaz.Parameters.AddWithValue("$h", hodnotenie is >= 1 and <= 10 ? hodnotenie : DBNull.Value);
        prikaz.Parameters.AddWithValue("$p", string.IsNullOrWhiteSpace(poznamka) ? DBNull.Value : poznamka.Trim());
        prikaz.Parameters.AddWithValue("$z", zelany ? 1 : 0);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.Parameters.AddWithValue("$id", titulId);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Marks a film or an episode as watched now (true) or forgets all its watching (false).</summary>
    public static void NastavVidene(SqliteConnection spojenie, long? titulId, long? epizodaId, bool videne, long teraz)
    {
        if ((titulId is null) == (epizodaId is null))
        {
            throw new ArgumentException("Pozretie patrí filmu alebo epizóde.");
        }

        using var prikaz = Kniznica.Prikaz(spojenie, videne
            ? "INSERT INTO pozretie(titul_id, epizoda_id, kedy) VALUES ($t, $e, $teraz)"
            : "DELETE FROM pozretie WHERE titul_id IS $t AND epizoda_id IS $e");
        prikaz.Parameters.AddWithValue("$t", (object?)titulId ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$e", (object?)epizodaId ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>
    /// Watching from another program (Jellyfin) by TMDB ids: a film, or an episode of a series the library knows; written
    /// only where the library has no watching yet. Returns how many were written.
    /// </summary>
    public static int ZapisPozretia(SqliteConnection spojenie, IEnumerable<(DruhTitulu Druh, int TmdbId, int? Seria, int? Cislo, long Kedy)> pozretia)
    {
        var zapisane = 0;
        foreach (var (druh, tmdb, seria, cislo, kedy) in pozretia)
        {
            if (Vlastnik(spojenie, ZdrojUdajov.Tmdb, DruhObjektu.Titul, TmdbIdentifikator(druh, tmdb)) is not { } titulId)
            {
                continue;
            }

            using var prikaz = Kniznica.Prikaz(spojenie, druh == DruhTitulu.Film
                ? """
                  INSERT INTO pozretie(titul_id, kedy) SELECT $t, $k WHERE NOT EXISTS (SELECT 1 FROM pozretie WHERE titul_id = $t)
                  """
                : """
                  INSERT INTO pozretie(epizoda_id, kedy)
                  SELECT e.id, $k FROM epizoda e WHERE e.titul_id = $t AND e.seria = $s AND e.cislo = $c
                    AND NOT EXISTS (SELECT 1 FROM pozretie p WHERE p.epizoda_id = e.id)
                  """);
            prikaz.Parameters.AddWithValue("$t", titulId);
            prikaz.Parameters.AddWithValue("$k", kedy);
            prikaz.Parameters.AddWithValue("$s", (object?)seria ?? DBNull.Value);
            prikaz.Parameters.AddWithValue("$c", (object?)cislo ?? DBNull.Value);
            zapisane += prikaz.ExecuteNonQuery();
        }

        return zapisane;
    }

    /// <summary>
    /// Writes a collection from a source with all its parts: parts the library does not know become titles without a
    /// version (A2 – the missing parts). Returns the collection's id.
    /// </summary>
    public static long ZapisKolekciu(SqliteConnection spojenie, UdajeKolekcie kolekcia, long? plagatId, IReadOnlyList<ZdrojUdajov> poradie, long teraz)
    {
        long id;
        if (Vlastnik(spojenie, kolekcia.Zdroj, DruhObjektu.Kolekcia, kolekcia.Identifikator) is { } znama)
        {
            id = znama;
        }
        else
        {
            using var nova = Kniznica.Prikaz(spojenie, "INSERT INTO kolekcia(nazov, vytvorena) VALUES ($n, $teraz) RETURNING id");
            nova.Parameters.AddWithValue("$n", kolekcia.Nazov.Trim());
            nova.Parameters.AddWithValue("$teraz", teraz);
            id = (long)nova.ExecuteScalar()!;
            PridajIdentifikator(spojenie, kolekcia.Zdroj, DruhObjektu.Kolekcia, kolekcia.Identifikator, id);
        }

        var polia = new (string Pole, object? Hodnota)[]
        {
            ("nazov", string.IsNullOrWhiteSpace(kolekcia.Nazov) ? null : kolekcia.Nazov.Trim()),
            ("dej", string.IsNullOrWhiteSpace(kolekcia.Dej) ? null : kolekcia.Dej.Trim()),
            ("plagat_id", plagatId),
        };
        foreach (var (pole, hodnota) in polia)
        {
            if (hodnota is not null && SmieZapisat(spojenie, DruhObjektu.Kolekcia, id, pole, kolekcia.Zdroj, poradie))
            {
                using var prikaz = Kniznica.Prikaz(spojenie, $"UPDATE kolekcia SET {pole} = $h WHERE id = $id");
                prikaz.Parameters.AddWithValue("$h", hodnota);
                prikaz.Parameters.AddWithValue("$id", id);
                prikaz.ExecuteNonQuery();
                ZapisPovod(spojenie, DruhObjektu.Kolekcia, id, pole, kolekcia.Zdroj, teraz, zamknute: false);
            }
        }

        var poradieDielu = 0;
        foreach (var diel in kolekcia.Diely)
        {
            var titulId = TitulZoZdroja(spojenie, diel, teraz);
            using var clen = Kniznica.Prikaz(spojenie, """
                INSERT INTO kolekcia_titul(kolekcia_id, titul_id, poradie) VALUES ($k, $t, $p)
                ON CONFLICT(kolekcia_id, titul_id) DO UPDATE SET poradie = excluded.poradie
                """);
            clen.Parameters.AddWithValue("$k", id);
            clen.Parameters.AddWithValue("$t", titulId);
            clen.Parameters.AddWithValue("$p", poradieDielu++);
            clen.ExecuteNonQuery();
        }

        return id;
    }

    /// <summary>Identifiers of a source whose titles already have a poster (their posters are not downloaded again).</summary>
    public static HashSet<string> IdentifikatorySPlagatom(SqliteConnection spojenie, ZdrojUdajov zdroj, IEnumerable<string> identifikatory)
    {
        var vysledok = new HashSet<string>(StringComparer.Ordinal);
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT 1 FROM identifikator i JOIN titul t ON t.id = i.titul_id
            WHERE i.zdroj = $z AND i.druh = 1 AND i.hodnota = $h AND t.plagat_id IS NOT NULL
            """);
        prikaz.Parameters.AddWithValue("$z", (int)zdroj);
        var hodnota = prikaz.Parameters.AddWithValue("$h", "");
        foreach (var id in identifikatory)
        {
            hodnota.Value = id;
            if (prikaz.ExecuteScalar() is not null)
            {
                vysledok.Add(id);
            }
        }

        return vysledok;
    }

    /// <summary>A poster from a source for a title (a part of a collection), unless a locked or better one is there.</summary>
    public static void NastavPlagat(SqliteConnection spojenie, long titulId, ZdrojUdajov zdroj, long plagatId, IReadOnlyList<ZdrojUdajov> poradie, long teraz)
    {
        if (!SmieZapisat(spojenie, DruhObjektu.Titul, titulId, "plagat_id", zdroj, poradie))
        {
            return;
        }

        using var prikaz = Kniznica.Prikaz(spojenie, "UPDATE titul SET plagat_id = $p WHERE id = $t");
        prikaz.Parameters.AddWithValue("$p", plagatId);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.ExecuteNonQuery();
        ZapisPovod(spojenie, DruhObjektu.Titul, titulId, "plagat_id", zdroj, teraz, zamknute: false);
    }

    /// <summary>
    /// The title a source names (a part of a collection, an entry of a HISTRA list): found by the identifier, else by
    /// the key of its name and year, else created without a version.
    /// </summary>
    public static long TitulZoZdroja(SqliteConnection spojenie, Kandidat titul, long teraz)
    {
        if (Vlastnik(spojenie, titul.Zdroj, DruhObjektu.Titul, titul.Identifikator) is { } znamy)
        {
            return znamy;
        }

        long? id = null;
        foreach (var meno in new[] { titul.Original, titul.Nazov }.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                SELECT k.titul_id FROM titul_kluc k WHERE k.kluc = $k
                  AND NOT EXISTS (SELECT 1 FROM identifikator i WHERE i.titul_id = k.titul_id AND i.zdroj = $z)
                LIMIT 1
                """);
            prikaz.Parameters.AddWithValue("$k", Normalizacia.Kluc(titul.Druh, meno!, titul.Rok));
            prikaz.Parameters.AddWithValue("$z", (int)titul.Zdroj);
            if (prikaz.ExecuteScalar() is long najdeny)
            {
                id = najdeny;
                break;
            }
        }

        if (id is null)
        {
            using var novy = Kniznica.Prikaz(spojenie, """
                INSERT INTO titul(druh, nazov, original, rok, vytvoreny, zmeneny) VALUES ($d, $n, $o, $r, $teraz, $teraz) RETURNING id
                """);
            novy.Parameters.AddWithValue("$d", (int)titul.Druh);
            novy.Parameters.AddWithValue("$n", titul.Nazov.Trim());
            novy.Parameters.AddWithValue("$o", string.IsNullOrWhiteSpace(titul.Original) ? DBNull.Value : titul.Original.Trim());
            novy.Parameters.AddWithValue("$r", titul.Rok is >= 1870 and <= 2200 ? titul.Rok : DBNull.Value);
            novy.Parameters.AddWithValue("$teraz", teraz);
            id = (long)novy.ExecuteScalar()!;
            foreach (var pole in new[] { "nazov", "original", "rok" })
            {
                ZapisPovod(spojenie, DruhObjektu.Titul, id.Value, pole, titul.Zdroj, teraz, zamknute: false);
            }

            PridajKluceNazvov(spojenie, id.Value);
        }

        PridajIdentifikator(spojenie, titul.Zdroj, DruhObjektu.Titul, titul.Identifikator, id.Value);
        return id.Value;
    }

    /// <summary>
    /// A list of the user's from HISTRA, compared with the library: each entry points to its title, which is created
    /// without a version when the library does not know it (A2 – "nemám"). Entries no longer in the list go.
    /// </summary>
    public static long ZapisZoznamHistra(SqliteConnection spojenie, string histraId, string nazov, IReadOnlyList<PolozkaZoznamuHistra> polozky, long teraz)
    {
        using var zoznam = Kniznica.Prikaz(spojenie, """
            INSERT INTO histra_zoznam(histra_id, nazov, synchronizovany) VALUES ($h, $n, $teraz)
            ON CONFLICT(histra_id) DO UPDATE SET nazov = excluded.nazov, synchronizovany = excluded.synchronizovany
            RETURNING id
            """);
        zoznam.Parameters.AddWithValue("$h", histraId);
        zoznam.Parameters.AddWithValue("$n", nazov.Trim().Length > 0 ? nazov.Trim() : histraId);
        zoznam.Parameters.AddWithValue("$teraz", teraz);
        var id = (long)zoznam.ExecuteScalar()!;
        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM histra_polozka WHERE zoznam_id = $z"))
        {
            zmaz.Parameters.AddWithValue("$z", id);
            zmaz.ExecuteNonQuery();
        }

        foreach (var p in polozky.DistinctBy(p => p.Titul.Identifikator))
        {
            var titulId = p.TmdbId is { } tmdb && Vlastnik(spojenie, ZdrojUdajov.Tmdb, DruhObjektu.Titul, TmdbIdentifikator(p.Titul.Druh, tmdb)) is { } podlaTmdb
                ? podlaTmdb
                : TitulZoZdroja(spojenie, p.Titul, teraz);
            PridajIdentifikator(spojenie, ZdrojUdajov.Histra, DruhObjektu.Titul, p.Titul.Identifikator, titulId);
            if (p.TmdbId is { } t)
            {
                PridajIdentifikator(spojenie, ZdrojUdajov.Tmdb, DruhObjektu.Titul, TmdbIdentifikator(p.Titul.Druh, t), titulId);
            }

            using var polozka = Kniznica.Prikaz(spojenie, """
                INSERT OR REPLACE INTO histra_polozka(zoznam_id, histra_titul, nazov, rok, titul_id) VALUES ($z, $h, $n, $r, $t)
                """);
            polozka.Parameters.AddWithValue("$z", id);
            polozka.Parameters.AddWithValue("$h", p.Titul.Identifikator);
            polozka.Parameters.AddWithValue("$n", p.Titul.Nazov);
            polozka.Parameters.AddWithValue("$r", (object?)p.Titul.Rok ?? DBNull.Value);
            polozka.Parameters.AddWithValue("$t", titulId);
            polozka.ExecuteNonQuery();
        }

        return id;
    }

    /// <summary>Removes titles nobody needs any more: without versions, not wanted, in no collection and no HISTRA list.</summary>
    public static int OdstranOpustene(SqliteConnection spojenie) =>
        PrikazOpustenych(spojenie, null);

    private static void OdstranOpusteny(SqliteConnection spojenie, long titulId) => PrikazOpustenych(spojenie, titulId);

    private static int PrikazOpustenych(SqliteConnection spojenie, long? titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            DELETE FROM titul WHERE ($t IS NULL OR id = $t)
              AND zelany = 0 AND moje_hodnotenie IS NULL AND moja_poznamka IS NULL
              AND NOT EXISTS (SELECT 1 FROM verzia v WHERE v.titul_id = titul.id)
              AND NOT EXISTS (SELECT 1 FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = titul.id)
              AND NOT EXISTS (SELECT 1 FROM kolekcia_titul k WHERE k.titul_id = titul.id)
              AND NOT EXISTS (SELECT 1 FROM histra_polozka h WHERE h.titul_id = titul.id)
              AND NOT EXISTS (SELECT 1 FROM pozretie p WHERE p.titul_id = titul.id
                              OR p.epizoda_id IN (SELECT id FROM epizoda WHERE titul_id = titul.id))
            """);
        prikaz.Parameters.AddWithValue("$t", (object?)titulId ?? DBNull.Value);
        return prikaz.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ helpers

    private static long? NajdiTitul(SqliteConnection spojenie, NavrhTitulu navrh)
    {
        string? tmdb = navrh.TmdbId is { } id ? TmdbIdentifikator(navrh.Druh, id) : null;
        if (tmdb is not null && Vlastnik(spojenie, ZdrojUdajov.Tmdb, DruhObjektu.Titul, tmdb) is { } podlaId)
        {
            return podlaId;
        }

        // by the key of the name; a title with another TMDB id is another title of the same name (a remake)
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT k.titul_id FROM titul_kluc k JOIN titul t ON t.id = k.titul_id
            WHERE k.kluc = $k AND t.druh = $d
              AND ($tmdb IS NULL OR NOT EXISTS (SELECT 1 FROM identifikator i WHERE i.titul_id = t.id AND i.zdroj = 3))
            ORDER BY t.id LIMIT 1
            """);
        prikaz.Parameters.AddWithValue("$k", Normalizacia.Kluc(navrh.Druh, navrh.Nazov, navrh.Rok));
        prikaz.Parameters.AddWithValue("$d", (int)navrh.Druh);
        prikaz.Parameters.AddWithValue("$tmdb", (object?)tmdb ?? DBNull.Value);
        return prikaz.ExecuteScalar() is long najdeny ? najdeny : null;
    }

    private static long VytvorTitul(SqliteConnection spojenie, NavrhTitulu navrh, long teraz)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT INTO titul(druh, nazov, original, rok, vytvoreny, zmeneny) VALUES ($d, $n, $o, $r, $teraz, $teraz) RETURNING id
            """);
        var isty = navrh.Istota == IstotaNavrhu.Isty;
        prikaz.Parameters.AddWithValue("$d", (int)navrh.Druh);
        prikaz.Parameters.AddWithValue("$n", navrh.Nazov);
        prikaz.Parameters.AddWithValue("$o", isty ? navrh.Nazov : DBNull.Value);
        prikaz.Parameters.AddWithValue("$r", navrh.Rok is >= 1870 and <= 2200 ? navrh.Rok : DBNull.Value);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        var id = (long)prikaz.ExecuteScalar()!;

        // only a sure name is an origin (UNIVERSUM's names are the original name and year); a guess has none, so the
        // first source replaces it – and the display name always comes from a source when one knows the title
        if (isty)
        {
            ZapisPovod(spojenie, DruhObjektu.Titul, id, "original", ZdrojUdajov.NazovSuboru, teraz, zamknute: false);
            if (navrh.Rok is not null)
            {
                ZapisPovod(spojenie, DruhObjektu.Titul, id, "rok", ZdrojUdajov.NazovSuboru, teraz, zamknute: false);
            }
        }

        return id;
    }

    private static long Epizoda(SqliteConnection spojenie, long titulId, int seria, int cislo)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT INTO epizoda(titul_id, seria, cislo) VALUES ($t, $s, $c)
            ON CONFLICT(titul_id, seria, cislo) DO UPDATE SET seria = seria
            RETURNING id
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.Parameters.AddWithValue("$s", seria);
        prikaz.Parameters.AddWithValue("$c", cislo);
        return (long)prikaz.ExecuteScalar()!;
    }

    private static DruhTitulu? Druh(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT druh FROM titul WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", titulId);
        return prikaz.ExecuteScalar() is long druh ? (DruhTitulu)druh : null;
    }

    private static long? Vlastnik(SqliteConnection spojenie, ZdrojUdajov zdroj, DruhObjektu druh, string hodnota)
    {
        var stlpec = druh switch
        {
            DruhObjektu.Titul => "titul_id",
            DruhObjektu.Epizoda => "epizoda_id",
            DruhObjektu.Kolekcia => "kolekcia_id",
            _ => "osoba_id",
        };
        using var prikaz = Kniznica.Prikaz(spojenie, $"SELECT {stlpec} FROM identifikator WHERE zdroj = $z AND druh = $d AND hodnota = $h");
        prikaz.Parameters.AddWithValue("$z", (int)zdroj);
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$h", hodnota);
        return prikaz.ExecuteScalar() is long id ? id : null;
    }

    private static void PridajIdentifikator(SqliteConnection spojenie, ZdrojUdajov zdroj, DruhObjektu druh, string hodnota, long objektId)
    {
        var stlpec = druh switch
        {
            DruhObjektu.Titul => "titul_id",
            DruhObjektu.Epizoda => "epizoda_id",
            DruhObjektu.Kolekcia => "kolekcia_id",
            _ => "osoba_id",
        };
        using var prikaz = Kniznica.Prikaz(spojenie, $"INSERT OR IGNORE INTO identifikator(zdroj, druh, hodnota, {stlpec}) VALUES ($z, $d, $h, $o)");
        prikaz.Parameters.AddWithValue("$z", (int)zdroj);
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$h", hodnota);
        prikaz.Parameters.AddWithValue("$o", objektId);
        prikaz.ExecuteNonQuery();
    }

    private static void PridajKluc(SqliteConnection spojenie, long titulId, string kluc)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "INSERT OR IGNORE INTO titul_kluc(kluc, titul_id) VALUES ($k, $t)");
        prikaz.Parameters.AddWithValue("$k", kluc);
        prikaz.Parameters.AddWithValue("$t", titulId);
        prikaz.ExecuteNonQuery();
    }

    /// <summary>The keys of the title's present name and original name (a file named either way finds it).</summary>
    private static void PridajKluceNazvov(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT druh, nazov, original, rok FROM titul WHERE id = $id");
        prikaz.Parameters.AddWithValue("$id", titulId);
        var kluce = new List<string>();
        using (var c = prikaz.ExecuteReader())
        {
            if (!c.Read())
            {
                return;
            }

            var druh = (DruhTitulu)c.GetInt32(0);
            int? rok = c.IsDBNull(3) ? null : c.GetInt32(3);
            kluce.Add(Normalizacia.Kluc(druh, c.GetString(1), rok));
            if (!c.IsDBNull(2))
            {
                kluce.Add(Normalizacia.Kluc(druh, c.GetString(2), rok));
            }
        }

        foreach (var kluc in kluce.Where(k => !k.Contains("::", StringComparison.Ordinal) || k.Length > 4))
        {
            PridajKluc(spojenie, titulId, kluc);
        }
    }

    private static bool SmieZapisat(SqliteConnection spojenie, DruhObjektu druh, long objektId, string pole, ZdrojUdajov zdroj, IReadOnlyList<ZdrojUdajov> poradie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT zdroj, zamknute FROM povod_pola WHERE druh = $d AND objekt_id = $o AND pole = $p");
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$o", objektId);
        prikaz.Parameters.AddWithValue("$p", pole);
        using var c = prikaz.ExecuteReader();
        return !c.Read() || PoradieZdrojov.MozePrepisat((ZdrojUdajov)c.GetInt32(0), c.GetInt32(1) == 1, zdroj, poradie);
    }

    private static void ZapisPovod(SqliteConnection spojenie, DruhObjektu druh, long objektId, string pole, ZdrojUdajov zdroj, long teraz, bool zamknute)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            INSERT OR REPLACE INTO povod_pola(druh, objekt_id, pole, zdroj, cas, zamknute) VALUES ($d, $o, $p, $z, $teraz, $zam)
            """);
        prikaz.Parameters.AddWithValue("$d", (int)druh);
        prikaz.Parameters.AddWithValue("$o", objektId);
        prikaz.Parameters.AddWithValue("$p", pole);
        prikaz.Parameters.AddWithValue("$z", (int)zdroj);
        prikaz.Parameters.AddWithValue("$teraz", teraz);
        prikaz.Parameters.AddWithValue("$zam", zamknute ? 1 : 0);
        prikaz.ExecuteNonQuery();
    }

    private static void NastavZanre(SqliteConnection spojenie, long titulId, IReadOnlyList<string> zanre)
    {
        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM titul_zaner WHERE titul_id = $t"))
        {
            zmaz.Parameters.AddWithValue("$t", titulId);
            zmaz.ExecuteNonQuery();
        }

        foreach (var zaner in zanre.Select(z => z.Trim()).Where(z => z.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
        {
            using var prikaz = Kniznica.Prikaz(spojenie, """
                INSERT INTO zaner(nazov, kluc) VALUES ($n, $k) ON CONFLICT(nazov) DO UPDATE SET kluc = excluded.kluc RETURNING id
                """);
            prikaz.Parameters.AddWithValue("$n", zaner);
            prikaz.Parameters.AddWithValue("$k", Normalizacia.Nazov(zaner));
            var zanerId = (long)prikaz.ExecuteScalar()!;
            using var spoj = Kniznica.Prikaz(spojenie, "INSERT OR IGNORE INTO titul_zaner(titul_id, zaner_id) VALUES ($t, $z)");
            spoj.Parameters.AddWithValue("$t", titulId);
            spoj.Parameters.AddWithValue("$z", zanerId);
            spoj.ExecuteNonQuery();
        }
    }

    private static void NastavOsoby(SqliteConnection spojenie, long titulId, ZdrojUdajov zdroj, IReadOnlyList<UdajeOsoby> osoby)
    {
        using (var zmaz = Kniznica.Prikaz(spojenie, "DELETE FROM titul_osoba WHERE titul_id = $t"))
        {
            zmaz.Parameters.AddWithValue("$t", titulId);
            zmaz.ExecuteNonQuery();
        }

        foreach (var o in osoby.Where(o => !string.IsNullOrWhiteSpace(o.Meno) && o.Identifikator.Length > 0).Take(60))
        {
            long osobaId;
            if (Vlastnik(spojenie, zdroj, DruhObjektu.Osoba, o.Identifikator) is { } znama)
            {
                osobaId = znama;
            }
            else
            {
                using var nova = Kniznica.Prikaz(spojenie, "INSERT INTO osoba(meno) VALUES ($m) RETURNING id");
                nova.Parameters.AddWithValue("$m", o.Meno.Trim());
                osobaId = (long)nova.ExecuteScalar()!;
                PridajIdentifikator(spojenie, zdroj, DruhObjektu.Osoba, o.Identifikator, osobaId);
            }

            using var spoj = Kniznica.Prikaz(spojenie, """
                INSERT OR IGNORE INTO titul_osoba(titul_id, osoba_id, rola, postava, poradie) VALUES ($t, $o, $r, $p, $po)
                """);
            spoj.Parameters.AddWithValue("$t", titulId);
            spoj.Parameters.AddWithValue("$o", osobaId);
            spoj.Parameters.AddWithValue("$r", (int)o.Rola);
            spoj.Parameters.AddWithValue("$p", string.IsNullOrWhiteSpace(o.Postava) ? DBNull.Value : o.Postava.Trim());
            spoj.Parameters.AddWithValue("$po", o.Poradie);
            spoj.ExecuteNonQuery();
        }
    }
}

/// <summary>A title the sources should look at, with what is known to search for it.</summary>
/// <param name="Tmdb">Its TMDB identifier ("movie/603"), when known.</param>
/// <param name="Histra">Its HISTRA identifier, when known.</param>
/// <param name="NaPotvrdenie">It has a version that waits for confirmation.</param>
public sealed record TitulNaDoplnenie(
    long Id, DruhTitulu Druh, string Nazov, string? Original, int? Rok, string? Tmdb, string? Histra, long? Hladany, long? Nacitany, bool NaPotvrdenie);

/// <summary>An entry of a HISTRA list: the title as HISTRA names it ("movie/123") and its TMDB id when HISTRA gives one.</summary>
public sealed record PolozkaZoznamuHistra(Kandidat Titul, int? TmdbId);
