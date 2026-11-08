using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>
/// Forward-only migrations of the library schema (decision A9). Each step is an embedded file Schema/NNN_*.sql that is
/// never edited once released; a step runs in its own transaction together with raising PRAGMA user_version, so a
/// library is always either at the old or at the new version, never in between.
/// </summary>
public static class Migracie
{
    private static readonly IReadOnlyList<(int Verzia, string Zdroj)> Kroky = NacitajKroky();

    /// <summary>Schema version this program creates and understands.</summary>
    public static int Posledna => Kroky[^1].Verzia;

    /// <summary>SQL of one step (for tests and the documentation).</summary>
    public static string Sql(int verzia) => Citaj(Kroky.Single(k => k.Verzia == verzia).Zdroj);

    /// <summary>Raises the library from <paramref name="odVerzie"/> to <paramref name="doVerzie"/> (default: the latest).</summary>
    internal static void Vykonaj(SqliteConnection spojenie, int odVerzie, int? doVerzie = null)
    {
        foreach (var (verzia, zdroj) in Kroky.Where(k => k.Verzia > odVerzie && k.Verzia <= (doVerzie ?? Posledna)))
        {
            var sql = Citaj(zdroj);
            Kniznica.Vykonaj(spojenie, "BEGIN IMMEDIATE");
            try
            {
                Kniznica.Vykonaj(spojenie, sql);
                if (verzia == 1)
                {
                    Kniznica.Vykonaj(spojenie, string.Create(CultureInfo.InvariantCulture, $"PRAGMA application_id = {Kniznica.IdAplikacie}"));
                }

                Kniznica.Vykonaj(spojenie, string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {verzia}"));
                // a migration must leave every reference valid
                using (var prikaz = Kniznica.Prikaz(spojenie, "PRAGMA foreign_key_check"))
                using (var kontrola = prikaz.ExecuteReader())
                {
                    if (kontrola.Read())
                    {
                        throw new InvalidOperationException($"Migrácia {verzia} porušila väzby v tabuľke {kontrola.GetString(0)}.");
                    }
                }

                Kniznica.Vykonaj(spojenie, "COMMIT");
            }
            catch
            {
                Kniznica.Vykonaj(spojenie, "ROLLBACK");
                throw;
            }
        }
    }

    private static IReadOnlyList<(int, string)> NacitajKroky()
    {
        const string predpona = "Katalog.Data.Schema.";
        var kroky = typeof(Migracie).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(predpona, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => (Verzia: int.Parse(n.AsSpan(predpona.Length, 3), CultureInfo.InvariantCulture), Zdroj: n))
            .OrderBy(k => k.Verzia)
            .ToList();
        // steps are numbered 1, 2, 3 … without gaps
        for (var i = 0; i < kroky.Count; i++)
        {
            if (kroky[i].Verzia != i + 1)
            {
                throw new InvalidOperationException($"Migrácie nie sú očíslované za sebou: chýba {i + 1}.");
            }
        }

        return kroky.Count > 0 ? kroky : throw new InvalidOperationException("Chýba schéma knižnice.");
    }

    private static string Citaj(string zdroj)
    {
        using var prud = Assembly.GetExecutingAssembly().GetManifestResourceStream(zdroj)!;
        using var citac = new StreamReader(prud);
        return citac.ReadToEnd();
    }
}
