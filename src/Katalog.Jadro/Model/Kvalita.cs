namespace Katalog.Jadro.Model;

/// <summary>Quality class of a video by its resolution.</summary>
public static class Kvalita
{
    /// <summary>
    /// SD, HD, FHD or 4K, tolerant of cropped wide formats (1920×800 is FHD) – the same limits as the names of
    /// UNIVERSUM's library, so a file and its name always agree.
    /// </summary>
    public static TriedaKvality Z(int sirka, int vyska)
    {
        if (sirka <= 0 || vyska <= 0)
        {
            return TriedaKvality.Nezistena;
        }

        return sirka >= 3200 || vyska >= 1800 ? TriedaKvality.UHD4K
            : sirka >= 1600 || vyska >= 1000 ? TriedaKvality.FHD
            : sirka >= 1100 || vyska >= 650 ? TriedaKvality.HD
            : TriedaKvality.SD;
    }

    /// <summary>Short name used in names and queries ("SD", "HD", "FHD", "4K").</summary>
    public static string Skratka(TriedaKvality trieda) => trieda switch
    {
        TriedaKvality.SD => "SD",
        TriedaKvality.HD => "HD",
        TriedaKvality.FHD => "FHD",
        TriedaKvality.UHD4K => "4K",
        _ => "",
    };
}
