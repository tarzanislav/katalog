namespace Katalog.Zdroje;

/// <summary>A picture downloaded for the library (a poster): its bytes and size in pixels.</summary>
public sealed record StiahnutyObrazok(byte[] Data, int Sirka, int Vyska);

/// <summary>
/// Downloads posters – only from TMDB's image server over HTTPS (HISTRA's and Jellyfin's posters are TMDB's too), at
/// most 5 MB, and only a real JPEG or PNG picture.
/// </summary>
public static class StahovanieObrazkov
{
    private const int NajviacBajtov = 5 << 20;

    public static async Task<StiahnutyObrazok?> StiahniAsync(HttpClient http, Uri adresa, CancellationToken zrusenie)
    {
        if (!Povolena(adresa))
        {
            return null;
        }

        var data = await Web.BajtyAsync(http, () => new HttpRequestMessage(HttpMethod.Get, adresa), Rozostup.Obrazky, "TMDB", Povolena, NajviacBajtov, zrusenie)
            .ConfigureAwait(false);
        return data is not null && Rozmery(data) is var (sirka, vyska) && sirka > 0 && vyska > 0 ? new StiahnutyObrazok(data, sirka, vyska) : null;
    }

    private static bool Povolena(Uri adresa) =>
        adresa.Scheme == Uri.UriSchemeHttps && adresa.Host == TmdbZdroj.Obrazky.Host && adresa.IsDefaultPort && adresa.UserInfo.Length == 0;

    /// <summary>Width and height of a JPEG (from its frame header) or PNG (from IHDR); (0, 0) for anything else.</summary>
    public static (int Sirka, int Vyska) Rozmery(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return (Cislo32(data[16..]), Cislo32(data[20..]));
        }

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return (0, 0);
        }

        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                return (0, 0);
            }

            var znacka = data[i + 1];
            if (znacka == 0xFF)
            {
                i++;
                continue;
            }

            var dlzka = (data[i + 2] << 8) | data[i + 3];
            // the frame headers SOF0–SOF15, except DHT (C4), JPG (C8) and DAC (CC)
            if (znacka is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
            }

            if (dlzka < 2)
            {
                return (0, 0);
            }

            i += 2 + dlzka;
        }

        return (0, 0);
    }

    private static int Cislo32(ReadOnlySpan<byte> b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
}
