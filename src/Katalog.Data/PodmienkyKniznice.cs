using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;

namespace Katalog.Data;

/// <summary>
/// The conditions of the query language on the library of titles (stage E4) as SQL: on a title (alias given), or on a
/// file through its version. Values always go as parameters.
/// </summary>
internal static class PodmienkyKniznice
{
    /// <summary>The versions of title <paramref name="t"/>: its own (a film) and those of its episodes (a series).</summary>
    public static string VerzieTitulu(string t) =>
        $"SELECT v.id, v.potvrdena FROM verzia v WHERE v.titul_id = {t} " +
        $"UNION ALL SELECT v.id, v.potvrdena FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = {t}";

    /// <summary>The title has a version with a file present on its disk.</summary>
    public static string MaPritomnu(string t) =>
        $"EXISTS (SELECT 1 FROM ({VerzieTitulu(t)}) vt JOIN verzia_subor vs ON vs.verzia_id = vt.id " +
        "JOIN subor sp ON sp.id = vs.subor_id WHERE sp.stav = 0)";

    /// <summary>A condition on title <paramref name="t"/>.</summary>
    public static string PreTitul(TitulovaPodmienka podmienka, string t, Func<object, string> parameter) => podmienka switch
    {
        Roky r => Rozsah($"{t}.rok", r.Od, r.Do, parameter),
        Zanre z => $"{t}.id IN (SELECT tz.titul_id FROM titul_zaner tz JOIN zaner z ON z.id = tz.zaner_id WHERE " +
                   string.Join(" OR ", z.Nazvy.Select(n => $"z.kluc LIKE {parameter("%" + Escape(n) + "%")} ESCAPE '\\'")) + ")",
        Videne v => (v.Hodnota ? "" : "NOT ") +
                    $"(EXISTS (SELECT 1 FROM pozretie p WHERE p.titul_id = {t}.id) " +
                    $"OR EXISTS (SELECT 1 FROM epizoda e JOIN pozretie p ON p.epizoda_id = e.id WHERE e.titul_id = {t}.id))",
        Hodnotenia h => Rozsah(h.Moje ? $"{t}.moje_hodnotenie" : $"{t}.hodnotenie_zdroja", h.Od, h.Do, parameter),
        Osoby o => $"{t}.id IN (SELECT tos.titul_id FROM titul_osoba tos WHERE tos.rola = {(int)o.Rola} AND tos.osoba_id IN " +
                   $"(SELECT rowid >> 3 FROM hladanie WHERE hladanie MATCH {parameter(string.Join(" OR ", o.Mena.Select(Fulltext.Fraza)))} " +
                   $"AND (rowid & 7) = {(int)DruhHladania.Osoba}))",
        Kolekcie { Nazvy.Count: 0 } => $"{t}.id IN (SELECT titul_id FROM kolekcia_titul)",
        Kolekcie k => $"{t}.id IN (SELECT kt.titul_id FROM kolekcia_titul kt WHERE kt.kolekcia_id IN " +
                      $"(SELECT rowid >> 3 FROM hladanie WHERE hladanie MATCH {parameter(string.Join(" OR ", k.Nazvy.Select(Fulltext.Fraza)))} " +
                      $"AND (rowid & 7) = {(int)DruhHladania.Kolekcia}))",
        VKniznici { Stav: StavVKniznici.Film } => $"{t}.druh = 1",
        VKniznici { Stav: StavVKniznici.Serial } => $"{t}.druh = 2",
        VKniznici { Stav: StavVKniznici.VKniznici } => $"EXISTS ({VerzieTitulu(t + ".id")})",
        VKniznici { Stav: StavVKniznici.NaPotvrdenie } => $"EXISTS (SELECT 1 FROM ({VerzieTitulu(t + ".id")}) vt WHERE vt.potvrdena = 0)",
        VKniznici => "0",
        Mam m => (m.Hodnota ? "" : "NOT ") + MaPritomnu(t + ".id"),
        Zelane z => z.Hodnota ? $"{t}.zelany = 1" : $"{t}.zelany = 0",
        PocetVerzii p => RozsahCisla($"max(coalesce({PocetFilmu(t)}, 0), coalesce({PocetEpizody(t)}, 0))", p.Od, p.Do, parameter),
        Zalohovane z => $"{MaPritomnu(t + ".id")} AND " + (z.Hodnota ? "" : "NOT ") + $"""
            (CASE {t}.druh WHEN 1 THEN (SELECT count(DISTINCT sp.disk_id) FROM verzia v JOIN verzia_subor vs ON vs.verzia_id = v.id
                                         JOIN subor sp ON sp.id = vs.subor_id WHERE v.titul_id = {t}.id AND sp.stav = 0) >= 2
             ELSE NOT EXISTS (SELECT 1 FROM epizoda e WHERE e.titul_id = {t}.id
                              AND (SELECT count(DISTINCT sp.disk_id) FROM verzia v JOIN verzia_subor vs ON vs.verzia_id = v.id
                                   JOIN subor sp ON sp.id = vs.subor_id WHERE v.epizoda_id = e.id AND sp.stav = 0) = 1) END)
            """,
        Pozicane p => $"({t}.id IN (SELECT titul_id FROM vypozicka WHERE do IS NULL AND titul_id IS NOT NULL{Komu(p, parameter)}) " +
                      $"OR EXISTS (SELECT 1 FROM ({VerzieTitulu(t + ".id")}) vt JOIN verzia_subor vs ON vs.verzia_id = vt.id " +
                      $"JOIN subor sp ON sp.id = vs.subor_id WHERE sp.stav = 0 AND sp.disk_id IN ({PozicaneDisky(p, parameter)})))",
        Rozpozerane r => (r.Hodnota ? "" : "NOT ") +
                         $"({t}.druh = 2 AND EXISTS (SELECT 1 FROM epizoda e JOIN pozretie p ON p.epizoda_id = e.id WHERE e.titul_id = {t}.id) " +
                         $"AND EXISTS (SELECT 1 FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id JOIN verzia_subor vs ON vs.verzia_id = v.id " +
                         $"JOIN subor sp ON sp.id = vs.subor_id WHERE e.titul_id = {t}.id AND sp.stav = 0 " +
                         "AND NOT EXISTS (SELECT 1 FROM pozretie p WHERE p.epizoda_id = e.id)))",
        Stitkovane { Nazvy.Count: 0 } => $"{t}.id IN (SELECT titul_id FROM titul_stitok)",
        Stitkovane st => $"{t}.id IN (SELECT ts.titul_id FROM titul_stitok ts JOIN stitok st ON st.id = ts.stitok_id WHERE " +
                         string.Join(" OR ", st.Nazvy.Select(n => $"st.kluc LIKE {parameter("%" + Escape(n) + "%")} ESCAPE '\\'")) + ")",
        _ => "1",
    };

