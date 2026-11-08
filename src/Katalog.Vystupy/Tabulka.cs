using System.Globalization;

namespace Katalog.Vystupy;

public enum DruhStlpca
{
    Text = 0,

    /// <summary>A whole number (long).</summary>
    Cislo = 1,

    /// <summary>A number with decimals (double), e.g. a rating.</summary>
    Desatinne = 2,

    /// <summary>Bytes (long); tables for calculating get gigabytes, the ones for reading "1,2 GB".</summary>
    Velkost = 3,

    /// <summary>A local date and time (DateTime).</summary>
    Datum = 4,

    /// <summary>Seconds (double or long), shown as 1:45:30.</summary>
    Trvanie = 5,

    /// <summary>A year (long), without the thousands space.</summary>
    Rok = 6,
}

/// <summary>A column: its heading, kind and relative width on paper (1 = normal).</summary>
public sealed record StlpecTabulky(string Nazov, DruhStlpca Druh = DruhStlpca.Text, double Sirka = 1);

/// <summary>
/// What a list in the window shows (files, titles, disks), as one table for every export: CSV, Excel, HTML, PDF and
/// print. A cell is null, string, long, double or DateTime by its column's kind.
/// </summary>
public sealed record Tabulka(string Nazov, IReadOnlyList<StlpecTabulky> Stlpce, IReadOnlyList<object?[]> Riadky)
{
    /// <summary>A line under the title: the query, the date (e.g. "typ:video · 8. 11. 2026").</summary>
    public string? Popis { get; init; }
}

/// <summary>Values written the Slovak way (decimal comma, day. month. year) – one place for all outputs.</summary>
public static class FormatVystupu
{
    public static readonly CultureInfo Slovensky = CultureInfo.GetCultureInfo("sk-SK");

    private static readonly string[] Jednotky = ["B", "kB", "MB", "GB", "TB", "PB"];

    public const double BajtovNaGb = 1024d * 1024 * 1024;

    /// <summary>"512 B", "1,2 MB", "3,64 TB" (by 1024, as Windows counts).</summary>
    public static string Velkost(long bajty)
    {
        double hodnota = bajty;
        var jednotka = 0;
        while (hodnota >= 1024 && jednotka < Jednotky.Length - 1)
        {
            hodnota /= 1024;
            jednotka++;
        }

        var text = jednotka == 0 ? hodnota.ToString("0", Slovensky)
            : hodnota < 10 ? hodnota.ToString("0.0#", Slovensky)
            : hodnota < 100 ? hodnota.ToString("0.0", Slovensky)
            : hodnota.ToString("0", Slovensky);
        return text + " " + Jednotky[jednotka];
    }

    public static string Datum(DateTime cas) => cas.ToString(cas.TimeOfDay == TimeSpan.Zero ? "d. M. yyyy" : "d. M. yyyy H:mm", Slovensky);

    public static string Trvanie(double sekundy)
    {
        var cas = TimeSpan.FromSeconds(Math.Round(Math.Max(0, sekundy)));
        return cas.TotalHours >= 1
            ? $"{(int)cas.TotalHours}:{cas.Minutes:00}:{cas.Seconds:00}"
            : $"{cas.Minutes}:{cas.Seconds:00}";
    }

    /// <summary>A cell as a person reads it (HTML, PDF, print).</summary>
    public static string NaCitanie(object? hodnota, DruhStlpca druh) => hodnota switch
    {
        null => "",
        string s => s,
        DateTime d => Datum(d),
        _ when druh == DruhStlpca.Velkost => Velkost(Convert.ToInt64(hodnota, CultureInfo.InvariantCulture)),
        _ when druh == DruhStlpca.Trvanie => Trvanie(Convert.ToDouble(hodnota, CultureInfo.InvariantCulture)),
        _ when druh == DruhStlpca.Rok => Convert.ToInt64(hodnota, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        long l => l.ToString("#,0", Slovensky),
        int i => i.ToString("#,0", Slovensky),
        double d => d.ToString("0.0#", Slovensky),
        IFormattable f => f.ToString(null, Slovensky),
        _ => hodnota.ToString() ?? "",
    };

    /// <summary>The heading of a column in a table for calculating (sizes are in GB there).</summary>
    public static string NazovNaVypocet(StlpecTabulky stlpec) =>
        stlpec.Druh == DruhStlpca.Velkost ? stlpec.Nazov + " (GB)" : stlpec.Nazov;
}
