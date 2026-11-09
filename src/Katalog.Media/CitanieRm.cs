using System.Buffers.Binary;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of RealMedia (RM, RMVB): the headers before the data – the properties of the file (length,
/// rates), the properties of each stream (RealVideo 1–4 with picture size and frame rate, RealAudio Cook, ATRAC, AAC,
/// AC-3 …) and the title.
/// </summary>
public static class CitanieRm
{
    private const int NajviacHlavicky = 1 << 20;

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
        var data = new byte[(int)Math.Min(NajviacHlavicky, subor.Length)];
        subor.Position = 0;
        subor.ReadExactly(data);
        if (data.Length < 18 || Encoding.ASCII.GetString(data, 0, 4) != ".RMF")
        {
            return null;
        }

        var info = new InfoSuboru { Velkost = subor.Length, Format = "RealMedia" };
        var pos = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        while (pos + 10 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, pos, 4);
            var velkost = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
            if (velkost < 10 || id == "DATA")
            {
                break;
            }

            var obsah = data.AsSpan(pos + 10, Math.Min(velkost - 10, data.Length - pos - 10));
            switch (id)
            {
                case "PROP" when obsah.Length >= 40:
                    info.DlzkaSekund = BinaryPrimitives.ReadUInt32BigEndian(obsah[20..]) / 1000.0;
                    break;
                case "MDPR":
                    if (Prud(obsah) is { } s)
                    {
                        info.Stopy.Add(s);
                    }

                    break;
                case "CONT" when obsah.Length >= 2:
                    var dlzkaNazvu = BinaryPrimitives.ReadUInt16BigEndian(obsah);
                    if (dlzkaNazvu > 0 && 2 + dlzkaNazvu <= obsah.Length)
                    {
                        info.Nazov = Encoding.Latin1.GetString(obsah.Slice(2, dlzkaNazvu)).TrimEnd('\0');
                    }

                    break;
            }

            pos += velkost;
        }

        foreach (var s in info.Stopy)
        {
            s.DlzkaSekund ??= info.DlzkaSekund;
            if (s.DatovyTok is { } t && s.DlzkaSekund is > 0)
            {
                s.VelkostStreamu = (long)(t / 8.0 * s.DlzkaSekund.Value);
                s.Odhad = true;
            }
        }

        return info.Stopy.Count == 0 ? null : info;
    }

    /// <summary>A media properties header: its rates, length, MIME type and the codec's own data.</summary>
    private static InfoStopy? Prud(ReadOnlySpan<byte> m)
    {
        if (m.Length < 31)
        {
            return null;
        }

        var cislo = BinaryPrimitives.ReadUInt16BigEndian(m);
        var priemer = BinaryPrimitives.ReadUInt32BigEndian(m[6..]);
        var trvanie = BinaryPrimitives.ReadUInt32BigEndian(m[26..]);
        var i = 30;
        var nazov = Encoding.Latin1.GetString(m.Slice(i + 1, Math.Min(m[i], m.Length - i - 1)));
        i += 1 + m[i];
        if (i >= m.Length)
        {
            return null;
        }

        var mime = Encoding.Latin1.GetString(m.Slice(i + 1, Math.Min(m[i], m.Length - i - 1)));
        i += 1 + m[i];
        if (i + 4 > m.Length || mime.StartsWith("logical-", StringComparison.Ordinal))
        {
            return null;
        }

        var dlzka = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(m[i..]), (uint)(m.Length - i - 4));
        var spec = m.Slice(i + 4, dlzka);
        var s = new InfoStopy
        {
            Id = cislo,
            IdToku = cislo,
            DatovyTok = priemer > 0 ? priemer : null,
            DlzkaSekund = trvanie > 0 ? trvanie / 1000.0 : null,
            Nazov = string.IsNullOrWhiteSpace(nazov) ? null : nazov.Trim(),
            Predvolena = true,
        };
        if (spec.Length >= 34 && spec.Slice(4, 4).SequenceEqual("VIDO"u8))
        {
            var fourcc = Encoding.ASCII.GetString(spec.Slice(8, 4));
            s.Druh = DruhStopy.Video;
            s.JeVideo = true;
            s.IdKodeku = fourcc;
            s.Sirka = BinaryPrimitives.ReadUInt16BigEndian(spec[12..]);
            s.Vyska = BinaryPrimitives.ReadUInt16BigEndian(spec[14..]);
            var fps = BinaryPrimitives.ReadUInt32BigEndian(spec[22..]) / 65536.0;
            if (fps is > 0 and < 500)
            {
                s.Snimky = fps;
            }

            (s.Kodek, s.Format, s.FormatInfo) = fourcc switch
            {
                "RV10" => ("rv10", "RealVideo 1", "H.263"),
                "RV20" => ("rv20", "RealVideo 2", "H.263+"),
                "RV30" => ("rv30", "RealVideo 3", "RealVideo 8"),
                "RV40" => ("rv40", "RealVideo 4", "RealVideo 9/10"),
                "RV60" => ("rv60", "RealVideo 6", "RealVideo HD"),
                _ => (fourcc.ToLowerInvariant(), fourcc, (string?)null),
            };
            s.FarebnyPriestor = "YUV";
            s.Podvzorkovanie = "4:2:0";
            s.BitovaHlbka = 8;
            s.Prekladane = false;
            if (s.Sirka > 0 && s.Vyska > 0)
            {
                s.PomerStran = s.Sirka.Value / (double)s.Vyska.Value;
            }
        }
        else if (spec.Length >= 6 && spec[0] == '.' && spec[1] == 'r' && spec[2] == 'a' && spec[3] == 0xFD)
        {
            s.Druh = DruhStopy.Zvuk;
            RealAudio(spec, s);
        }
        else if (mime.StartsWith("audio/", StringComparison.Ordinal))
        {
            s.Druh = DruhStopy.Zvuk;
            s.Kodek = mime[6..];
            s.Format = mime[6..];
        }
        else
        {
            return null;
        }

        return s;
    }

    /// <summary>The RealAudio header (.ra version 3, 4 or 5): sample rate, channels and the codec's FourCC.</summary>
    private static void RealAudio(ReadOnlySpan<byte> r, InfoStopy s)
    {
        var verzia = BinaryPrimitives.ReadUInt16BigEndian(r[4..]);
        string? fourcc = null;
        if (verzia == 3)
        {
            s.Kanaly = 1;
            s.Vzorkovanie = 8000;
            fourcc = "lpcJ";
        }
        else if (verzia is 4 or 5 && r.Length >= 70)
        {
            var i = 6 + 2 + 4 + 4 + 2 + 4 + 2 + 4 + 4;
            var zaMinutu = BinaryPrimitives.ReadUInt32BigEndian(r[i..]);
            if (verzia == 4 && zaMinutu > 0)
            {
                s.DatovyTok ??= zaMinutu * 8L / 60;
            }

            i += 4 + 4 + 2 + 2 + 2 + 2;
            if (verzia == 5)
            {
                i += 6;
            }

            s.Vzorkovanie = BinaryPrimitives.ReadUInt16BigEndian(r[i..]);
            i += 2 + 4;
            s.Kanaly = BinaryPrimitives.ReadUInt16BigEndian(r[i..]);
            i += 2;
            if (verzia == 5 && i + 8 <= r.Length)
            {
                fourcc = Encoding.ASCII.GetString(r.Slice(i + 4, 4));
            }
            else if (i < r.Length)
            {
                // two length-prefixed strings: the interleaver, then the codec
                i += 1 + r[i];
                if (i < r.Length && r[i] == 4 && i + 5 <= r.Length)
                {
                    fourcc = Encoding.ASCII.GetString(r.Slice(i + 1, 4));
                }
            }
        }

        s.IdKodeku = fourcc;
        (s.Kodek, s.Format, s.ObchodnyNazov) = fourcc switch
        {
            "cook" => ("cook", "Cooker", "RealAudio G2 (Cook)"),
            "atrc" => ("atrac3", "ATRAC3", "Sony ATRAC3"),
            "sipr" => ("sipr", "Sipro", "RealAudio Sipro"),
            "raac" or "racp" => ("aac", "AAC", fourcc == "racp" ? "HE-AAC" : null),
            "dnet" => ("ac3", "AC-3", "Dolby Digital"),
            "28_8" => ("ra_288", "RealAudio 2", "RealAudio 28.8"),
            "lpcJ" => ("ra_144", "RealAudio 1", "RealAudio 14.4"),
            "ralf" => ("ralf", "RealAudio Lossless", null),
            _ => (fourcc?.ToLowerInvariant() ?? "realaudio", fourcc ?? "RealAudio", (string?)null),
        };
    }
}
