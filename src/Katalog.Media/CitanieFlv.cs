using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of Flash Video (FLV, also with the enhanced codecs HEVC, AV1, VP9): the metadata (onMetaData,
/// AMF0) and the first tags of video and audio at the start (codec configuration: avcC, hvcC, AudioSpecificConfig),
/// and the last tag at the end for the length when the metadata do not say it.
/// </summary>
public static class CitanieFlv
{
    private const int Zaciatok = 2 << 20;

    public static InfoSuboru? Citaj(string cesta)
    {
        try
        {
            using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess);
            return Citaj(subor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Never throws for what a file contains: a damaged or cut file gives null.</summary>
    public static InfoSuboru? Citaj(Stream subor)
    {
        try
        {
            return CitajBezpecne(subor);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException or InvalidDataException or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static InfoSuboru? CitajBezpecne(Stream subor)
    {
        var data = new byte[(int)Math.Min(Zaciatok, subor.Length)];
        subor.Position = 0;
        subor.ReadExactly(data);
        if (data.Length < 13 || data[0] != 'F' || data[1] != 'L' || data[2] != 'V')
        {
            return null;
        }

        var info = new InfoSuboru { Velkost = subor.Length, Format = "Flash Video", VerziaFormatu = "verzia " + data[3].ToString(CultureInfo.InvariantCulture) };
        var meta = new Dictionary<string, object?>(StringComparer.Ordinal);
        InfoStopy? video = null, zvuk = null;
        var pos = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(5)) + 4;
        var tagov = 0;
        while (pos + 11 <= data.Length && tagov++ < 2000 && (video is null || zvuk is null || meta.Count == 0))
        {
            var typ = data[pos] & 0x1F;
            var velkost = (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            var obsah = pos + 11;
            if (obsah + velkost > data.Length)
            {
                break;
            }

            var telo = data.AsSpan(obsah, velkost);
            if (typ == 18 && meta.Count == 0)
            {
                Metadata(telo, meta);
            }
            else if (typ == 9 && velkost > 1)
            {
                video ??= new InfoStopy { Druh = DruhStopy.Video, JeVideo = true, Id = 0, Predvolena = true };
                Obraz(telo, video);
            }
            else if (typ == 8 && velkost > 1)
            {
                zvuk ??= new InfoStopy { Druh = DruhStopy.Zvuk, Id = 1, Predvolena = true };
                Zvuk(telo, zvuk);
            }

            pos = obsah + velkost + 4;
        }

        double? Cislo(string kluc) => meta.TryGetValue(kluc, out var v) && v is double d && d > 0 && !double.IsInfinity(d) ? d : null;
        info.DlzkaSekund = Cislo("duration") ?? Posledny(subor);
        info.Aplikacia = meta.TryGetValue("encoder", out var e) && e is string koder ? koder : meta.TryGetValue("metadatacreator", out var m) && m is string tvorca ? tvorca : null;
        if (video is not null)
        {
            video.Sirka ??= Cislo("width") is { } w ? (int)w : null;
            video.Vyska ??= Cislo("height") is { } h ? (int)h : null;
            video.Snimky ??= Cislo("framerate");
            video.DatovyTok ??= Cislo("videodatarate") is { } vt ? (long)(vt * 1000) : null;
            if (video.Sirka > 0 && video.Vyska > 0)
            {
                video.PomerStran ??= video.Sirka.Value / (double)video.Vyska.Value;
            }

            info.Stopy.Add(video);
        }

        if (zvuk is not null)
        {
            zvuk.DatovyTok ??= Cislo("audiodatarate") is { } at ? (long)(at * 1000) : null;
            info.Stopy.Add(zvuk);
        }

        foreach (var s in info.Stopy)
        {
            s.DlzkaSekund = info.DlzkaSekund;
            if (s.DatovyTok is { } t && info.DlzkaSekund is > 0)
            {
                s.VelkostStreamu = (long)(t / 8.0 * info.DlzkaSekund.Value);
                s.Odhad = true;
            }
        }

        return info.Stopy.Count == 0 ? null : info;
    }

    /// <summary>A video tag: the codec, and its configuration (AVC, HEVC, AV1 – also of the enhanced FLV).</summary>
    private static void Obraz(ReadOnlySpan<byte> t, InfoStopy s)
    {
        if ((t[0] & 0x80) != 0 && t.Length >= 5)
        {
            // enhanced FLV: packet type in the low bits, the codec as a FourCC
            var typPaketu = t[0] & 0x0F;
            var fourcc = Encoding.ASCII.GetString(t.Slice(1, 4));
            (s.Kodek, s.Format, s.FormatInfo) = fourcc switch
            {
                "hvc1" => ("hevc", "HEVC", "High Efficiency Video Coding"),
                "av01" => ("av1", "AV1", "AOMedia Video 1"),
                "vp09" => ("vp9", "VP9", (string?)null),
                "avc1" => ("h264", "AVC", "Advanced Video Codec"),
                _ => (fourcc.ToLowerInvariant(), fourcc, null),
            };
            s.IdKodeku = fourcc;
            if (typPaketu == 0 && t.Length > 5)
            {
                Konfiguracia(t[5..], s);
            }

            return;
        }

        var kodek = t[0] & 0x0F;
        s.IdKodeku = kodek.ToString(CultureInfo.InvariantCulture);
        (s.Kodek, s.Format, s.FormatInfo) = kodek switch
        {
            2 => ("flv1", "Sorenson Spark", "H.263"),
            3 => ("flashsv", "Screen Video", (string?)null),
            4 => ("vp6f", "VP6", "On2 VP6"),
            5 => ("vp6a", "VP6", "On2 VP6 s alfa kanálom"),
            6 => ("flashsv2", "Screen Video 2", null),
            7 => ("h264", "AVC", "Advanced Video Codec"),
            12 => ("hevc", "HEVC", "High Efficiency Video Coding"),
            _ => ($"flv{kodek}", kodek.ToString(CultureInfo.InvariantCulture), null),
        };
        if (kodek is 7 or 12 && t.Length > 5 && t[1] == 0)
        {
            Konfiguracia(t[5..], s);
        }
    }

    private static void Konfiguracia(ReadOnlySpan<byte> konfiguracia, InfoStopy s)
    {
        switch (s.Kodek)
        {
            case "h264":
                KodekyVidea.Avc(konfiguracia, s);
                break;
            case "hevc":
                KodekyVidea.Hevc(konfiguracia, s);
                break;
            case "av1":
                KodekyVidea.Av1(konfiguracia, s);
                break;
        }
    }

    /// <summary>An audio tag: format, rate, size and channels, and for AAC its AudioSpecificConfig.</summary>
    private static void Zvuk(ReadOnlySpan<byte> t, InfoStopy s)
    {
        var format = t[0] >> 4;
        if (s.Kodek is null)
        {
            s.IdKodeku = format.ToString(CultureInfo.InvariantCulture);
            (s.Kodek, s.Format, s.FormatInfo, s.ObchodnyNazov) = format switch
            {
                0 or 3 => ("pcm_s16le", "PCM", (string?)null, (string?)null),
                1 => ("adpcm_swf", "ADPCM", "Flash ADPCM", null),
                2 or 14 => ("mp3", "MPEG Audio", "MPEG-1 Layer 3", "MP3"),
                4 or 5 or 6 => ("nellymoser", "Nellymoser", null, null),
                7 => ("pcm_alaw", "A-law", null, null),
                8 => ("pcm_mulaw", "µ-law", null, null),
                10 => ("aac", "AAC", "Advanced Audio Codec", null),
                11 => ("speex", "Speex", null, null),
                _ => ($"flv{format}", format.ToString(CultureInfo.InvariantCulture), null, null),
            };
            s.Vzorkovanie = ((t[0] >> 2) & 3) switch { 0 => 5512, 1 => 11025, 2 => 22050, _ => 44100 };
            s.Kanaly = (t[0] & 1) == 1 ? 2 : 1;
            if (s.Format == "PCM")
            {
                s.BitovaHlbka = (t[0] & 2) != 0 ? 16 : 8;
            }
        }

        if (format == 10 && t.Length >= 4 && t[1] == 0)
        {
            var asc = t[2..];
            s.Profil = KodekyVidea.ProfilAac(asc);
            var index = ((asc[0] & 7) << 1) | (asc[1] >> 7);
            int[] frekvencie = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
            if (index < frekvencie.Length)
            {
                s.Vzorkovanie = frekvencie[index];
            }

            var kanaly = (asc[1] >> 3) & 15;
            if (kanaly is > 0 and < 8)
            {
                s.Kanaly = kanaly == 7 ? 8 : kanaly;
            }
        }
        else if (format is 2 or 14 && t.Length > 4)
        {
            ElementarneToky.MpegAudio(t[1..], s);
        }
    }

    /// <summary>The length from the time stamp of the last tag (the last 4 bytes give its size).</summary>
    private static double? Posledny(Stream subor)
    {
        if (subor.Length < 32)
        {
            return null;
        }

        Span<byte> b = stackalloc byte[4];
        subor.Position = subor.Length - 4;
        subor.ReadExactly(b);
        var velkost = BinaryPrimitives.ReadUInt32BigEndian(b);
        if (velkost < 11 || velkost > subor.Length - 4)
        {
            return null;
        }

        Span<byte> tag = stackalloc byte[8];
        subor.Position = subor.Length - 4 - velkost;
        subor.ReadExactly(tag);
        var cas = ((uint)tag[7] << 24) | ((uint)tag[4] << 16) | ((uint)tag[5] << 8) | tag[6];
        return cas > 0 ? cas / 1000.0 : null;
    }

    // ---------------------------------------------------------------- AMF0

    /// <summary>The script data tag "onMetaData": its object or ECMA array of numbers, strings and flags.</summary>
    private static void Metadata(ReadOnlySpan<byte> t, Dictionary<string, object?> meta)
    {
        var i = 0;
        if (Hodnota(t, ref i, 0) is not "onMetaData" || i >= t.Length)
        {
            return;
        }

        if (Hodnota(t, ref i, 0) is Dictionary<string, object?> objekt)
        {
            foreach (var (k, v) in objekt)
            {
                meta[k] = v;
            }
        }
    }

    private static object? Hodnota(ReadOnlySpan<byte> t, ref int i, int hlbka)
    {
        if (i >= t.Length || hlbka > 8)
        {
            i = t.Length;
            return null;
        }

        var typ = t[i++];
        switch (typ)
        {
            case 0 when i + 8 <= t.Length:
                var cislo = BinaryPrimitives.ReadDoubleBigEndian(t[i..]);
                i += 8;
                return cislo;
            case 1 when i < t.Length:
                return t[i++] != 0;
            case 2:
                return Retazec(t, ref i, dlhy: false);
            case 12:
                return Retazec(t, ref i, dlhy: true);
            case 3 or 8:
                if (typ == 8)
                {
                    i += 4;
                }

                var objekt = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (i + 3 <= t.Length && objekt.Count < 512)
                {
                    if (t[i] == 0 && t[i + 1] == 0 && t[i + 2] == 9)
                    {
                        i += 3;
                        break;
                    }

                    var kluc = Retazec(t, ref i, dlhy: false);
                    objekt[kluc] = Hodnota(t, ref i, hlbka + 1);
                }

                return objekt;
            case 10 when i + 4 <= t.Length:
                var pocet = BinaryPrimitives.ReadUInt32BigEndian(t[i..]);
                i += 4;
                for (var n = 0; n < pocet && i < t.Length && n < 100_000; n++)
                {
                    Hodnota(t, ref i, hlbka + 1);
                }

                return null;
            case 11:
                i += 10;
                return null;
            case 5 or 6:
                return null;
            default:
                i = t.Length; // a type it does not follow: stop
                return null;
        }
    }

    private static string Retazec(ReadOnlySpan<byte> t, ref int i, bool dlhy)
    {
        var dlzkaPola = dlhy ? 4 : 2;
        if (i + dlzkaPola > t.Length)
        {
            i = t.Length;
            return "";
        }

        var dlzka = dlhy ? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(t[i..]), int.MaxValue) : BinaryPrimitives.ReadUInt16BigEndian(t[i..]);
        i += dlzkaPola;
        if (i + dlzka > t.Length)
        {
            i = t.Length;
            return "";
        }

        var text = Encoding.UTF8.GetString(t.Slice(i, dlzka));
        i += dlzka;
        return text;
    }
}
