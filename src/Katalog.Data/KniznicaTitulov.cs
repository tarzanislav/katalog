using System.Globalization;
using System.Text;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>What part of the library of titles is shown.</summary>
public enum PohladKniznice
{
    /// <summary>Films the user has (a version with a file present).</summary>
    Filmy,

    /// <summary>Series the user has (an episode with a file present).</summary>
    Serialy,

    /// <summary>Titles with a version waiting for confirmation.</summary>
    NaPotvrdenie,

    /// <summary>The wish list.</summary>
    Zelane,

    /// <summary>Every title, also those without a version (collections, HISTRA lists).</summary>
    Vsetko,

    /// <summary>Titles with more present versions (a series: of an episode) – duplicates, stage E5.</summary>
    Duplicity,

    /// <summary>Titles that lie on one disk only (N1).</summary>
    Nezalohovane,
}

/// <summary>A title as the poster wall and the list show it.</summary>
/// <param name="Plagat">The poster's picture name (its SHA-256 in hex), null without a poster.</param>
/// <param name="Mam">A version with a file present on its disk.</param>
/// <param name="Verzie">Versions with a present file.</param>
/// <param name="Epizody">Episodes with a present file (a series).</param>
/// <param name="Kvalita">The best quality among the present files.</param>
public sealed record PolozkaKniznice(
    long Id, DruhTitulu Druh, string Nazov, string? Original, int? Rok, string? Plagat, double? Hodnotenie, int? MojeHodnotenie,
    bool Mam, int Verzie, int Epizody, bool Videne, bool NaPotvrdenie, bool Zelany, TriedaKvality Kvalita);

/// <summary>A file of a version, with its disk.</summary>
public sealed record SuborVerzie(
    long SuborId, string Nazov, long PriecinokId, long Velkost, bool Zmazany, long DiskId, string DiskCislo, string DiskNazov, TriedaKvality Kvalita);

/// <summary>A version of a film or an episode with its files in order.</summary>
public sealed record VerziaTitulu(long Id, long? EpizodaId, int? Seria, int? Cislo, bool Potvrdena, string? Poznamka, IReadOnlyList<SuborVerzie> Subory)
{
    public bool Pritomna => Subory.Any(s => !s.Zmazany);
}

/// <summary>An episode of a series and whether the user has and watched it.</summary>
public sealed record EpizodaTitulu(long Id, int Seria, int Cislo, string? Nazov, DateOnly? Vysielanie, string? Dej, bool Mam, bool Videne);

/// <summary>A person of a title.</summary>
public sealed record OsobaTitulu(long Id, string Meno, RolaOsoby Rola, string? Postava);

/// <summary>A collection with how many of its parts there are and how many the user has.</summary>
public sealed record KolekciaKniznice(long Id, string Nazov, string? Dej, string? Plagat, int Diely, int Mam);

/// <summary>A HISTRA list with how many of its titles the user has.</summary>
public sealed record ZoznamHistraKniznice(long Id, string HistraId, string Nazov, long? Synchronizovany, int Polozky, int Mam);

/// <summary>Where a field of a title came from (A5).</summary>
public sealed record PovodPola(ZdrojUdajov Zdroj, long Cas, bool Zamknute);

/// <summary>Everything the detail of a title shows.</summary>
public sealed record DetailTitulu(
    PolozkaKniznice Polozka,
    string? Dej,
    int? DlzkaMinut,
    string? MojaPoznamka,
    IReadOnlyList<string> Zanre,
    IReadOnlyList<OsobaTitulu> Osoby,
    IReadOnlyList<VerziaTitulu> Verzie,
    IReadOnlyList<EpizodaTitulu> Epizody,
    IReadOnlyList<KolekciaKniznice> Kolekcie,
    IReadOnlyList<string> ZoznamyHistra,
    IReadOnlyList<(ZdrojUdajov Zdroj, string Hodnota)> Identifikatory,
    IReadOnlyDictionary<string, PovodPola> Povod,
    IReadOnlyList<Kandidat> Kandidati,
    long? NaposledyVidene)
{
    /// <summary>The best present version of the film, or of each episode, when there are more (duplicates, E5).</summary>
    public IReadOnlySet<long> NajlepsieVerzie { get; init; } = new HashSet<long>();

    /// <summary>What is known about each version for comparing them.</summary>
    public IReadOnlyList<VlastnostiVerzie> Vlastnosti { get; init; } = [];
}

