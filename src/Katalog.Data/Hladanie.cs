using System.Diagnostics;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A file or folder that matched a query.</summary>
/// <param name="PriecinokId">The folder the file is in; for a folder, the folder itself.</param>
public sealed record NajdenaPolozka(
    DruhHladania Druh, long Id, string Nazov, long DiskId, long PriecinokId, long Velkost, long? Zmeneny, string Pripona, bool Zmazana);

/// <param name="Viac">More matched than were returned: the query should be narrowed.</param>
public sealed record VysledkyHladania(IReadOnlyList<NajdenaPolozka> Polozky, bool Viac, TimeSpan Trvanie)
{
    public static VysledkyHladania Prazdne { get; } = new([], false, TimeSpan.Zero);
}

/// <summary>What a query needs to know besides the library: which disks are connected and what time it is.</summary>
public sealed record KontextHladania(IReadOnlyCollection<long> PripojeneDisky, DateTime TerazLokalne)
{
    public static KontextHladania Teraz(IReadOnlyCollection<long> pripojeneDisky) => new(pripojeneDisky, DateTime.Now);
}

/// <summary>
/// Runs a query of the query language over the files and folders of the library. Words go to the full-text index, fields
/// become conditions with parameters (nothing the user writes ever becomes SQL). The result is limited and not ranked: it
/// must come back while the user types (100 ms with a million files); when there is more, the user narrows the query.
/// </summary>
public static class Hladanie
{
    /// <summary>How many files and folders a search returns at most.</summary>
    public const int Najviac = 500;

    public static VysledkyHladania Najdi(SqliteConnection spojenie, Dotaz dotaz, KontextHladania kontext, int najviac = Najviac)
    {
        if (dotaz.JePrazdny)
        {
            return VysledkyHladania.Prazdne;
        }

        var cas = Stopwatch.StartNew();
        var polozky = new List<NajdenaPolozka>();
        var viac = false;
        if (dotaz.HladaPriecinky)
        {
            viac |= Spusti(spojenie, Prikaz(dotaz, kontext, priecinky: true), najviac, polozky);
        }

        if (dotaz.HladaSubory && polozky.Count < najviac)
        {
            viac |= Spusti(spojenie, Prikaz(dotaz, kontext, priecinky: false), najviac - polozky.Count, polozky);
        }

        var poradie = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        var zoradene = polozky
            .OrderBy(p => p.Druh == DruhHladania.Priecinok ? 0 : 1)
            .ThenBy(p => p.Nazov, poradie)
            .ToList();
        return new VysledkyHladania(zoradene, viac, cas.Elapsed);
    }

    /// <summary>
    /// Paths of folders inside their disks ("Filmy/Nové"), for showing results. Parents shared by many results are read
    /// once.
    /// </summary>
    public static IReadOnlyDictionary<long, string> Cesty(SqliteConnection spojenie, IEnumerable<long> priecinky)
    {
        var rodicia = new Dictionary<long, (long? Rodic, string Nazov)>();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT rodic_id, nazov FROM priecinok WHERE id = $id";
        var parameter = prikaz.Parameters.Add("$id", SqliteType.Integer);

        (long? Rodic, string Nazov) Citaj(long id)
        {
            if (!rodicia.TryGetValue(id, out var zaznam))
            {
                parameter.Value = id;
                using var citac = prikaz.ExecuteReader();
                zaznam = citac.Read() ? (citac.IsDBNull(0) ? null : citac.GetInt64(0), citac.GetString(1)) : (null, "");
                rodicia[id] = zaznam;
            }

            return zaznam;
        }

        var cesty = new Dictionary<long, string>();
        foreach (var priecinok in priecinky)
        {
            if (cesty.ContainsKey(priecinok))
            {
                continue;
            }

            var casti = new List<string>();
            long? aktualny = priecinok;
            while (aktualny is { } id && casti.Count < 512)
            {
                var (rodic, nazov) = Citaj(id);
                if (nazov.Length > 0)
                {
                    casti.Add(nazov);
                }

                aktualny = rodic;
            }

            casti.Reverse();
            cesty[priecinok] = string.Join('/', casti);
        }

        return cesty;
    }

    private static bool Spusti(SqliteConnection spojenie, SqliteCommand prikaz, int najviac, List<NajdenaPolozka> polozky)
    {
        using (prikaz)
        {
            prikaz.Connection = spojenie;
            prikaz.Parameters.AddWithValue("$najviac", najviac + 1);
            using var citac = prikaz.ExecuteReader();
            var pocet = 0;
            while (citac.Read())
            {
                if (++pocet > najviac)
                {
                    return true;
                }

                var druh = (DruhHladania)citac.GetInt32(0);
                polozky.Add(new NajdenaPolozka(druh, citac.GetInt64(1), citac.GetString(2), citac.GetInt64(3), citac.GetInt64(4),
                    citac.GetInt64(5), citac.IsDBNull(6) ? null : citac.GetInt64(6), citac.GetString(7), citac.GetInt32(8) == 1));
            }

            return false;
        }
    }

