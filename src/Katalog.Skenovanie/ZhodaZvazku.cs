using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Skenovanie;

/// <summary>What the program does with a connected volume compared with the disks of the library.</summary>
public enum VerdiktZhody
{
    /// <summary>Not one of the known disks.</summary>
    Iny = 0,
    /// <summary>Probably a known disk, but the user confirms ("Je to disk 003?").</summary>
    Opytat = 1,
    /// <summary>Certainly this disk.</summary>
    Isto = 2,
}

/// <summary>Points of agreement between a stored identity and a connected volume.</summary>
public sealed record Zhoda(int Body, VerdiktZhody Verdikt);

/// <summary>
/// Recognises a disk by its volume, never by its drive letter: every agreeing part of the identity adds points, a part that
/// certainly differs takes them away. A volume GUID or a network address is decisive; a label alone never is (labels are
/// renamed, many disks are called "Záloha").
/// </summary>
public static class ZhodaZvazku
{
    public const int Isto = 60;
    public const int Opytat = 30;

    public static Zhoda Porovnaj(IdentitaZvazku ulozena, IdentitaZvazku najdena)
    {
        if (ulozena.Druh != DruhDisku.Nezisteny && najdena.Druh != DruhDisku.Nezisteny && ulozena.Druh != najdena.Druh &&
            !(JeLokalny(ulozena.Druh) && JeLokalny(najdena.Druh)))
        {
            return new Zhoda(0, VerdiktZhody.Iny);
        }

        var body = 0;
        body += Text(ulozena.GuidZvazku, najdena.GuidZvazku, zhoda: 60, rozdiel: -60);
        body += Text(ulozena.Adresa, najdena.Adresa, zhoda: 60, rozdiel: -30);
        body += Text(ulozena.SerioveCisloZvazku, najdena.SerioveCisloZvazku, zhoda: 40, rozdiel: -40);
        body += Text(ulozena.SerioveCisloDisku, najdena.SerioveCisloDisku, zhoda: 25, rozdiel: -15);
        body += Text(ulozena.SuborovySystem, najdena.SuborovySystem, zhoda: 5, rozdiel: -20);
        body += Text(ulozena.Menovka, najdena.Menovka, zhoda: 5, rozdiel: 0);
        if (ulozena.Velkost is { } a && najdena.Velkost is { } b)
        {
            body += a == b ? 15 : -40;
        }

        var verdikt = body >= Isto ? VerdiktZhody.Isto : body >= Opytat ? VerdiktZhody.Opytat : VerdiktZhody.Iny;
        return new Zhoda(body, verdikt);
    }

    /// <summary>
    /// The known disk a connected volume is: the single certain match, or a question when the best match is uncertain or
    /// two disks match equally well; null when none matches.
    /// </summary>
    public static (T Disk, VerdiktZhody Verdikt)? Najlepsia<T>(IEnumerable<T> disky, Func<T, IdentitaZvazku> identita, IdentitaZvazku najdena)
    {
        var poradie = disky
            .Select(d => (Disk: d, Zhoda: Porovnaj(identita(d), najdena)))
            .Where(z => z.Zhoda.Verdikt != VerdiktZhody.Iny)
            .OrderByDescending(z => z.Zhoda.Body)
            .ToList();
        if (poradie.Count == 0)
        {
            return null;
        }

        var prva = poradie[0];
        var nejasne = poradie.Count > 1 && poradie[1].Zhoda.Body == prva.Zhoda.Body;
        return (prva.Disk, nejasne ? VerdiktZhody.Opytat : prva.Zhoda.Verdikt);
    }

    // a USB disk built into a computer later is still the same disk
    private static bool JeLokalny(DruhDisku druh) => druh is DruhDisku.Usb or DruhDisku.Interny;

    private static int Text(string? ulozeny, string? najdeny, int zhoda, int rozdiel)
    {
        if (string.IsNullOrWhiteSpace(ulozeny) || string.IsNullOrWhiteSpace(najdeny))
        {
            return 0;
        }

        return string.Equals(ulozeny.Trim(), najdeny.Trim(), StringComparison.OrdinalIgnoreCase) ? zhoda : rozdiel;
    }
}