/// <summary>
/// Reading the library of titles for the window (stage E4): the poster wall of a view filtered by the one query language
/// (A4), the detail of a title, collections and HISTRA lists.
/// </summary>
public static class KniznicaTitulov
{
    private const string Aggregaty = """
        WITH vt AS (
          SELECT v.titul_id AS titul_id, v.id AS verzia_id, NULL AS epizoda_id, v.potvrdena FROM verzia v WHERE v.titul_id IS NOT NULL
          UNION ALL
          SELECT e.titul_id, v.id, e.id, v.potvrdena FROM verzia v JOIN epizoda e ON e.id = v.epizoda_id
        ),
        pritomne AS (
          SELECT vt.titul_id, count(DISTINCT vt.verzia_id) AS verzie, count(DISTINCT vt.epizoda_id) AS epizody,
                 max(coalesce(m.trieda_kvality, 0)) AS kvalita, min(vt.potvrdena) AS potvrdene
          FROM vt JOIN verzia_subor vs ON vs.verzia_id = vt.verzia_id JOIN subor s ON s.id = vs.subor_id AND s.stav = 0
          LEFT JOIN medialne_udaje m ON m.subor_id = s.id
          GROUP BY vt.titul_id
        ),
        videne AS (
          SELECT titul_id FROM pozretie WHERE titul_id IS NOT NULL
          UNION SELECT e.titul_id FROM pozretie p JOIN epizoda e ON e.id = p.epizoda_id
        )
        """;

    private const string Stlpce = """
        SELECT t.id, t.druh, t.nazov, t.original, t.rok, nullif(lower(hex(o.odtlacok)), ''), t.hodnotenie_zdroja, t.moje_hodnotenie,
               p.titul_id IS NOT NULL, coalesce(p.verzie, 0), coalesce(p.epizody, 0), t.id IN (SELECT titul_id FROM videne),
               coalesce(p.potvrdene, 1) = 0, t.zelany, coalesce(p.kvalita, 0)
        FROM titul t LEFT JOIN pritomne p ON p.titul_id = t.id LEFT JOIN obrazok o ON o.id = t.plagat_id
        """;