    /// <summary>The SQL of a query, for files or for folders, with its parameters.</summary>
    internal static SqliteCommand Prikaz(Dotaz dotaz, KontextHladania kontext, bool priecinky)
    {
        var prikaz = new SqliteCommand();
        var podmienky = new List<string>();
        var cislo = 0;
        string Parameter(object hodnota)
        {
            var meno = "$p" + (++cislo).ToString(CultureInfo.InvariantCulture);
            prikaz.Parameters.AddWithValue(meno, hodnota);
            return meno;
        }

        var druh = priecinky ? DruhHladania.Priecinok : DruhHladania.Subor;
        var x = priecinky ? "p" : "s";

        // words: the positive ones go to the full-text index, the rest become conditions
        var hladane = new List<string>();
        foreach (var slovo in dotaz.Podmienky.OfType<Slovo>())
        {
            var dostDlhe = slovo.Text.Length >= Fulltext.NajkratsiText;
            if (!slovo.Zaporna && dostDlhe)
            {
                hladane.Add(Fulltext.Fraza(slovo.Text));
            }
            else if (dostDlhe)
            {
                podmienky.Add($"{x}.id NOT IN (SELECT rowid >> 3 FROM hladanie WHERE hladanie MATCH {Parameter(Fulltext.Fraza(slovo.Text))} AND (rowid & 7) = {(int)druh})");
            }
            else
            {
                // one or two letters are too short for the index: a plain comparison of the name
                var podobne = $"{x}.nazov LIKE {Parameter("%" + Escape(slovo.Text) + "%")} ESCAPE '\\'";
                podmienky.Add(slovo.Zaporna ? "NOT (" + podobne + ")" : podobne);
            }
        }

        foreach (var podmienka in dotaz.Podmienky)
        {
            var sql = podmienka switch
            {
                Pripony p when !priecinky => $"{x}.pripona IN ({Zoznam(p.Zoznam, Parameter)})",
                Typy t when !priecinky => t.VsetkySubory ? "1" : t.Druhy.Count == 0 ? "0"
                    : $"{x}.pripona IN ({Zoznam(t.Druhy.SelectMany(DruhySuborov.Pripony).Distinct(), Parameter)})",
                Typy t => t.Priecinky ? "1" : "0",
                Velkost v when !priecinky => Rozsah($"{x}.velkost", v.Od, v.Do, Parameter),
                Obdobie o when o.Pole == PoleDotazu.Zmenene => PodmienkaObdobia($"{x}.zmeneny", o, kontext, Parameter),
                Obdobie o when !priecinky => PodmienkaObdobia($"{x}.prvy_raz", o, kontext, Parameter),
                Disky d => $"{x}.disk_id IN (SELECT id FROM disk WHERE {PodmienkaDiskov(d, Parameter)})",
                Katalogy k => $"{x}.disk_id IN (SELECT d.id FROM disk d JOIN katalog k ON k.id = d.katalog_id WHERE " +
                              string.Join(" OR ", k.Nazvy.Select(n => $"k.nazov LIKE {Parameter("%" + Escape(n) + "%")} ESCAPE '\\'")) + ")",
                Pripojene p => $"{x}.disk_id {(p.Hodnota ? "IN" : "NOT IN")} (SELECT value FROM json_each({Parameter(Json(kontext.PripojeneDisky))}))",
                MedialnaPodmienka m when !priecinky => PodmienkaMedii(m, Parameter),
                _ => null,
            };
            if (sql is not null)
            {
                podmienky.Add(podmienka.Zaporna ? "NOT (" + sql + ")" : sql);
            }
        }

        podmienky.Add(dotaz.Zmazane switch
        {
            RezimZmazanych.Aj => "1",
            RezimZmazanych.Len => $"{x}.stav = 1",
            _ => $"{x}.stav = 0",
        });
        if (priecinky)
        {
            // the root folder of a disk has no name: the disk itself is not a search result here
            podmienky.Add("p.rodic_id IS NOT NULL");
        }

        var stlpce = priecinky
            ? "2, p.id, p.nazov, p.disk_id, p.id, 0, p.zmeneny, '', p.stav"
            : "1, s.id, s.nazov, s.disk_id, s.priecinok_id, s.velkost, s.zmeneny, s.pripona, s.stav";
        var tabulka = priecinky ? "priecinok p" : "subor s";
        var sqlText = new StringBuilder();
        if (hladane.Count > 0)
        {
            podmienky.Insert(0, $"hladanie MATCH {Parameter(string.Join(" AND ", hladane))} AND (h.rowid & 7) = {(int)druh}");
            sqlText.Append($"SELECT {stlpce} FROM hladanie h JOIN {tabulka} ON {x}.id = h.rowid >> 3");
        }
        else
        {
            sqlText.Append($"SELECT {stlpce} FROM {tabulka}");
        }

        sqlText.Append(" WHERE ").Append(string.Join(" AND ", podmienky)).Append(" LIMIT $najviac");
        prikaz.CommandText = sqlText.ToString();
        return prikaz;
    }

