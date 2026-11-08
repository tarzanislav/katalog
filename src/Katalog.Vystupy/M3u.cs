using System.Globalization;
using System.Text;

namespace Katalog.Vystupy;

/// <summary>One entry of a playlist for players: the full path, the name shown and the length (if known).</summary>
public sealed record PolozkaM3u(string Cesta, string Nazov, TimeSpan? Dlzka = null);

/// <summary>
/// An extended M3U playlist in UTF-8 (.m3u8), as VLC, MPC-HC, mpv, PotPlayer and Windows Media Player read it; line
/// breaks in names are left out, so no entry can become two.
/// </summary>
public static class M3u
{
    public static string Text(IEnumerable<PolozkaM3u> polozky, string? nazov = null)
    {
        ArgumentNullException.ThrowIfNull(polozky);
        var text = new StringBuilder("#EXTM3U\r\n");
        if (!string.IsNullOrWhiteSpace(nazov))
        {
            text.Append("#PLAYLIST:").Append(Riadok(nazov)).Append("\r\n");
        }

        foreach (var p in polozky)
        {
            var sekundy = p.Dlzka is { } d ? ((long)Math.Round(d.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : "-1";
            text.Append("#EXTINF:").Append(sekundy).Append(',').Append(Riadok(p.Nazov)).Append("\r\n");
            text.Append(Riadok(p.Cesta)).Append("\r\n");
        }

        return text.ToString();
    }

    private static string Riadok(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
