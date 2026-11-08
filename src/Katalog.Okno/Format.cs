using System.Globalization;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>Numbers, sizes and times as the window shows them (Slovak: decimal comma, day.month.year).</summary>
internal static class Format
{
    public static readonly CultureInfo Slovensky = CultureInfo.GetCultureInfo("sk-SK");

    private static readonly string[] Jednotky = ["B", "kB", "MB", "GB", "TB", "PB"];

    /// <summary>"512 B", "1,2 MB", "3,64 TB" (as Windows Explorer counts them, by 1024).</summary>
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

    public static string Velkost(long? bajty) => bajty is { } b ? Velkost(b) : "–";

    /// <summary>Local date and time of UTC ticks ("8. 11. 2026 9:41").</summary>
    public static string Cas(long tiky) => Jadro.Model.Cas.NaUtc(tiky).ToLocalTime().ToString("d. M. yyyy H:mm", Slovensky);

    public static string Cas(long? tiky) => tiky is { } t ? Cas(t) : "nikdy";

    public static string Pocet(long pocet) => pocet.ToString("#,0", Slovensky);

    public static string Druh(DruhDisku druh) => druh switch
    {
        DruhDisku.Usb => "USB disk",
        DruhDisku.Interny => "interný disk",
        DruhDisku.Sietovy => "sieťový disk",
        DruhDisku.Opticky => "DVD / Blu-ray",
        DruhDisku.Vzdialeny => "vzdialený server",
        _ => "disk",
    };
}
