using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Zdroje;

/// <summary>
/// Order in which sources fill a field that is not locked (decision A5): the file name first (UNIVERSUM's names are
/// exact), then TMDB, HISTRA and Jellyfin. The user may reorder them; a field the user typed is never overwritten.
/// </summary>
public static class PoradieZdrojov
{
    public static IReadOnlyList<ZdrojUdajov> Predvolene { get; } =
        [ZdrojUdajov.NazovSuboru, ZdrojUdajov.Ffprobe, ZdrojUdajov.Tmdb, ZdrojUdajov.Histra, ZdrojUdajov.Jellyfin];

    /// <summary>The switched-on sources in the given order (switched-off ones are skipped, nothing breaks).</summary>
    public static IReadOnlyList<IZdrojUdajov> Zapnute(IEnumerable<IZdrojUdajov> zdroje, IReadOnlyList<ZdrojUdajov> poradie) =>
        zdroje.Where(z => z.JeZapnuty)
            .OrderBy(z => Index(poradie, z.Zdroj))
            .ToList();

    /// <summary>True when a value from <paramref name="novy"/> may replace one from <paramref name="sucasny"/>.</summary>
    public static bool MozePrepisat(ZdrojUdajov sucasny, bool zamknute, ZdrojUdajov novy, IReadOnlyList<ZdrojUdajov> poradie) =>
        !zamknute && sucasny != ZdrojUdajov.Pouzivatel && Index(poradie, novy) <= Index(poradie, sucasny);

    private static int Index(IReadOnlyList<ZdrojUdajov> poradie, ZdrojUdajov zdroj)
    {
        for (var i = 0; i < poradie.Count; i++)
        {
            if (poradie[i] == zdroj)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