    /// <summary>The disks lent now (to the person, if the condition names one).</summary>
    private static string PozicaneDisky(Pozicane p, Func<object, string> parameter) =>
        $"SELECT disk_id FROM vypozicka WHERE do IS NULL AND disk_id IS NOT NULL{Komu(p, parameter)}";

    private static string Komu(Pozicane p, Func<object, string> parameter) => p.Komu.Count == 0
        ? ""
        : " AND (" + string.Join(" OR ", p.Komu.Select(k => $"kluc LIKE {parameter("%" + Escape(k) + "%")} ESCAPE '\\'")) + ")";

    /// <summary>A present version: one of its files is on its disk (the version is <c>v</c>).</summary>
    private const string Pritomna = "EXISTS (SELECT 1 FROM verzia_subor vp JOIN subor sv ON sv.id = vp.subor_id WHERE vp.verzia_id = v.id AND sv.stav = 0)";

    private static string PocetFilmu(string t) => $"(SELECT count(*) FROM verzia v WHERE v.titul_id = {t}.id AND {Pritomna})";

    private static string PocetEpizody(string t) =>
        $"(SELECT count(*) FROM epizoda e JOIN verzia v ON v.epizoda_id = e.id WHERE e.titul_id = {t}.id AND {Pritomna} GROUP BY e.id ORDER BY 1 DESC LIMIT 1)";

