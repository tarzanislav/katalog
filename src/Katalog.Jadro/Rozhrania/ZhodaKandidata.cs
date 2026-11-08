using Katalog.Jadro.Model;

namespace Katalog.Jadro.Rozhrania;

/// <summary>
/// How well a candidate of a source matches a searched title, and whether it may be taken without asking (R5: only an
/// unambiguous name with its year – a wrong poster is worse than none).
/// </summary>
public static class ZhodaKandidata
{
    /// <summary>The match needed to take a candidate without asking.</summary>
    public const double Ista = 0.95;

    /// <summary>0 to 1: the name (its own or the original, without diacritics and punctuation) and the year.</summary>
    public static double Urci(HladanieTitulu hladanie, Kandidat kandidat)
    {
        var hladany = Normalizacia.Nazov(hladanie.Nazov);
        var meno = new[] { kandidat.Nazov, kandidat.Original }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => ZhodaMena(hladany, Normalizacia.Nazov(n!)))
            .DefaultIfEmpty(0)
            .Max();
        var rok = (hladanie.Rok, kandidat.Rok) switch
        {
            (null, _) => 0.85,
            (_, null) => 0.7,
            var (h, k) when h == k => 1.0,
            var (h, k) when Math.Abs(h!.Value - k!.Value) == 1 => 0.9,
            _ => 0.5,
        };
        var druh = hladanie.Druh is { } d && d != kandidat.Druh ? 0.8 : 1.0;
        return Math.Round(meno * rok * druh, 3);
    }

    /// <summary>
    /// The candidates scored and ordered, and the one to take without asking: the only one with the same name and year
    /// (none when the year is not known, or two match).
    /// </summary>
    public static (IReadOnlyList<Kandidat> Kandidati, Kandidat? Isty) Vyber(HladanieTitulu hladanie, IEnumerable<Kandidat> kandidati)
    {
        var zoradeni = kandidati
            .DistinctBy(k => (k.Zdroj, k.Identifikator))
            .Select(k => k with { Zhoda = Urci(hladanie, k) })
            .OrderByDescending(k => k.Zhoda)
            .ToList();
        var isti = zoradeni.Where(k => k.Zhoda >= Ista).ToList();
        return (zoradeni, hladanie.Rok is not null && isti.Count == 1 ? isti[0] : null);
    }

    private static double ZhodaMena(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return 0.75;
        }

        var slovaA = a.Split(' ').ToHashSet(StringComparer.Ordinal);
        var slovaB = b.Split(' ').ToHashSet(StringComparer.Ordinal);
        var spolocne = slovaA.Intersect(slovaB).Count();
        return 0.6 * spolocne / Math.Max(1, slovaA.Union(slovaB).Count());
    }
}
