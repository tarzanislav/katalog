using System.Globalization;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A catalogue in a library file offered for import.</summary>
public sealed record KatalogVSubore(long Id, string Nazov, int Diskov, long Suborov);

/// <summary>What an import brought.</summary>
/// <param name="Preskocene">Disks left out because the library has them already ("D003 Filmy").</param>
/// <param name="Precislovane">Disks that got a new number, their number being taken ("D003 → D012 Filmy").</param>
public sealed record VysledokImportu(
    IReadOnlyList<string> Katalogy,
    int Diskov,
    long Suborov,
    int NovychTitulov,
    int ZlucenychTitulov,
    IReadOnlyList<string> Preskocene,
    IReadOnlyList<string> Precislovane);

/// <summary>
/// Export and import of one catalogue (A1). The export is a library file holding just that catalogue: its disks with
/// folders, files, technical data, scans and health, and the titles on them with everything known about them (pictures
/// stay behind; the sources load them again). The import takes catalogues from any library file into this library:
/// disks it already has are left out, taken disk numbers get new ones, and titles it knows (by the source's identifier
/// or by name and year) are joined rather than doubled.
/// </summary>
public static class PrenosKatalogu
{
    /// <summary>Writes catalogue <paramref name="katalogId"/> into a new library file <paramref name="ciel"/>.</summary>
    public static async Task ExportujAsync(Kniznica kniznica, long katalogId, string ciel, CancellationToken zrusenie = default)
    {
        ArgumentNullException.ThrowIfNull(kniznica);
        ciel = Path.GetFullPath(ciel);
        if (string.Equals(ciel, kniznica.Cesta, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Katalóg sa nedá exportovať do súboru samotnej knižnice.", nameof(ciel));
        }

        var docasny = ciel + ".tmp";
        File.Delete(docasny);
        try
        {
            await using (var citanie = await kniznica.OtvorCitanieAsync(zrusenie).ConfigureAwait(false))
            {
                if (Kniznica.Skalar(citanie, $"SELECT count(*) FROM katalog WHERE id = {katalogId}") is not 1L)
                {
                    throw new ArgumentException("Katalóg neexistuje.", nameof(katalogId));
                }

                using var kopia = Otvor(docasny);
                citanie.BackupDatabase(kopia);
            }

            zrusenie.ThrowIfCancellationRequested();
            using (var s = Otvor(docasny))
            {
                Kniznica.Vykonaj(s, "PRAGMA journal_mode = DELETE");
                Kniznica.Vykonaj(s, "BEGIN IMMEDIATE");
                using (var prikaz = Kniznica.Prikaz(s, """
                    DELETE FROM katalog WHERE id <> $k;
                    DELETE FROM uloha;
                    DELETE FROM playlist;
                    DELETE FROM ulozene_hladanie;
                    DELETE FROM histra_zoznam;
                    DELETE FROM nastavenie;
                    DELETE FROM obrazok;
                    DELETE FROM verzia WHERE NOT EXISTS (SELECT 1 FROM verzia_subor vs WHERE vs.verzia_id = verzia.id);
                    DELETE FROM titul WHERE id NOT IN (SELECT titul_id FROM verzia WHERE titul_id IS NOT NULL)
                                        AND id NOT IN (SELECT e.titul_id FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id);
                    DELETE FROM kolekcia WHERE id NOT IN (SELECT kolekcia_id FROM kolekcia_titul);
                    DELETE FROM osoba WHERE id NOT IN (SELECT osoba_id FROM titul_osoba);
                    DELETE FROM zaner WHERE id NOT IN (SELECT zaner_id FROM titul_zaner);
                    DELETE FROM stitok WHERE id NOT IN (SELECT stitok_id FROM titul_stitok);
                    """))
                {
                    prikaz.Parameters.AddWithValue("$k", katalogId);
                    prikaz.ExecuteNonQuery();
                }

                Kniznica.Vykonaj(s, "COMMIT");
                Kniznica.Vykonaj(s, "VACUUM");
            }

            File.Move(docasny, ciel, overwrite: true);
        }
        finally
        {
            File.Delete(docasny);
        }
    }

    /// <summary>The catalogues of a library file (a copy is read; the file itself stays untouched).</summary>
    public static IReadOnlyList<KatalogVSubore> Katalogy(string zdroj)
    {
        var kopia = PripravZdroj(zdroj);
        try
        {
            using var s = Otvor(kopia);
            using var prikaz = Kniznica.Prikaz(s, """
                SELECT k.id, k.nazov,
                       (SELECT count(*) FROM disk d WHERE d.katalog_id = k.id),
                       (SELECT count(*) FROM subor f JOIN disk d ON d.id = f.disk_id WHERE d.katalog_id = k.id AND f.stav = 0)
                FROM katalog k ORDER BY k.poradie, k.nazov
                """);
            using var citac = prikaz.ExecuteReader();
            var vysledok = new List<KatalogVSubore>();
            while (citac.Read())
            {
                vysledok.Add(new KatalogVSubore(citac.GetInt64(0), citac.GetString(1), citac.GetInt32(2), citac.GetInt64(3)));
            }

            return vysledok;
        }
        finally
        {
            File.Delete(kopia);
        }
    }

    /// <summary>
    /// Imports catalogues of library file <paramref name="zdroj"/> (all, or those with ids in <paramref name="katalogy"/>)
    /// in one write: either all of it comes in, or nothing.
    /// </summary>
    public static async Task<VysledokImportu> ImportujAsync(
        Kniznica kniznica, string zdroj, IReadOnlyCollection<long>? katalogy = null, CancellationToken zrusenie = default)
    {
        ArgumentNullException.ThrowIfNull(kniznica);
        if (string.Equals(Path.GetFullPath(zdroj), kniznica.Cesta, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Knižnica nemôže importovať samu seba.", nameof(zdroj));
        }

        var kopia = PripravZdroj(zdroj);
        try
        {
            return await kniznica.Zapis.ZapisAsync(s =>
            {
                using var z = Otvor(kopia);
                return new Import(kniznica, z, s, zrusenie).Vykonaj(katalogy);
            }).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(kopia);
        }
    }

    /// <summary>A private copy of the file, raised to this program's schema; refuses files that are no library or are newer.</summary>
    private static string PripravZdroj(string zdroj)
    {
        if (!File.Exists(zdroj))
        {
            throw new FileNotFoundException("Súbor neexistuje.", zdroj);
        }

        var kopia = Path.Combine(Path.GetTempPath(), $"{Jadro.Produkt.Nazov}-import-{Guid.NewGuid():N}{Jadro.Produkt.PriponaKniznice}");
        try
        {
            using (var povodny = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = zdroj, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                povodny.Open();
                using var ciel = Otvor(kopia);
                povodny.BackupDatabase(ciel);
            }

            using var s = Otvor(kopia);
            var id = Convert.ToInt32(Kniznica.Skalar(s, "PRAGMA application_id"), CultureInfo.InvariantCulture);
            var verzia = Convert.ToInt32(Kniznica.Skalar(s, "PRAGMA user_version"), CultureInfo.InvariantCulture);
            if (id != Kniznica.IdAplikacie || verzia < 1)
            {
                throw new CudziSuborException(zdroj);
            }

            if (verzia > Migracie.Posledna)
            {
                throw new NovsiaKniznicaException(verzia, Migracie.Posledna);
            }

            Migracie.Vykonaj(s, verzia);
            return kopia;
        }
        catch (SqliteException ex)
        {
            File.Delete(kopia);
            throw new CudziSuborException(zdroj + " (" + ex.Message + ")");
        }
        catch
        {
            File.Delete(kopia);
            throw;
        }
    }

    /// <summary>A connection to a file of its own (export, import copy): no pool, so the file can be moved right after.</summary>
    private static SqliteConnection Otvor(string cesta)
    {
        var s = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = cesta,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        s.Open();
        // trusted_schema off: a file from elsewhere cannot run functions with side effects from its schema
        Kniznica.Vykonaj(s, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE; PRAGMA temp_store = MEMORY; PRAGMA trusted_schema = OFF;");
        return s;
    }

    /// <summary>One import: reads the source copy <c>z</c> and writes into the library <c>c</c> within the writer's transaction.</summary>
    private sealed class Import(Kniznica kniznica, SqliteConnection z, SqliteConnection c, CancellationToken zrusenie)
    {
        private readonly Dictionary<string, SqliteCommand> _vkladanie = [];
        private readonly Dictionary<long, long> _disky = [];
        private readonly Dictionary<long, long> _priecinky = [];
        private readonly Dictionary<long, long> _subory = [];
        private readonly Dictionary<long, long> _tituly = [];
        private readonly Dictionary<long, long> _epizody = [];
        private readonly Dictionary<long, long> _osoby = [];
        private readonly Dictionary<long, long> _kolekcie = [];
        private readonly HashSet<long> _noveTituly = [];
        private readonly List<string> _preskocene = [];
        private readonly List<string> _precislovane = [];
        private int _zlucene;

        public VysledokImportu Vykonaj(IReadOnlyCollection<long>? vybrane)
        {
            try
            {
                // the source's ids of the imported files, for the queries that follow (in the private copy)
                Kniznica.Vykonaj(z, "CREATE TEMP TABLE importovane(id INTEGER PRIMARY KEY)");
                var katalogy = Riadky("SELECT * FROM katalog ORDER BY poradie, id")
                    .Where(k => vybrane is null || vybrane.Contains((long)k["id"]!))
                    .ToList();
                var nazvy = new List<string>();
                foreach (var katalog in katalogy)
                {
                    if (Katalog(katalog) is { } nazov)
                    {
                        nazvy.Add(nazov);
                    }
                }

                // titles of the imported versions (a film's own or an episode's series)
                var tituly = Stlpec($"""
                    SELECT DISTINCT coalesce(v.titul_id, e.titul_id) FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id
                    WHERE EXISTS (SELECT 1 FROM verzia_subor vs WHERE vs.verzia_id = v.id AND vs.subor_id IN (SELECT id FROM temp.importovane))
                    """);
                foreach (var titul in Riadky($"SELECT * FROM titul WHERE id IN ({Zoznam(tituly)}) ORDER BY id"))
                {
                    Titul(titul);
                }

                Verzie();
                return new VysledokImportu(nazvy, _disky.Count, _subory.Count, _noveTituly.Count, _zlucene, _preskocene, _precislovane);
            }
            finally
            {
                foreach (var prikaz in _vkladanie.Values)
                {
                    prikaz.Dispose();
                }
            }
        }

        // ---------------------------------------------------------- storages

        /// <summary>The catalogue with its disks; null when the library has all its disks already (nothing came in).</summary>
        private string? Katalog(Dictionary<string, object?> katalog)
        {
            var nazov = VolnyNazov((string)katalog["nazov"]!);
            katalog["nazov"] = nazov;
            katalog["poradie"] = Convert.ToInt64(Kniznica.Skalar(c, "SELECT coalesce(max(poradie), 0) + 1 FROM katalog"), CultureInfo.InvariantCulture);
            var stareId = (long)katalog["id"]!;
            var noveId = Vloz("katalog", katalog, bezId: true);

            var diskov = _disky.Count;
            // disks whose number is free first, so as few disks as possible get a new number
            var disky = Riadky("SELECT * FROM disk WHERE katalog_id = $k ORDER BY cislo", ("$k", stareId))
                .OrderBy(d => Existuje("SELECT 1 FROM disk WHERE cislo = $c", ("$c", d["cislo"])))
                .ToList();
            foreach (var disk in disky)
            {
                zrusenie.ThrowIfCancellationRequested();
                Disk(disk, noveId);
            }

            // the library has all the catalogue's disks already: nothing came in (an empty catalogue comes in empty)
            if (disky.Count > 0 && _disky.Count == diskov)
            {
                Vykonaj("DELETE FROM katalog WHERE id = $k", ("$k", noveId));
                return null;
            }

            return nazov;
        }

        private string VolnyNazov(string nazov)
        {
            var kandidat = nazov;
            for (var i = 2; Existuje("SELECT 1 FROM katalog WHERE nazov = $n", ("$n", kandidat)); i++)
            {
                kandidat = $"{nazov} ({i})";
            }

            return kandidat;
        }

        private void Disk(Dictionary<string, object?> disk, long katalogId)
        {
            var stareId = (long)disk["id"]!;
            var cislo = (long)disk["cislo"]!;
            var popis = $"D{cislo:000} {disk["nazov"]}";
            // the same volume (and the same root folder on it) is in the library already
            if (Existuje("""
                SELECT 1 FROM disk WHERE koren = $koren AND (
                    (guid_zvazku IS NOT NULL AND guid_zvazku = $guid)
                 OR (seriove_cislo_zvazku IS NOT NULL AND seriove_cislo_zvazku = $seriove AND velkost IS $velkost)
                 OR (adresa IS NOT NULL AND adresa = $adresa))
                """, ("$koren", disk["koren"]), ("$guid", disk["guid_zvazku"]), ("$seriove", disk["seriove_cislo_zvazku"]),
                ("$velkost", disk["velkost"]), ("$adresa", disk["adresa"])))
            {
                _preskocene.Add(popis);
                return;
            }

            if (Existuje("SELECT 1 FROM disk WHERE cislo = $c", ("$c", cislo)))
            {
                var nove = Convert.ToInt64(Kniznica.Skalar(c, "SELECT max(cislo) + 1 FROM disk"), CultureInfo.InvariantCulture);
                _precislovane.Add($"D{cislo:000} → D{nove:000} {disk["nazov"]}");
                disk["cislo"] = nove;
            }

            disk["katalog_id"] = katalogId;
            disk["nahlady"] = 0L;
            var noveId = Vloz("disk", disk, bezId: true);
            _disky[stareId] = noveId;

            // folders from the root down (a parent before its children), with ids from the library's own sequence
            foreach (var priecinok in Riadky("""
                WITH RECURSIVE strom(id, hlbka) AS (
                    SELECT id, 0 FROM priecinok WHERE disk_id = $d AND rodic_id IS NULL
                    UNION ALL SELECT p.id, s.hlbka + 1 FROM priecinok p JOIN strom s ON p.rodic_id = s.id)
                SELECT p.* FROM priecinok p JOIN strom s ON s.id = p.id ORDER BY s.hlbka, p.id
                """, ("$d", stareId)))
            {
                var stary = (long)priecinok["id"]!;
                var novy = kniznica.NovyIdPriecinka();
                priecinok["id"] = novy;
                priecinok["disk_id"] = noveId;
                priecinok["rodic_id"] = priecinok["rodic_id"] is long r ? _priecinky[r] : null;
                Vloz("priecinok", priecinok, bezId: false);
                _priecinky[stary] = novy;
            }

            using (var zapis = Kniznica.Prikaz(z, "INSERT INTO temp.importovane(id) VALUES ($id)"))
            {
                var parameter = zapis.Parameters.Add(new SqliteParameter("$id", 0L));
                foreach (var subor in Prud("SELECT * FROM subor WHERE disk_id = $d ORDER BY id", ("$d", stareId)))
                {
                    var stary = (long)subor["id"]!;
                    subor["disk_id"] = noveId;
                    subor["priecinok_id"] = _priecinky[(long)subor["priecinok_id"]!];
                    _subory[stary] = Vloz("subor", subor, bezId: true);
                    parameter.Value = stary;
                    zapis.ExecuteNonQuery();
                }
            }

            const string subory = "SELECT id FROM subor WHERE disk_id = $d";
            foreach (var udaje in Prud($"SELECT * FROM medialne_udaje WHERE subor_id IN ({subory})", ("$d", stareId)))
            {
                udaje["subor_id"] = _subory[(long)udaje["subor_id"]!];
                Vloz("medialne_udaje", udaje, bezId: false);
            }

            foreach (var stopa in Prud($"SELECT * FROM stopa WHERE subor_id IN ({subory})", ("$d", stareId)))
            {
                stopa["subor_id"] = _subory[(long)stopa["subor_id"]!];
                // an external subtitle file lies next to its video, on the same disk
                stopa["externy_subor_id"] = stopa["externy_subor_id"] is long e && _subory.TryGetValue(e, out var ne) ? ne : null;
                Vloz("stopa", stopa, bezId: true);
            }

            foreach (var mimo in Prud($"SELECT * FROM subor_mimo_kniznice WHERE subor_id IN ({subory})", ("$d", stareId)))
            {
                mimo["subor_id"] = _subory[(long)mimo["subor_id"]!];
                Vloz("subor_mimo_kniznice", mimo, bezId: false);
            }

            foreach (var tabulka in new[] { "sken", "zdravie_zaznam" })
            {
                foreach (var riadok in Riadky($"SELECT * FROM {tabulka} WHERE disk_id = $d", ("$d", stareId)))
                {
                    riadok["disk_id"] = noveId;
                    Vloz(tabulka, riadok, bezId: true);
                }
            }

            foreach (var vypozicka in Riadky("SELECT * FROM vypozicka WHERE disk_id = $d", ("$d", stareId)))
            {
                vypozicka["disk_id"] = noveId;
                Vloz("vypozicka", vypozicka, bezId: true);
            }
        }

        // ---------------------------------------------------------- titles

        private void Titul(Dictionary<string, object?> titul)
        {
            zrusenie.ThrowIfCancellationRequested();
            var stareId = (long)titul["id"]!;
            var existujuci = PodlaIdentifikatora(1, "titul_id", stareId) ?? PodlaKluca(stareId, (long)titul["druh"]!);
            if (existujuci is { } id)
            {
                _tituly[stareId] = id;
                _zlucene++;
                // what the user said of the title joins what the library knows
                Stitky(stareId, id);
                if (!Existuje("SELECT 1 FROM pozretie WHERE titul_id = $t", ("$t", id)))
                {
                    Pozretia("titul_id", stareId, id);
                }

                if ((long)titul["druh"]! == 2)
                {
                    foreach (var epizoda in Riadky("SELECT * FROM epizoda WHERE titul_id = $t ORDER BY seria, cislo", ("$t", stareId)))
                    {
                        Epizoda(epizoda, id);
                    }
                }

                return;
            }

            titul["plagat_id"] = null;
            // the pictures stayed in the other library: the sources load them again
            titul["nacitany"] = null;
            var noveId = Vloz("titul", titul, bezId: true);
            _tituly[stareId] = noveId;
            _noveTituly.Add(noveId);

            foreach (var kluc in Riadky("SELECT kluc FROM titul_kluc WHERE titul_id = $t", ("$t", stareId)))
            {
                Vykonaj("INSERT OR IGNORE INTO titul_kluc(kluc, titul_id) VALUES ($k, $t)", ("$k", kluc["kluc"]), ("$t", noveId));
            }

            Identifikatory(1, "titul_id", stareId, noveId);
            Povod(1, stareId, noveId);
            Pozretia("titul_id", stareId, noveId);
            Stitky(stareId, noveId);

            foreach (var kandidat in Riadky("SELECT * FROM kandidat WHERE titul_id = $t", ("$t", stareId)))
            {
                kandidat["titul_id"] = noveId;
                Vloz("kandidat", kandidat, bezId: false);
            }

            foreach (var zaner in Riadky("SELECT z.nazov, z.kluc FROM titul_zaner tz JOIN zaner z ON z.id = tz.zaner_id WHERE tz.titul_id = $t", ("$t", stareId)))
            {
                Vykonaj("INSERT OR IGNORE INTO zaner(nazov, kluc) VALUES ($n, $k)", ("$n", zaner["nazov"]), ("$k", zaner["kluc"]));
                Vykonaj("INSERT OR IGNORE INTO titul_zaner(titul_id, zaner_id) SELECT $t, id FROM zaner WHERE nazov = $n",
                    ("$t", noveId), ("$n", zaner["nazov"]));
            }

            foreach (var rola in Riadky("SELECT * FROM titul_osoba WHERE titul_id = $t", ("$t", stareId)))
            {
                rola["titul_id"] = noveId;
                rola["osoba_id"] = Osoba((long)rola["osoba_id"]!);
                VlozAleboNic("titul_osoba", rola);
            }

            foreach (var clen in Riadky("SELECT * FROM kolekcia_titul WHERE titul_id = $t", ("$t", stareId)))
            {
                clen["titul_id"] = noveId;
                clen["kolekcia_id"] = Kolekcia((long)clen["kolekcia_id"]!);
                VlozAleboNic("kolekcia_titul", clen);
            }

            if (!Existuje("SELECT 1 FROM vypozicka WHERE titul_id = $t AND do IS NULL", ("$t", noveId)))
            {
                foreach (var vypozicka in Riadky("SELECT * FROM vypozicka WHERE titul_id = $t", ("$t", stareId)))
                {
                    vypozicka["titul_id"] = noveId;
                    Vloz("vypozicka", vypozicka, bezId: true);
                }
            }

            if ((long)titul["druh"]! == 2)
            {
                foreach (var epizoda in Riadky("SELECT * FROM epizoda WHERE titul_id = $t ORDER BY seria, cislo", ("$t", stareId)))
                {
                    Epizoda(epizoda, noveId);
                }
            }
        }

        /// <summary>An episode joins the one of the same number in the series, or comes in new.</summary>
        private void Epizoda(Dictionary<string, object?> epizoda, long titulId)
        {
            var stareId = (long)epizoda["id"]!;
            using (var prikaz = Kniznica.Prikaz(c, "SELECT id FROM epizoda WHERE titul_id = $t AND seria = $s AND cislo = $c"))
            {
                prikaz.Parameters.AddWithValue("$t", titulId);
                prikaz.Parameters.AddWithValue("$s", epizoda["seria"]);
                prikaz.Parameters.AddWithValue("$c", epizoda["cislo"]);
                if (prikaz.ExecuteScalar() is long existujuca)
                {
                    _epizody[stareId] = existujuca;
                    if (!Existuje("SELECT 1 FROM pozretie WHERE epizoda_id = $e", ("$e", existujuca)))
                    {
                        Pozretia("epizoda_id", stareId, existujuca);
                    }

                    return;
                }
            }

            epizoda["titul_id"] = titulId;
            var noveId = Vloz("epizoda", epizoda, bezId: true);
            _epizody[stareId] = noveId;
            Identifikatory(2, "epizoda_id", stareId, noveId);
            Povod(2, stareId, noveId);
            Pozretia("epizoda_id", stareId, noveId);
        }

        private long Osoba(long stareId)
        {
            if (_osoby.TryGetValue(stareId, out var mapovane))
            {
                return mapovane;
            }

            var noveId = PodlaIdentifikatora(4, "osoba_id", stareId);
            if (noveId is null)
            {
                var osoba = Riadky("SELECT * FROM osoba WHERE id = $o", ("$o", stareId)).Single();
                osoba["fotka_id"] = null;
                noveId = Vloz("osoba", osoba, bezId: true);
                Identifikatory(4, "osoba_id", stareId, noveId.Value);
                Povod(4, stareId, noveId.Value);
            }

            _osoby[stareId] = noveId.Value;
            return noveId.Value;
        }

        private long Kolekcia(long stareId)
        {
            if (_kolekcie.TryGetValue(stareId, out var mapovane))
            {
                return mapovane;
            }

            var kolekcia = Riadky("SELECT * FROM kolekcia WHERE id = $k", ("$k", stareId)).Single();
            var noveId = PodlaIdentifikatora(3, "kolekcia_id", stareId);
            if (noveId is null)
            {
                using var prikaz = Kniznica.Prikaz(c, "SELECT id FROM kolekcia WHERE nazov = $n LIMIT 1");
                prikaz.Parameters.AddWithValue("$n", kolekcia["nazov"]);
                noveId = prikaz.ExecuteScalar() as long?;
            }

            if (noveId is null)
            {
                kolekcia["plagat_id"] = null;
                noveId = Vloz("kolekcia", kolekcia, bezId: true);
                Identifikatory(3, "kolekcia_id", stareId, noveId.Value);
                Povod(3, stareId, noveId.Value);
            }

            _kolekcie[stareId] = noveId.Value;
            return noveId.Value;
        }

        /// <summary>The object of the library that has one of the source object's identifiers (TMDB, HISTRA…).</summary>
        private long? PodlaIdentifikatora(int druh, string stlpec, long stareId)
        {
            foreach (var identifikator in Riadky($"SELECT zdroj, hodnota FROM identifikator WHERE druh = {druh} AND {stlpec} = $id", ("$id", stareId)))
            {
                using var prikaz = Kniznica.Prikaz(c, $"SELECT {stlpec} FROM identifikator WHERE zdroj = $z AND druh = {druh} AND hodnota = $h");
                prikaz.Parameters.AddWithValue("$z", identifikator["zdroj"]);
                prikaz.Parameters.AddWithValue("$h", identifikator["hodnota"]);
                if (prikaz.ExecuteScalar() is long id)
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>A title of the same kind with one of the source title's name keys ("1:nazov:1999"), when just one has it.</summary>
        private long? PodlaKluca(long stareId, long druh)
        {
            var kandidati = new HashSet<long>();
            foreach (var kluc in Riadky("SELECT kluc FROM titul_kluc WHERE titul_id = $t", ("$t", stareId)))
            {
                using var prikaz = Kniznica.Prikaz(c, "SELECT tk.titul_id FROM titul_kluc tk JOIN titul t ON t.id = tk.titul_id WHERE tk.kluc = $k AND t.druh = $d");
                prikaz.Parameters.AddWithValue("$k", kluc["kluc"]);
                prikaz.Parameters.AddWithValue("$d", druh);
                using var citac = prikaz.ExecuteReader();
                while (citac.Read())
                {
                    kandidati.Add(citac.GetInt64(0));
                }
            }

            return kandidati.Count == 1 ? kandidati.First() : null;
        }

        private void Identifikatory(int druh, string stlpec, long stareId, long noveId)
        {
            foreach (var identifikator in Riadky($"SELECT * FROM identifikator WHERE druh = {druh} AND {stlpec} = $id", ("$id", stareId)))
            {
                identifikator[stlpec] = noveId;
                VlozAleboNic("identifikator", identifikator);
            }
        }

        private void Povod(int druh, long stareId, long noveId)
        {
            foreach (var povod in Riadky($"SELECT * FROM povod_pola WHERE druh = {druh} AND objekt_id = $id", ("$id", stareId)))
            {
                povod["objekt_id"] = noveId;
                VlozAleboNic("povod_pola", povod);
            }
        }

        private void Pozretia(string stlpec, long stareId, long noveId)
        {
            foreach (var pozretie in Riadky($"SELECT * FROM pozretie WHERE {stlpec} = $id", ("$id", stareId)))
            {
                pozretie[stlpec] = noveId;
                Vloz("pozretie", pozretie, bezId: true);
            }
        }

        private void Stitky(long stareId, long noveId)
        {
            foreach (var stitok in Riadky("SELECT s.nazov, s.farba, s.kluc FROM titul_stitok ts JOIN stitok s ON s.id = ts.stitok_id WHERE ts.titul_id = $t", ("$t", stareId)))
            {
                Vykonaj("INSERT OR IGNORE INTO stitok(nazov, farba, kluc) VALUES ($n, $f, $k)", ("$n", stitok["nazov"]), ("$f", stitok["farba"]), ("$k", stitok["kluc"]));
                Vykonaj("INSERT OR IGNORE INTO titul_stitok(titul_id, stitok_id) SELECT $t, id FROM stitok WHERE nazov = $n", ("$t", noveId), ("$n", stitok["nazov"]));
            }
        }

        /// <summary>The versions of the imported files, tied to the library's titles and episodes.</summary>
        private void Verzie()
        {
            const string subory = "SELECT id FROM temp.importovane";
            foreach (var verzia in Riadky($"SELECT * FROM verzia v WHERE EXISTS (SELECT 1 FROM verzia_subor vs WHERE vs.verzia_id = v.id AND vs.subor_id IN ({subory})) ORDER BY id"))
            {
                zrusenie.ThrowIfCancellationRequested();
                var stareId = (long)verzia["id"]!;
                if (verzia["titul_id"] is long t)
                {
                    verzia["titul_id"] = _tituly[t];
                }
                else
                {
                    verzia["epizoda_id"] = _epizody[(long)verzia["epizoda_id"]!];
                }

                var noveId = Vloz("verzia", verzia, bezId: true);
                foreach (var clen in Riadky($"SELECT * FROM verzia_subor WHERE verzia_id = $v AND subor_id IN ({subory})", ("$v", stareId)))
                {
                    clen["verzia_id"] = noveId;
                    clen["subor_id"] = _subory[(long)clen["subor_id"]!];
                    Vloz("verzia_subor", clen, bezId: false);
                }

                foreach (var dalsia in Riadky("SELECT epizoda_id FROM verzia_dalsia_epizoda WHERE verzia_id = $v", ("$v", stareId)))
                {
                    if (_epizody.TryGetValue((long)dalsia["epizoda_id"]!, out var epizoda))
                    {
                        Vykonaj("INSERT OR IGNORE INTO verzia_dalsia_epizoda(verzia_id, epizoda_id) VALUES ($v, $e)", ("$v", noveId), ("$e", epizoda));
                    }
                }
            }
        }

        // ---------------------------------------------------------- reading and writing rows

        private List<Dictionary<string, object?>> Riadky(string sql, params (string Meno, object? Hodnota)[] parametre) =>
            Prud(sql, parametre).ToList();

        /// <summary>Rows read one by one (a disk's files are not all held in memory); nothing else may read meanwhile.</summary>
        private IEnumerable<Dictionary<string, object?>> Prud(string sql, params (string Meno, object? Hodnota)[] parametre)
        {
            using var prikaz = Kniznica.Prikaz(z, sql);
            foreach (var (meno, hodnota) in parametre)
            {
                prikaz.Parameters.AddWithValue(meno, hodnota ?? DBNull.Value);
            }

            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                var riadok = new Dictionary<string, object?>(citac.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < citac.FieldCount; i++)
                {
                    riadok[citac.GetName(i)] = citac.IsDBNull(i) ? null : citac.GetValue(i);
                }

                yield return riadok;
            }
        }

        private List<long> Stlpec(string sql)
        {
            using var prikaz = Kniznica.Prikaz(z, sql);
            using var citac = prikaz.ExecuteReader();
            var vysledok = new List<long>();
            while (citac.Read())
            {
                if (!citac.IsDBNull(0))
                {
                    vysledok.Add(citac.GetInt64(0));
                }
            }

            return vysledok;
        }

        /// <summary>Ids as a list for IN (…): numbers only, never text from a file.</summary>
        private static string Zoznam(IEnumerable<long> idcka)
        {
            var text = string.Join(',', idcka.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            return text.Length == 0 ? "NULL" : text;
        }

        /// <summary>Inserts a row into the library (without its id: the library gives a new one); returns the row's id.</summary>
        private long Vloz(string tabulka, Dictionary<string, object?> riadok, bool bezId)
        {
            var prikaz = Prikaz(tabulka, riadok, bezId, "INSERT");
            if (bezId)
            {
                return (long)prikaz.ExecuteScalar()!;
            }

            prikaz.ExecuteNonQuery();
            return riadok.TryGetValue("id", out var id) && id is long dane ? dane : 0;
        }

        private void VlozAleboNic(string tabulka, Dictionary<string, object?> riadok) =>
            Prikaz(tabulka, riadok, bezId: false, "INSERT OR IGNORE").ExecuteNonQuery();

        private SqliteCommand Prikaz(string tabulka, Dictionary<string, object?> riadok, bool bezId, string sloveso)
        {
            // only the library's own columns of the table: the file comes from elsewhere and its schema could say anything
            var stlpce = riadok.Keys.Where(k => !(bezId && k == "id") && StlpceTabulky(tabulka).Contains(k)).ToList();
            var kluc = sloveso + " " + tabulka + ":" + bezId + ":" + string.Join(',', stlpce);
            if (!_vkladanie.TryGetValue(kluc, out var prikaz))
            {
                // the column names come from the schema of the copy (the same as the library's), never from its data
                prikaz = Kniznica.Prikaz(c, $"{sloveso} INTO {tabulka}({string.Join(", ", stlpce)}) VALUES ({string.Join(", ", stlpce.Select((_, i) => "$p" + i.ToString(CultureInfo.InvariantCulture)))})" +
                    (bezId ? " RETURNING rowid" : ""));
                for (var i = 0; i < stlpce.Count; i++)
                {
                    prikaz.Parameters.Add(new SqliteParameter("$p" + i.ToString(CultureInfo.InvariantCulture), null));
                }

                _vkladanie[kluc] = prikaz;
            }

            for (var i = 0; i < stlpce.Count; i++)
            {
                prikaz.Parameters[i].Value = riadok[stlpce[i]] ?? DBNull.Value;
            }

            return prikaz;
        }

        private readonly Dictionary<string, HashSet<string>> _stlpce = [];

        private HashSet<string> StlpceTabulky(string tabulka)
        {
            if (!_stlpce.TryGetValue(tabulka, out var stlpce))
            {
                stlpce = new HashSet<string>(StringComparer.Ordinal);
                using var prikaz = Kniznica.Prikaz(c, "SELECT name FROM pragma_table_info($t)");
                prikaz.Parameters.AddWithValue("$t", tabulka);
                using var citac = prikaz.ExecuteReader();
                while (citac.Read())
                {
                    stlpce.Add(citac.GetString(0));
                }

                _stlpce[tabulka] = stlpce;
            }

            return stlpce;
        }

        private bool Existuje(string sql, params (string Meno, object? Hodnota)[] parametre)
        {
            using var prikaz = Kniznica.Prikaz(c, sql);
            foreach (var (meno, hodnota) in parametre)
            {
                prikaz.Parameters.AddWithValue(meno, hodnota ?? DBNull.Value);
            }

            return prikaz.ExecuteScalar() is not null;
        }

        private void Vykonaj(string sql, params (string Meno, object? Hodnota)[] parametre)
        {
            using var prikaz = Kniznica.Prikaz(c, sql);
            foreach (var (meno, hodnota) in parametre)
            {
                prikaz.Parameters.AddWithValue(meno, hodnota ?? DBNull.Value);
            }

            prikaz.ExecuteNonQuery();
        }
    }
}