    /// <summary>
    /// The titles of a view that match a query, by name (Slovak order). The view's own condition ("has a file") gives way
    /// when the query speaks of it (mam:nie, zelane:ano).
    /// </summary>
    /// <param name="kolekciaId">Only the parts of this collection (all of them, in their order).</param>
    /// <param name="zoznamHistra">Only the titles of this HISTRA list.</param>
    /// <param name="stitokId">Only the titles with this tag of the user.</param>
    public static IReadOnlyList<PolozkaKniznice> Najdi(
        SqliteConnection spojenie, Dotaz dotaz, KontextHladania kontext, PohladKniznice pohlad, long? kolekciaId = null, long? zoznamHistra = null,
        long? stitokId = null)
    {
        using var prikaz = spojenie.CreateCommand();
        var cislo = 0;
        string Parameter(object hodnota)
        {
            var meno = "$p" + (++cislo).ToString(CultureInfo.InvariantCulture);
            prikaz.Parameters.AddWithValue(meno, hodnota);
            return meno;
        }

        var podmienky = new List<string>();
        var hovoriOMam = dotaz.Podmienky.Any(p => p is Mam or Zelane or Kolekcie) || kolekciaId is not null || zoznamHistra is not null;
        switch (pohlad)
        {
            case PohladKniznice.Filmy:
                podmienky.Add("t.druh = 1");
                break;
            case PohladKniznice.Serialy:
                podmienky.Add("t.druh = 2");
                break;
            case PohladKniznice.NaPotvrdenie:
                podmienky.Add("coalesce(p.potvrdene, 1) = 0");
                break;
            case PohladKniznice.Zelane:
                podmienky.Add("t.zelany = 1");
                hovoriOMam = true;
                break;
            case PohladKniznice.Duplicity:
                podmienky.Add(PodmienkyKniznice.PreTitul(new PocetVerzii(2, null, false), "t", Parameter));
                break;
            case PohladKniznice.Nezalohovane:
                podmienky.Add(PodmienkyKniznice.PreTitul(new Zalohovane(false), "t", Parameter));
                break;
        }

        if (pohlad is PohladKniznice.Filmy or PohladKniznice.Serialy && !hovoriOMam)
        {
            podmienky.Add("p.titul_id IS NOT NULL");
        }

        if (stitokId is { } st)
        {
            podmienky.Add($"t.id IN (SELECT titul_id FROM titul_stitok WHERE stitok_id = {Parameter(st)})");
        }

        if (kolekciaId is { } k)
        {
            podmienky.Add($"t.id IN (SELECT titul_id FROM kolekcia_titul WHERE kolekcia_id = {Parameter(k)})");
        }

        if (zoznamHistra is { } z)
        {
            podmienky.Add($"t.id IN (SELECT titul_id FROM histra_polozka WHERE zoznam_id = {Parameter(z)})");
        }

        // words: names of titles (also the original) in the full-text index
        foreach (var slovo in dotaz.Podmienky.OfType<Slovo>())
        {
            string sql;
            if (slovo.Text.Length >= Fulltext.NajkratsiText)
            {
                sql = $"t.id IN (SELECT rowid >> 3 FROM hladanie WHERE hladanie MATCH {Parameter(Fulltext.Fraza(slovo.Text))} AND (rowid & 7) = {(int)DruhHladania.Titul})";
            }
            else
            {
                var vzor = Parameter("%" + slovo.Text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%");
                sql = $"(t.nazov LIKE {vzor} ESCAPE '\\' OR t.original LIKE {vzor} ESCAPE '\\')";
            }

            podmienky.Add(slovo.Zaporna ? "NOT " + sql : sql);
        }

        // conditions of titles
        foreach (var titulova in dotaz.Podmienky.OfType<TitulovaPodmienka>())
        {
            var sql = PodmienkyKniznice.PreTitul(titulova, "t", Parameter);
            podmienky.Add(titulova.Zaporna ? "NOT (" + sql + ")" : sql);
        }

        // conditions of files (quality, sound, disk…): a present file of one of the title's versions has them all
        var suborove = dotaz.Podmienky
            .Where(p => p is not (Slovo or TitulovaPodmienka or Zmazane))
            .Select(p => Hladanie.PodmienkaPolozky(p, priecinky: false, kontext, Parameter))
            .OfType<string>()
            .ToList();
        if (suborove.Count > 0)
        {
            podmienky.Add($"""
                t.id IN (SELECT vt.titul_id FROM vt JOIN verzia_subor vs ON vs.verzia_id = vt.verzia_id JOIN subor s ON s.id = vs.subor_id
                         WHERE s.stav = 0 AND {string.Join(" AND ", suborove)})
                """);
        }

        var sqlText = new StringBuilder(Aggregaty).Append('\n').Append(Stlpce);
        if (podmienky.Count > 0)
        {
            sqlText.Append(" WHERE ").Append(string.Join(" AND ", podmienky));
        }

        prikaz.CommandText = sqlText.ToString();
        var vysledok = Citaj(prikaz);
        if (kolekciaId is { } kol)
        {
            // a collection in its own order (part 1, 2, 3), not by name
            var poradie = PoradieKolekcie(spojenie, kol);
            return vysledok.OrderBy(t => poradie.TryGetValue(t.Id, out var i) ? i : int.MaxValue).ThenBy(t => t.Rok).ToList();
        }

        var porovnanie = StringComparer.Create(CultureInfo.GetCultureInfo("sk-SK"), CompareOptions.IgnoreCase);
        return vysledok.OrderBy(t => t.Nazov, porovnanie).ThenBy(t => t.Rok).ToList();
    }

    /// <summary>Everything about one title for its detail; null when it is gone.</summary>
    /// <param name="poradie">The order of criteria for the best version (default: <see cref="PorovnanieVerzii.Predvolene"/>).</param>
    public static DetailTitulu? Detail(SqliteConnection spojenie, long titulId, IReadOnlyList<KriteriumVerzie>? poradie = null)
    {
        PolozkaKniznice polozka;
        string? dej, poznamka;
        int? dlzka;
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = Aggregaty + "\n" + Stlpce + " WHERE t.id = $id";
            prikaz.Parameters.AddWithValue("$id", titulId);
            if (Citaj(prikaz).FirstOrDefault() is not { } p)
            {
                return null;
            }

            polozka = p;
        }

        using (var prikaz = Kniznica.Prikaz(spojenie, "SELECT dej, dlzka_min, moja_poznamka FROM titul WHERE id = $id"))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            c.Read();
            dej = c.IsDBNull(0) ? null : c.GetString(0);
            dlzka = c.IsDBNull(1) ? null : c.GetInt32(1);
            poznamka = c.IsDBNull(2) ? null : c.GetString(2);
        }

