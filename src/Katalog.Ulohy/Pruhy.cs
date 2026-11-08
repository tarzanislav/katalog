using System.Globalization;

namespace Katalog.Ulohy;

/// <summary>
/// Lanes of the job queue (decision A7): every physical disk has its own lane, so a slow USB disk never holds up the
/// internal one; internet sources have their own lanes with a limit of requests.
/// </summary>
public static class Pruhy
{
    public static string Disk(long idDisku) => string.Create(CultureInfo.InvariantCulture, $"disk:{idDisku}");

    public static string Siet(string zdroj) => "net:" + zdroj.ToLowerInvariant();

    /// <summary>Work that needs neither a disk nor the internet (pictures, exports).</summary>
    public const string Pocitac = "cpu";
}