    private static string RozsahCisla(string vyraz, int? od, int? doo, Func<object, string> parameter) => (od, doo) switch
    {
        ({ } a, { } b) => $"{vyraz} BETWEEN {parameter(a)} AND {parameter(b)}",
        ({ } a, null) => $"{vyraz} >= {parameter(a)}",
        (null, { } b) => $"{vyraz} <= {parameter(b)}",
        _ => "1",
    };

    /// <summary>A file with an exact copy elsewhere (the same fingerprint), or without one (only files with a fingerprint).</summary>
    public static string Duplikat(Duplikat d) => d.Hodnota
        ? "s.odtlacok IS NOT NULL AND EXISTS (SELECT 1 FROM subor d WHERE d.odtlacok = s.odtlacok AND d.id <> s.id AND d.stav = 0)"
        : "s.odtlacok IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subor d WHERE d.odtlacok = s.odtlacok AND d.id <> s.id AND d.stav = 0)";

    /// <summary>
    /// A condition on file <c>s</c> through the title of its version. A negated one ("not a comedy") speaks of versions:
    /// files without a version are no match (as files without technical data for the conditions of E3).
    /// </summary>
    public static string PreSubor(TitulovaPodmienka podmienka, Func<object, string> parameter)
    {
        const string Verzie = "SELECT vs.subor_id FROM verzia_subor vs JOIN verzia v ON v.id = vs.verzia_id";
        var sql = podmienka switch
        {
            VKniznici { Stav: StavVKniznici.VKniznici } => "s.id IN (SELECT subor_id FROM verzia_subor)",
            VKniznici { Stav: StavVKniznici.MimoKniznice } => "s.id IN (SELECT subor_id FROM subor_mimo_kniznice)",
            VKniznici { Stav: StavVKniznici.Film } => $"s.id IN ({Verzie} WHERE v.titul_id IS NOT NULL)",
            VKniznici { Stav: StavVKniznici.Serial } => $"s.id IN ({Verzie} WHERE v.epizoda_id IS NOT NULL)",
            VKniznici => $"s.id IN ({Verzie} WHERE v.potvrdena = 0)",
            // watched: the file's own film or episode
            Videne v => (v.Hodnota ? "" : "NOT ") +
                        $"s.id IN ({Verzie} WHERE v.titul_id IN (SELECT titul_id FROM pozretie WHERE titul_id IS NOT NULL) " +
                        "OR v.epizoda_id IN (SELECT epizoda_id FROM pozretie WHERE epizoda_id IS NOT NULL))",
            Mam m => m.Hodnota ? "s.stav = 0" : "0",
            // lent: its disk, or the title of its version
            Pozicane p => $"(s.disk_id IN ({PozicaneDisky(p, parameter)}) OR s.id IN ({Verzie} LEFT JOIN epizoda e ON e.id = v.epizoda_id " +
                          $"WHERE coalesce(v.titul_id, e.titul_id) IN (SELECT titul_id FROM vypozicka WHERE do IS NULL AND titul_id IS NOT NULL{Komu(p, parameter)})))",
            _ => $"s.id IN ({Verzie} LEFT JOIN epizoda e ON e.id = v.epizoda_id " +
                 $"JOIN titul t ON t.id = coalesce(v.titul_id, e.titul_id) WHERE {PreTitul(podmienka, "t", parameter)})",
        };
        if (!podmienka.Zaporna)
        {
            return sql;
        }

        return podmienka is VKniznici { Stav: StavVKniznici.VKniznici or StavVKniznici.MimoKniznice } or Pozicane
            ? $"NOT ({sql})"
            : $"NOT ({sql}) AND s.id IN (SELECT subor_id FROM verzia_subor)";
    }

    private static string Rozsah(string stlpec, double? od, double? doo, Func<object, string> parameter) => (od, doo) switch
    {
        ({ } a, { } b) => $"{stlpec} BETWEEN {parameter(a)} AND {parameter(b)}",
        ({ } a, null) => $"{stlpec} >= {parameter(a)}",
        (null, { } b) => $"{stlpec} <= {parameter(b)}",
        _ => $"{stlpec} IS NOT NULL",
    };

    private static string Rozsah(string stlpec, int? od, int? doo, Func<object, string> parameter) =>
        Rozsah(stlpec, (double?)od, doo, parameter);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