    /// <summary>
    /// Technical data (stage E3) as a set of files: <c>s.id IN (…)</c> lets SQLite go from the few matching files of a
    /// rare condition straight to them, and check a common one against the files of the words.
    /// </summary>
    private static string PodmienkaMedii(MedialnaPodmienka podmienka, Func<object, string> parameter)
    {
        static string Udaje(string kde) => "s.id IN (SELECT subor_id FROM medialne_udaje WHERE " + kde + ")";

        return podmienka switch
        {
            Kvality k => Udaje($"trieda_kvality IN ({string.Join(", ", k.Triedy.Select(t => parameter((int)t)))})"),
            Kodeky k => Udaje($"video_kodek IN ({Zoznam(k.Zoznam, parameter)})"),
            HdrDruhy h => Udaje($"hdr IN ({string.Join(", ", h.Druhy.Select(d => parameter((int)d)))})"),
            Dlzka d => Udaje(Rozsah("dlzka_ms", d.OdMs, d.DoMs, parameter)),
            Poskodene p => Udaje(p.Hodnota ? "chyby IS NOT NULL" : "chyby IS NULL"),
            JazykyStop { Akakolvek: true } j => $"s.id IN (SELECT subor_id FROM stopa WHERE druh = {(int)j.Druh})",
            JazykyStop j => $"s.id IN (SELECT subor_id FROM stopa WHERE druh = {(int)j.Druh} AND jazyk IN ({Zoznam(j.Jazyky, parameter)}))",
            _ => "1",
        };
    }

    private static string Zoznam(IEnumerable<string> hodnoty, Func<object, string> parameter) =>
        string.Join(", ", hodnoty.Select(h => parameter(h)));

    private static string Rozsah(string stlpec, long? od, long? doo, Func<object, string> parameter) => (od, doo) switch
    {
        ({ } a, { } b) => $"{stlpec} BETWEEN {parameter(a)} AND {parameter(b)}",
        ({ } a, null) => $"{stlpec} >= {parameter(a)}",
        (null, { } b) => $"{stlpec} <= {parameter(b)}",
        _ => "1",
    };

    /// <summary>A local time interval as UTC ticks (how times are stored); missing times never match.</summary>
    private static string PodmienkaObdobia(string stlpec, Obdobie obdobie, KontextHladania kontext, Func<object, string> parameter)
    {
        var casti = new List<string> { $"{stlpec} IS NOT NULL" };
        if (obdobie.Od is { } od)
        {
            casti.Add($"{stlpec} >= {parameter(Tiky(od.Urci(kontext.TerazLokalne)))}");
        }

        if (obdobie.Do is { } doo)
        {
            casti.Add($"{stlpec} < {parameter(Tiky(doo.Urci(kontext.TerazLokalne)))}");
        }

        return string.Join(" AND ", casti);
    }

    private static long Tiky(DateTime lokalne) =>
        DateTime.SpecifyKind(lokalne, DateTimeKind.Local).ToUniversalTime().Ticks;

    private static string PodmienkaDiskov(Disky disky, Func<object, string> parameter)
    {
        var casti = new List<string>();
        if (disky.Cisla.Count > 0)
        {
            casti.Add($"cislo IN ({string.Join(", ", disky.Cisla.Select(c => parameter(c)))})");
        }

        casti.AddRange(disky.Nazvy.Select(n => $"nazov LIKE {parameter("%" + Escape(n) + "%")} ESCAPE '\\'"));
        return casti.Count == 0 ? "0" : string.Join(" OR ", casti);
    }

    private static string Json(IReadOnlyCollection<long> cisla) =>
        "[" + string.Join(",", cisla.Select(c => c.ToString(CultureInfo.InvariantCulture))) + "]";

    /// <summary>Characters of LIKE that the user means literally.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
