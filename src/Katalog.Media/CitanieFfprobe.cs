using System.Globalization;
using System.Text.Json;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>What ffprobe said about one file: its technical data and its audio and subtitle tracks.</summary>
public sealed record VysledokFfprobe(MedialneUdaje Udaje, IReadOnlyList<Stopa> Stopy);

/// <summary>
/// Reads the JSON of <c>ffprobe -print_format json -show_format -show_streams -show_error</c>. Never throws for what a
/// file contains: what cannot be read becomes an error of the file (a damaged file, N3), not of the program.
/// </summary>
public static class CitanieFfprobe
{
    public static VysledokFfprobe Citaj(string json, string? chybovyVystup = null)
    {
        JsonDocument dokument;
        try
        {
            dokument = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Chyba(string.IsNullOrWhiteSpace(chybovyVystup) ? "ffprobe nevrátil čitateľný výsledok" : chybovyVystup);
        }

        using (dokument)
        {
            var koren = dokument.RootElement;
            if (koren.TryGetProperty("error", out var chyba))
            {
                return Chyba(Text(chyba, "string") ?? "ffprobe hlási chybu");
            }

            var streams = koren.TryGetProperty("streams", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : [];
            var format = koren.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
            if (streams.Count == 0)
            {
                return Chyba(string.IsNullOrWhiteSpace(chybovyVystup) ? "súbor nemá žiadnu stopu" : chybovyVystup);
            }

            // the picture of the film is the first video track that is not a cover image
            var video = streams.FirstOrDefault(x => Text(x, "codec_type") == "video" && !Priznak(x, "attached_pic"));
            var hlavneVideo = video.ValueKind == JsonValueKind.Object;
            var dlzka = Sekundy(Text(format, "duration")) ?? (hlavneVideo ? Sekundy(Text(video, "duration")) ?? DlzkaZoZnacky(video) : null);
            var udaje = new MedialneUdaje(
                Kontajner(Text(format, "format_name")),
                dlzka is { } d ? (long)Math.Round(d * 1000) : null,
                Cislo(Text(format, "bit_rate")),
                hlavneVideo ? Text(video, "codec_name") : null,
                hlavneVideo ? CeleCislo(video, "width") : null,
                hlavneVideo ? CeleCislo(video, "height") : null,
                hlavneVideo ? Hdr(video) : Jadro.Model.Hdr.Ziadne,
                hlavneVideo ? Snimky(Text(video, "avg_frame_rate")) ?? Snimky(Text(video, "r_frame_rate")) : null,
                hlavneVideo || streams.Any(x => Text(x, "codec_type") == "audio") ? null : "súbor nemá obraz ani zvuk");

            var stopy = new List<Stopa>();
            foreach (var stream in streams)
            {
                var druh = Text(stream, "codec_type") switch
                {
                    "audio" => DruhStopy.Zvuk,
                    "subtitle" => DruhStopy.Titulky,
                    _ => (DruhStopy?)null,
                };
                if (druh is not { } d2)
                {
                    continue;
                }

                var tagy = stream.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
                var nazov = Text(tagy, "title") ?? Text(tagy, "TITLE");
                stopy.Add(new Stopa(
                    CeleCislo(stream, "index") ?? stopy.Count,
                    d2,
                    Jazyky.Normalizuj(Text(tagy, "language") ?? Text(tagy, "LANGUAGE")),
                    KodekZvuku(Text(stream, "codec_name"), Text(stream, "profile")),
                    d2 == DruhStopy.Zvuk ? CeleCislo(stream, "channels") : null,
                    Priznak(stream, "default"),
                    Priznak(stream, "forced") || (nazov?.Contains("forced", StringComparison.OrdinalIgnoreCase) ?? false),
                    Priznak(stream, "hearing_impaired") || (nazov?.Contains("SDH", StringComparison.Ordinal) ?? false),
                    string.IsNullOrWhiteSpace(nazov) ? null : nazov.Trim()));
            }

            return new VysledokFfprobe(udaje, stopy);
        }
    }

    private static VysledokFfprobe Chyba(string chyba)
    {
        var text = chyba.Trim();
        return new VysledokFfprobe(MedialneUdaje.Necitatelne(text.Length > 500 ? text[..500] : text), []);
    }

    /// <summary>"matroska,webm" → "mkv", "mov,mp4,m4a,3gp,3g2,mj2" → "mp4", "mpegts" → "ts".</summary>
    private static string? Kontajner(string? nazov) => nazov?.Split(',')[0] switch
    {
        null or "" => null,
        "matroska" => "mkv",
        "mov" => "mp4",
        "mpegts" => "ts",
        "mpeg" => "mpg",
        "asf" => "wmv",
        "flv" => "flv",
        var k => k,
    };

    /// <summary>The audio codec refined by its profile: DTS-HD MA, DTS-HD HRA and DTS:X are not plain DTS.</summary>
    private static string? KodekZvuku(string? kodek, string? profil) => (kodek, profil) switch
    {
        ("dts", { } p) when p.Contains("DTS:X", StringComparison.OrdinalIgnoreCase) => "dts:x",
        ("dts", { } p) when p.Contains("MA", StringComparison.Ordinal) => "dts-hd ma",
        ("dts", { } p) when p.Contains("HRA", StringComparison.Ordinal) => "dts-hd hra",
        _ => kodek,
    };

    /// <summary>HDR from the transfer of the video and its side data (Dolby Vision has a configuration record).</summary>
    private static Hdr Hdr(JsonElement video)
    {
        if (video.TryGetProperty("side_data_list", out var data) && data.ValueKind == JsonValueKind.Array &&
            data.EnumerateArray().Any(x => Text(x, "side_data_type")?.Contains("DOVI", StringComparison.Ordinal) == true))
        {
            return Jadro.Model.Hdr.DolbyVision;
        }

        return Text(video, "color_transfer") switch
        {
            "smpte2084" => Jadro.Model.Hdr.Hdr10,
            "arib-std-b67" => Jadro.Model.Hdr.Hlg,
            _ => Jadro.Model.Hdr.Ziadne,
        };
    }

    /// <summary>Matroska keeps the length of a track in its tag DURATION ("01:23:45.123000000").</summary>
    private static double? DlzkaZoZnacky(JsonElement stream) =>
        stream.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object && Text(t, "DURATION") is { } text &&
        TimeSpan.TryParse(text.Length > 16 ? text[..16] : text, CultureInfo.InvariantCulture, out var cas)
            ? cas.TotalSeconds
            : null;

    /// <summary>"24000/1001" → 23.976; "0/0" → null.</summary>
    private static double? Snimky(string? zlomok)
    {
        var casti = zlomok?.Split('/');
        if (casti is not [var a, var b] || !double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var citatel) ||
            !double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var menovatel) || citatel <= 0 || menovatel <= 0)
        {
            return null;
        }

        var snimky = citatel / menovatel;
        return snimky is > 0 and < 1000 ? Math.Round(snimky, 3) : null;
    }

    private static double? Sekundy(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s is >= 0 and < 1e7 ? s : null;

    private static long? Cislo(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : null;

    private static int? CeleCislo(JsonElement prvok, string meno) =>
        prvok.ValueKind == JsonValueKind.Object && prvok.TryGetProperty(meno, out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var c)
            ? c
            : null;

    private static bool Priznak(JsonElement stream, string meno) =>
        stream.TryGetProperty("disposition", out var d) && d.ValueKind == JsonValueKind.Object && CeleCislo(d, meno) == 1;

    private static string? Text(JsonElement prvok, string meno)
    {
        if (prvok.ValueKind != JsonValueKind.Object || !prvok.TryGetProperty(meno, out var h))
        {
            return null;
        }

        return h.ValueKind switch
        {
            JsonValueKind.String => h.GetString() is { Length: > 0 } t ? t : null,
            JsonValueKind.Number => h.GetRawText(),
            _ => null,
        };
    }
}