        var zanre = Retazce(spojenie, "SELECT z.nazov FROM titul_zaner tz JOIN zaner z ON z.id = tz.zaner_id WHERE tz.titul_id = $id ORDER BY z.nazov", titulId);
        var osoby = new List<OsobaTitulu>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   SELECT o.id, o.meno, tos.rola, tos.postava FROM titul_osoba tos JOIN osoba o ON o.id = tos.osoba_id
                   WHERE tos.titul_id = $id ORDER BY tos.rola, tos.poradie
                   """))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                osoby.Add(new OsobaTitulu(c.GetInt64(0), c.GetString(1), (RolaOsoby)c.GetInt32(2), c.IsDBNull(3) ? null : c.GetString(3)));
            }
        }

        var verzie = Verzie(spojenie, titulId);
        var epizody = new List<EpizodaTitulu>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   SELECT e.id, e.seria, e.cislo, e.nazov, e.vysielanie, e.dej,
                          EXISTS (SELECT 1 FROM verzia v JOIN verzia_subor vs ON vs.verzia_id = v.id JOIN subor s ON s.id = vs.subor_id
                                  WHERE v.epizoda_id = e.id AND s.stav = 0)
                          OR EXISTS (SELECT 1 FROM verzia_dalsia_epizoda d JOIN verzia_subor vs ON vs.verzia_id = d.verzia_id
                                     JOIN subor s ON s.id = vs.subor_id WHERE d.epizoda_id = e.id AND s.stav = 0),
                          EXISTS (SELECT 1 FROM pozretie p WHERE p.epizoda_id = e.id)
                   FROM epizoda e WHERE e.titul_id = $id ORDER BY e.seria = 0, e.seria, e.cislo
                   """))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                epizody.Add(new EpizodaTitulu(c.GetInt64(0), c.GetInt32(1), c.GetInt32(2), c.IsDBNull(3) ? null : c.GetString(3),
                    c.IsDBNull(4) ? null : DateOnly.ParseExact(c.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    c.IsDBNull(5) ? null : c.GetString(5), c.GetInt32(6) == 1, c.GetInt32(7) == 1));
            }
        }

        var kolekcie = ZoznamKolekcii(spojenie, titulId);
        var zoznamy = Retazce(spojenie, "SELECT z.nazov FROM histra_polozka p JOIN histra_zoznam z ON z.id = p.zoznam_id WHERE p.titul_id = $id ORDER BY z.nazov", titulId);
        var identifikatory = new List<(ZdrojUdajov, string)>();
        using (var prikaz = Kniznica.Prikaz(spojenie, "SELECT zdroj, hodnota FROM identifikator WHERE titul_id = $id ORDER BY zdroj"))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                identifikatory.Add(((ZdrojUdajov)c.GetInt32(0), c.GetString(1)));
            }
        }

        var povod = new Dictionary<string, PovodPola>(StringComparer.Ordinal);
        using (var prikaz = Kniznica.Prikaz(spojenie, "SELECT pole, zdroj, cas, zamknute FROM povod_pola WHERE druh = 1 AND objekt_id = $id"))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                povod[c.GetString(0)] = new PovodPola((ZdrojUdajov)c.GetInt32(1), c.GetInt64(2), c.GetInt32(3) == 1);
            }
        }

        long? videne;
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   SELECT max(kedy) FROM pozretie WHERE titul_id = $id OR epizoda_id IN (SELECT id FROM epizoda WHERE titul_id = $id)
                   """))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            videne = prikaz.ExecuteScalar() is long kedy ? kedy : null;
        }

        var vlastnosti = VlastnostiVerzii(spojenie, titulId);
        var najlepsie = verzie
            .GroupBy(v => v.EpizodaId ?? 0)
            .Select(g => PorovnanieVerzii.Najlepsia(vlastnosti.Where(x => g.Any(v => v.Id == x.VerziaId)), poradie ?? PorovnanieVerzii.Predvolene))
            .OfType<long>()
            .ToHashSet();
        return new DetailTitulu(polozka, dej, dlzka, poznamka, zanre, osoby, verzie, epizody, kolekcie, zoznamy, identifikatory, povod,
            Tituly.Kandidati(spojenie, titulId), videne)
        {
            NajlepsieVerzie = najlepsie,
            Vlastnosti = vlastnosti,
        };
    }

    /// <summary>
    /// The versions of a title as their present files make them: the best quality class and picture height, the codec of
    /// that picture, the languages of sound and subtitles, the size and HDR.
    /// </summary>
    public static IReadOnlyList<VlastnostiVerzie> VlastnostiVerzii(SqliteConnection spojenie, long titulId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            WITH vt AS ({PodmienkyKniznice.VerzieTitulu("$t")})
            SELECT vt.id, max(coalesce(m.trieda_kvality, 0)), max(m.vyska),
                   (SELECT m2.video_kodek FROM verzia_subor vs2 JOIN medialne_udaje m2 ON m2.subor_id = vs2.subor_id JOIN subor s2 ON s2.id = vs2.subor_id
                    WHERE vs2.verzia_id = vt.id AND s2.stav = 0 ORDER BY m2.vyska DESC LIMIT 1),
                   (SELECT group_concat(DISTINCT st.jazyk) FROM verzia_subor vs3 JOIN subor s3 ON s3.id = vs3.subor_id JOIN stopa st ON st.subor_id = s3.id
                    WHERE vs3.verzia_id = vt.id AND s3.stav = 0 AND st.druh = 2 AND st.jazyk IS NOT NULL),
                   (SELECT group_concat(DISTINCT st.jazyk) FROM verzia_subor vs3 JOIN subor s3 ON s3.id = vs3.subor_id JOIN stopa st ON st.subor_id = s3.id
                    WHERE vs3.verzia_id = vt.id AND s3.stav = 0 AND st.druh = 3 AND st.jazyk IS NOT NULL),
                   coalesce(sum(s.velkost) FILTER (WHERE s.stav = 0), 0), max(coalesce(m.hdr, 0)) > 0, count(*) FILTER (WHERE s.stav = 0) > 0
            FROM vt JOIN verzia_subor vs ON vs.verzia_id = vt.id JOIN subor s ON s.id = vs.subor_id
            LEFT JOIN medialne_udaje m ON m.subor_id = s.id AND s.stav = 0
            GROUP BY vt.id
            """);
        prikaz.Parameters.AddWithValue("$t", titulId);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<VlastnostiVerzie>();
        while (c.Read())
        {
            IReadOnlyCollection<string> Jazyky(int i) => c.IsDBNull(i) ? [] : c.GetString(i).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            vysledok.Add(new VlastnostiVerzie(c.GetInt64(0), (TriedaKvality)c.GetInt32(1), c.IsDBNull(2) ? null : c.GetInt32(2), c.IsDBNull(3) ? null : c.GetString(3),
                Jazyky(4), Jazyky(5), c.GetInt64(6), c.GetInt32(7) == 1, c.GetInt32(8) == 1));
        }

        return vysledok;
    }

    /// <summary>The versions of a title (a film's own, a series' by episode), present ones first.</summary>
    public static IReadOnlyList<VerziaTitulu> Verzie(SqliteConnection spojenie, long titulId)
    {
        var verzie = new List<(long Id, long? Epizoda, int? Seria, int? Cislo, bool Potvrdena, string? Poznamka)>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   SELECT v.id, NULL, NULL, NULL, v.potvrdena, v.poznamka FROM verzia v WHERE v.titul_id = $id
                   UNION ALL
                   SELECT v.id, e.id, e.seria, e.cislo, v.potvrdena, v.poznamka FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = $id
                   ORDER BY 3, 4, 1
                   """))
        {
            prikaz.Parameters.AddWithValue("$id", titulId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                verzie.Add((c.GetInt64(0), c.IsDBNull(1) ? null : c.GetInt64(1), c.IsDBNull(2) ? null : c.GetInt32(2),
                    c.IsDBNull(3) ? null : c.GetInt32(3), c.GetInt32(4) == 1, c.IsDBNull(5) ? null : c.GetString(5)));
            }
        }

        var vysledok = new List<VerziaTitulu>(verzie.Count);
        using var subory = Kniznica.Prikaz(spojenie, """
            SELECT s.id, s.nazov, s.priecinok_id, s.velkost, s.stav, d.id, d.cislo, d.nazov, coalesce(m.trieda_kvality, 0)
            FROM verzia_subor vs JOIN subor s ON s.id = vs.subor_id JOIN disk d ON d.id = s.disk_id
            LEFT JOIN medialne_udaje m ON m.subor_id = s.id
            WHERE vs.verzia_id = $v ORDER BY vs.poradie, s.nazov
            """);
        var parameter = subory.Parameters.Add("$v", SqliteType.Integer);
        foreach (var v in verzie)
        {
            parameter.Value = v.Id;
            var zoznam = new List<SuborVerzie>();
            using (var c = subory.ExecuteReader())
            {
                while (c.Read())
                {
                    zoznam.Add(new SuborVerzie(c.GetInt64(0), c.GetString(1), c.GetInt64(2), c.GetInt64(3), c.GetInt32(4) == 1, c.GetInt64(5),
                        "D" + c.GetInt32(6).ToString("000", CultureInfo.InvariantCulture), c.GetString(7), (TriedaKvality)c.GetInt32(8)));
                }
            }

            vysledok.Add(new VerziaTitulu(v.Id, v.Epizoda, v.Seria, v.Cislo, v.Potvrdena, v.Poznamka, zoznam));
        }

        return vysledok.OrderBy(v => v.Pritomna ? 0 : 1).ThenBy(v => v.Seria).ThenBy(v => v.Cislo).ToList();
    }

    /// <summary>All collections (or those of one title) with their counts, by name.</summary>
    public static IReadOnlyList<KolekciaKniznice> ZoznamKolekcii(SqliteConnection spojenie, long? titulId = null)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT k.id, k.nazov, k.dej, nullif(lower(hex(o.odtlacok)), ''), count(kt.titul_id),
                   count(kt.titul_id) FILTER (WHERE {PodmienkyKniznice.MaPritomnu("kt.titul_id")})
            FROM kolekcia k LEFT JOIN kolekcia_titul kt ON kt.kolekcia_id = k.id LEFT JOIN obrazok o ON o.id = k.plagat_id
            {(titulId is null ? "" : "WHERE k.id IN (SELECT kolekcia_id FROM kolekcia_titul WHERE titul_id = $t)")}
            GROUP BY k.id
            """);
        if (titulId is { } t)
        {
            prikaz.Parameters.AddWithValue("$t", t);
        }

        using var c = prikaz.ExecuteReader();
        var vysledok = new List<KolekciaKniznice>();
        while (c.Read())
        {
            vysledok.Add(new KolekciaKniznice(c.GetInt64(0), c.GetString(1), c.IsDBNull(2) ? null : c.GetString(2),
                c.IsDBNull(3) ? null : c.GetString(3), c.GetInt32(4), c.GetInt32(5)));
        }

        var porovnanie = StringComparer.Create(CultureInfo.GetCultureInfo("sk-SK"), CompareOptions.IgnoreCase);
        return vysledok.OrderBy(k => k.Nazov, porovnanie).ToList();
    }

    /// <summary>
    /// The disks with present files of each title (its own versions or its episodes'), by number ("D003") – for labels
    /// and exports.
    /// </summary>
    public static IReadOnlyDictionary<long, IReadOnlyList<string>> DiskyTitulov(SqliteConnection spojenie, IEnumerable<long> tituly)
    {
        var vysledok = new Dictionary<long, IReadOnlyList<string>>();
        foreach (var davka in tituly.Distinct().Chunk(500))
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = $"""
                SELECT DISTINCT coalesce(v.titul_id, e.titul_id), d.cislo
                FROM verzia v LEFT JOIN epizoda e ON e.id = v.epizoda_id
                JOIN verzia_subor vs ON vs.verzia_id = v.id JOIN subor s ON s.id = vs.subor_id JOIN disk d ON d.id = s.disk_id
                WHERE s.stav = 0 AND coalesce(v.titul_id, e.titul_id) IN ({string.Join(',', davka.Select(i => i.ToString(CultureInfo.InvariantCulture)))})
                ORDER BY 1, 2
                """;
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                var titul = citac.GetInt64(0);
                var cislo = "D" + citac.GetInt64(1).ToString("000", CultureInfo.InvariantCulture);
                if (vysledok.TryGetValue(titul, out var disky))
                {
                    ((List<string>)disky).Add(cislo);
                }
                else
                {
                    vysledok[titul] = new List<string> { cislo };
                }
            }
        }

        return vysledok;
    }

    /// <summary>The HISTRA lists in the library with how many of their titles the user has.</summary>
    public static IReadOnlyList<ZoznamHistraKniznice> ZoznamyHistra(SqliteConnection spojenie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, $"""
            SELECT z.id, z.histra_id, z.nazov, z.synchronizovany, count(p.histra_titul),
                   count(p.histra_titul) FILTER (WHERE p.titul_id IS NOT NULL AND {PodmienkyKniznice.MaPritomnu("p.titul_id")})
            FROM histra_zoznam z LEFT JOIN histra_polozka p ON p.zoznam_id = z.id
            GROUP BY z.id
            """);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<ZoznamHistraKniznice>();
        while (c.Read())
        {
            vysledok.Add(new ZoznamHistraKniznice(c.GetInt64(0), c.GetString(1), c.GetString(2), c.IsDBNull(3) ? null : c.GetInt64(3),
                c.GetInt32(4), c.GetInt32(5)));
        }

        var porovnanie = StringComparer.Create(CultureInfo.GetCultureInfo("sk-SK"), CompareOptions.IgnoreCase);
        return vysledok.OrderBy(z => z.Nazov, porovnanie).ToList();
    }

    /// <summary>How many titles have duplicates and how many lie on one disk only (the counts next to the views).</summary>
    public static (int Duplicity, int Nezalohovane) PoctyZbierok(SqliteConnection spojenie)
    {
        using var prikaz = spojenie.CreateCommand();
        var cislo = 0;
        string Parameter(object hodnota)
        {
            var meno = "$p" + (++cislo).ToString(CultureInfo.InvariantCulture);
            prikaz.Parameters.AddWithValue(meno, hodnota);
            return meno;
        }

        prikaz.CommandText = $"""
            SELECT count(*) FILTER (WHERE {PodmienkyKniznice.PreTitul(new PocetVerzii(2, null, false), "t", Parameter)}),
                   count(*) FILTER (WHERE {PodmienkyKniznice.PreTitul(new Zalohovane(false), "t", Parameter)})
            FROM titul t
            """;
        using var c = prikaz.ExecuteReader();
        c.Read();
        return (c.GetInt32(0), c.GetInt32(1));
    }

    /// <summary>How many titles each view has (for the counts next to the views).</summary>
    public static (int Filmy, int Serialy, int NaPotvrdenie, int Zelane) Pocty(SqliteConnection spojenie)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, Aggregaty + """

            SELECT count(*) FILTER (WHERE t.druh = 1 AND p.titul_id IS NOT NULL),
                   count(*) FILTER (WHERE t.druh = 2 AND p.titul_id IS NOT NULL),
                   count(*) FILTER (WHERE coalesce(p.potvrdene, 1) = 0),
                   count(*) FILTER (WHERE t.zelany = 1)
            FROM titul t LEFT JOIN pritomne p ON p.titul_id = t.id
            """);
        using var c = prikaz.ExecuteReader();
        c.Read();
        return (c.GetInt32(0), c.GetInt32(1), c.GetInt32(2), c.GetInt32(3));
    }

    /// <summary>
    /// What playing a version plays: its files in order, or for a Blu-ray or DVD structure the folder that holds BDMV or
    /// VIDEO_TS (players open it as a disc). Subor is null for a folder.
    /// </summary>
    public static IReadOnlyList<(long DiskId, long PriecinokId, string? Subor, string Pripona)> CielVerzie(SqliteConnection spojenie, long verziaId)
    {
        var subory = new List<(long DiskId, long PriecinokId, string Subor, string Pripona)>();
        using (var prikaz = Kniznica.Prikaz(spojenie, """
                   SELECT s.disk_id, s.priecinok_id, s.nazov, s.pripona FROM verzia_subor vs JOIN subor s ON s.id = vs.subor_id
                   WHERE vs.verzia_id = $v AND s.stav = 0 ORDER BY vs.poradie, s.nazov
                   """))
        {
            prikaz.Parameters.AddWithValue("$v", verziaId);
            using var c = prikaz.ExecuteReader();
            while (c.Read())
            {
                subory.Add((c.GetInt64(0), c.GetInt64(1), c.GetString(2), c.GetString(3)));
            }
        }

        if (subory.Count == 0)
        {
            return [];
        }

        using var rodic = Kniznica.Prikaz(spojenie, "SELECT rodic_id, nazov FROM priecinok WHERE id = $id");
        var id = rodic.Parameters.Add("$id", SqliteType.Integer);
        for (long? priecinok = subory[0].PriecinokId; priecinok is { } p; )
        {
            id.Value = p;
            using var c = rodic.ExecuteReader();
            if (!c.Read())
            {
                break;
            }

            long? vyssi = c.IsDBNull(0) ? null : c.GetInt64(0);
            var nazov = c.GetString(1);
            if (nazov.Equals("BDMV", StringComparison.OrdinalIgnoreCase) || nazov.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
            {
                return [(subory[0].DiskId, vyssi ?? p, null, "")];
            }

            priecinok = vyssi;
        }

        return subory.Select(s => (s.DiskId, s.PriecinokId, (string?)s.Subor, s.Pripona)).ToList();
    }

    /// <summary>The title a file belongs to (through its version), for jumping from a file to its title.</summary>
    public static long? TitulSuboru(SqliteConnection spojenie, long suborId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT coalesce(v.titul_id, e.titul_id) FROM verzia_subor vs JOIN verzia v ON v.id = vs.verzia_id
            LEFT JOIN epizoda e ON e.id = v.epizoda_id WHERE vs.subor_id = $s
            """);
        prikaz.Parameters.AddWithValue("$s", suborId);
        return prikaz.ExecuteScalar() is long id ? id : null;
    }

    /// <summary>"Názov (rok)" of the title a file belongs to, with S01E02 for an episode; null for a file of no version.</summary>
    public static string? PopisTituluSuboru(SqliteConnection spojenie, long suborId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, """
            SELECT t.nazov, t.rok, e.seria, e.cislo, v.potvrdena FROM verzia_subor vs JOIN verzia v ON v.id = vs.verzia_id
            LEFT JOIN epizoda e ON e.id = v.epizoda_id JOIN titul t ON t.id = coalesce(v.titul_id, e.titul_id)
            WHERE vs.subor_id = $s
            """);
        prikaz.Parameters.AddWithValue("$s", suborId);
        using var c = prikaz.ExecuteReader();
        if (!c.Read())
        {
            return null;
        }

        var text = c.GetString(0) + (c.IsDBNull(1) ? "" : $" ({c.GetInt32(1).ToString(CultureInfo.InvariantCulture)})");
        if (!c.IsDBNull(2))
        {
            text += $" · S{c.GetInt32(2):00}E{c.GetInt32(3):00}";
        }

        return c.GetInt32(4) == 1 ? text : text + " · čaká na potvrdenie";
    }

    private static Dictionary<long, int> PoradieKolekcie(SqliteConnection spojenie, long kolekciaId)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, "SELECT titul_id, poradie FROM kolekcia_titul WHERE kolekcia_id = $k");
        prikaz.Parameters.AddWithValue("$k", kolekciaId);
        using var c = prikaz.ExecuteReader();
        var vysledok = new Dictionary<long, int>();
        while (c.Read())
        {
            vysledok[c.GetInt64(0)] = c.GetInt32(1);
        }

        return vysledok;
    }

    private static List<PolozkaKniznice> Citaj(SqliteCommand prikaz)
    {
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<PolozkaKniznice>();
        while (c.Read())
        {
            vysledok.Add(new PolozkaKniznice(
                c.GetInt64(0), (DruhTitulu)c.GetInt32(1), c.GetString(2), c.IsDBNull(3) ? null : c.GetString(3), c.IsDBNull(4) ? null : c.GetInt32(4),
                c.IsDBNull(5) || c.GetString(5).Length == 0 ? null : c.GetString(5), c.IsDBNull(6) ? null : c.GetDouble(6), c.IsDBNull(7) ? null : c.GetInt32(7),
                c.GetInt32(8) == 1, c.GetInt32(9), c.GetInt32(10), c.GetInt32(11) == 1, c.GetInt32(12) == 1, c.GetInt32(13) == 1,
                (TriedaKvality)c.GetInt32(14)));
        }

        return vysledok;
    }

    private static List<string> Retazce(SqliteConnection spojenie, string sql, long id)
    {
        using var prikaz = Kniznica.Prikaz(spojenie, sql);
        prikaz.Parameters.AddWithValue("$id", id);
        using var c = prikaz.ExecuteReader();
        var vysledok = new List<string>();
        while (c.Read())
        {
            vysledok.Add(c.GetString(0));
        }

        return vysledok;
    }
}
