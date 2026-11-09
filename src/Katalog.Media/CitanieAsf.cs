using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>
/// KATALOG's own reader of ASF files (WMV, WMA, ASF): only the header object at the start is read – file properties
/// (length, preroll, maximum rate), stream properties (WAVEFORMATEX, BITMAPINFOHEADER), the average rate of each
/// stream, languages of the header extension, title and the encoder.
/// </summary>
public static class CitanieAsf
{
    private const int NajviacHlavicka = 4 << 20;

    private static readonly Guid Hlavicka = new("75B22630-668E-11CF-A6D9-00AA0062CE6C");
    private static readonly Guid VlastnostiSuboru = new("8CABDCA1-A947-11CF-8EE4-00C00C205365");
    private static readonly Guid VlastnostiPrudu = new("B7DC0791-A9B7-11CF-8EE6-00C00C205365");
    private static readonly Guid RozsirenieHlavicky = new("5FBF03B5-A92E-11CF-8EE3-00C00C205365");
    private static readonly Guid RozsireneVlastnostiPrudu = new("14E6A5CB-C672-4332-8399-A96952065B5A");
    private static readonly Guid ZoznamJazykov = new("7C4346A9-EFE0-4BFC-B229-393EDE415C85");
    private static readonly Guid Popis = new("75B22633-668E-11CF-A6D9-00AA0062CE6C");
    private static readonly Guid RozsirenyPopis = new("D2D0A440-E307-11D2-97F0-00A0C95EA850");
    private static readonly Guid TokyPrudov = new("7BF875CE-468D-11D1-8D82-006097C9A2B2");
    private static readonly Guid Zvuk = new("F8699E40-5B4D-11CF-A8FD-00805F5C442B");
    private static readonly Guid Video = new("BC19EFC0-5B4D-11CF-A8FD-00805F5C442B");

    /// <summary>The first 16 bytes of an ASF file (the GUID of its header object as stored).</summary>
    internal static readonly byte[] Znacka = Hlavicka.ToByteArray();

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
        var hlava = new byte[30];
        subor.Position = 0;
        if (subor.ReadAtLeast(hlava, 30, throwOnEndOfStream: false) < 30 || new Guid(hlava.AsSpan(0, 16)) != Hlavicka)
        {
            return null;
        }

        var velkost = (long)BinaryPrimitives.ReadUInt64LittleEndian(hlava.AsSpan(16));
        if (velkost is < 30 or > NajviacHlavicka)
        {
            return null;
        }

        var data = new byte[(int)Math.Min(velkost, subor.Length)];
        subor.Position = 0;
        subor.ReadExactly(data);
        var info = new InfoSuboru { Velkost = subor.Length, Format = "Windows Media", VerziaFormatu = "ASF" };
        var prudy = new SortedDictionary<int, InfoStopy>();
        var jazyky = new List<string>();
        var jazykPrudu = new Dictionary<int, int>();
        var toky = new Dictionary<int, long>();
        var snimkyPrudu = new Dictionary<int, double>();
        double preroll = 0;
        foreach (var (guid, obsah) in Objekty(data.AsSpan(30)))
        {
            if (guid == VlastnostiSuboru && obsah.Length >= 80)
            {
                var trvanie = BinaryPrimitives.ReadUInt64LittleEndian(obsah.AsSpan(40));
                preroll = BinaryPrimitives.ReadUInt64LittleEndian(obsah.AsSpan(56)) / 1000.0;
                var vytvorene = BinaryPrimitives.ReadInt64LittleEndian(obsah.AsSpan(24));
                if (trvanie > 0)
                {
                    info.DlzkaSekund = Math.Max(0, (trvanie / 1e7) - preroll);
                }

                if (vytvorene > new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc() && vytvorene < DateTime.MaxValue.ToFileTimeUtc())
                {
                    info.DatumKodovania = DateTime.FromFileTimeUtc(vytvorene);
                }
            }
            else if (guid == VlastnostiPrudu)
            {
                if (Prud(obsah) is { } p)
                {
                    prudy[p.Id] = p;
                }
            }
            else if (guid == RozsirenieHlavicky && obsah.Length > 22)
            {
                foreach (var (eguid, eobsah) in Objekty(obsah.AsSpan(22)))
                {
                    if (eguid == ZoznamJazykov && eobsah.Length >= 2)
                    {
                        var pocet = BinaryPrimitives.ReadUInt16LittleEndian(eobsah);
                        var i = 2;
                        for (var j = 0; j < pocet && i < eobsah.Length; j++)
                        {
                            var dlzka = eobsah[i];
                            jazyky.Add(i + 1 + dlzka <= eobsah.Length ? Encoding.Unicode.GetString(eobsah, i + 1, dlzka).TrimEnd('\0') : "");
                            i += 1 + dlzka;
                        }
                    }
                    else if (eguid == RozsireneVlastnostiPrudu && eobsah.Length >= 64)
                    {
                        var cislo = BinaryPrimitives.ReadUInt16LittleEndian(eobsah.AsSpan(48));
                        jazykPrudu[cislo] = BinaryPrimitives.ReadUInt16LittleEndian(eobsah.AsSpan(50));
                        var casSnimky = BinaryPrimitives.ReadUInt64LittleEndian(eobsah.AsSpan(52));
                        if (casSnimky > 0)
                        {
                            snimkyPrudu[cislo] = 1e7 / casSnimky;
                        }

                        // an embedded stream properties object (after the names and payload extensions)
                        var mien = BinaryPrimitives.ReadUInt16LittleEndian(eobsah.AsSpan(60));
                        var rozsireni = BinaryPrimitives.ReadUInt16LittleEndian(eobsah.AsSpan(62));
                        var k = 64;
                        for (var n = 0; n < mien && k + 4 <= eobsah.Length; n++)
                        {
                            k += 4 + BinaryPrimitives.ReadUInt16LittleEndian(eobsah.AsSpan(k + 2));
                        }

                        for (var n = 0; n < rozsireni && k + 22 <= eobsah.Length; n++)
                        {
                            k += 22 + (int)BinaryPrimitives.ReadUInt32LittleEndian(eobsah.AsSpan(k + 18));
                        }

                        if (k + 24 <= eobsah.Length && new Guid(eobsah.AsSpan(k, 16)) == VlastnostiPrudu)
                        {
                            var dlzka = (int)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(eobsah.AsSpan(k + 16)), (ulong)(eobsah.Length - k));
                            if (dlzka > 24 && Prud(eobsah.AsSpan(k + 24, dlzka - 24).ToArray()) is { } vnoreny)
                            {
                                prudy[vnoreny.Id] = vnoreny;
                            }
                        }
                    }
                }
            }
            else if (guid == Popis && obsah.Length >= 10)
            {
                var dlzkaNazvu = BinaryPrimitives.ReadUInt16LittleEndian(obsah);
                if (10 + dlzkaNazvu <= obsah.Length && dlzkaNazvu > 2)
                {
                    info.Nazov = Encoding.Unicode.GetString(obsah, 10, dlzkaNazvu).TrimEnd('\0');
                }
            }
            else if (guid == RozsirenyPopis)
            {
                RozsirenyPopisObsah(obsah, info);
            }
            else if (guid == TokyPrudov && obsah.Length >= 2)
            {
                var pocet = BinaryPrimitives.ReadUInt16LittleEndian(obsah);
                for (var j = 0; j < pocet && 2 + (6 * j) + 6 <= obsah.Length; j++)
                {
                    toky[BinaryPrimitives.ReadUInt16LittleEndian(obsah.AsSpan(2 + (6 * j))) & 0x7F] = BinaryPrimitives.ReadUInt32LittleEndian(obsah.AsSpan(4 + (6 * j)));
                }
            }
        }

        foreach (var (cislo, s) in prudy)
        {
            if (jazykPrudu.TryGetValue(cislo, out var index) && index < jazyky.Count && jazyky[index].Length >= 2)
            {
                s.Jazyk = Jazyky.Normalizuj(jazyky[index].Split('-')[0]);
            }

            if (toky.TryGetValue(cislo, out var tok) && tok > 0)
            {
                s.DatovyTok ??= tok;
            }

            if (s.JeVideo && snimkyPrudu.TryGetValue(cislo, out var fps))
            {
                s.Snimky = fps;
            }

            s.DlzkaSekund = info.DlzkaSekund;
            if (s.DatovyTok is { } t && info.DlzkaSekund is > 0)
            {
                s.VelkostStreamu = (long)(t / 8.0 * info.DlzkaSekund.Value);
                s.Odhad = true;
            }

            info.Stopy.Add(s);
        }

        return info.Stopy.Count == 0 ? null : info;
    }

    /// <summary>A stream properties object: its number, kind and the codec's structure.</summary>
    private static InfoStopy? Prud(byte[] o)
    {
        if (o.Length < 54)
        {
            return null;
        }

        var typ = new Guid(o.AsSpan(0, 16));
        var dlzkaTypu = (int)BinaryPrimitives.ReadUInt32LittleEndian(o.AsSpan(40));
        var cislo = BinaryPrimitives.ReadUInt16LittleEndian(o.AsSpan(48)) & 0x7F;
        var spec = o.AsSpan(54, Math.Min(dlzkaTypu, o.Length - 54));
        var s = new InfoStopy { Id = cislo, IdToku = cislo, Predvolena = true };
        if (typ == Zvuk && spec.Length >= 16)
        {
            s.Druh = DruhStopy.Zvuk;
            var znacka = BinaryPrimitives.ReadUInt16LittleEndian(spec);
            s.Kanaly = BinaryPrimitives.ReadUInt16LittleEndian(spec[2..]);
            s.Vzorkovanie = BinaryPrimitives.ReadUInt32LittleEndian(spec[4..]);
            s.DatovyTok = BinaryPrimitives.ReadUInt32LittleEndian(spec[8..]) * 8L;
            var bity = BinaryPrimitives.ReadUInt16LittleEndian(spec[14..]);
            s.IdKodeku = "0x" + znacka.ToString("X4", CultureInfo.InvariantCulture);
            (s.Kodek, s.Format, s.FormatInfo) = znacka switch
            {
                0x0160 => ("wmav1", "WMA", "Windows Media Audio 1"),
                0x0161 => ("wmav2", "WMA", "Windows Media Audio 2"),
                0x0162 => ("wmapro", "WMA", "Windows Media Audio 9 Professional"),
                0x0163 => ("wmalossless", "WMA", "Windows Media Audio 9 Lossless"),
                0x000A => ("wmavoice", "WMA", "Windows Media Audio Voice"),
                0x0055 => ("mp3", "MPEG Audio", "MPEG-1 Layer 3"),
                0x2000 => ("ac3", "AC-3", "Audio Coding 3"),
                0x0001 => ("pcm_s16le", "PCM", "Little Endian"),
                _ => ($"0x{znacka:X4}", $"0x{znacka:X4}", null),
            };
            if (s.Format is "PCM" || s.Kodek == "wmalossless")
            {
                s.BitovaHlbka = bity;
            }
        }
        else if (typ == Video && spec.Length >= 11 + 40)
        {
            s.Druh = DruhStopy.Video;
            s.JeVideo = true;
            s.Sirka = (int)BinaryPrimitives.ReadUInt32LittleEndian(spec);
            s.Vyska = (int)BinaryPrimitives.ReadUInt32LittleEndian(spec[4..]);
            var bitmap = spec[11..];
            var fourcc = Encoding.ASCII.GetString(bitmap.Slice(16, 4));
            s.IdKodeku = fourcc;
            (s.Kodek, s.Format, s.FormatInfo) = fourcc.ToUpperInvariant() switch
            {
                "WMV1" => ("wmv1", "WMV1", "Windows Media Video 7"),
                "WMV2" => ("wmv2", "WMV2", "Windows Media Video 8"),
                "WMV3" => ("wmv3", "VC-1", "Windows Media Video 9"),
                "WMVA" or "WVC1" => ("vc1", "VC-1", "Windows Media Video 9 Advanced Profile"),
                "WVP2" or "WMVP" => ("wmv3image", "WMV", "Windows Media Video 9 Image"),
                "MP43" or "MP42" or "MP4S" => ("msmpeg4v3", "MPEG-4 Visual", "MS MPEG-4"),
                "H264" or "AVC1" => ("h264", "AVC", "Advanced Video Codec"),
                _ => (fourcc.Trim('\0', ' ').ToLowerInvariant(), fourcc.Trim('\0', ' '), null),
            };
            s.FarebnyPriestor = "YUV";
            s.Podvzorkovanie = "4:2:0";
            s.BitovaHlbka = 8;
            s.Prekladane ??= false;
            if (s.Sirka > 0 && s.Vyska > 0)
            {
                s.PomerStran = s.Sirka.Value / (double)s.Vyska.Value;
            }

            // VC-1 Advanced: the sequence header in the codec data
            if (s.Kodek == "vc1" && bitmap.Length > 40)
            {
                ElementarneToky.Vc1(bitmap[40..], s);
            }
            else if (s.Kodek == "wmv3" && bitmap.Length >= 41)
            {
                // the struct C of the Simple and Main profiles: the profile in its top bits
                s.Profil = (bitmap[40] >> 6) switch { 0 => "Simple", 1 => "Main", _ => "Complex" };
            }
        }
        else
        {
            return null;
        }

        return s;
    }

    private static void RozsirenyPopisObsah(byte[] o, InfoSuboru info)
    {
        if (o.Length < 2)
        {
            return;
        }

        var pocet = BinaryPrimitives.ReadUInt16LittleEndian(o);
        var i = 2;
        for (var j = 0; j < pocet && i + 6 <= o.Length; j++)
        {
            var dlzkaMena = BinaryPrimitives.ReadUInt16LittleEndian(o.AsSpan(i));
            if (i + 2 + dlzkaMena + 4 > o.Length)
            {
                return;
            }

            var meno = Encoding.Unicode.GetString(o, i + 2, dlzkaMena).TrimEnd('\0');
            i += 2 + dlzkaMena;
            var typ = BinaryPrimitives.ReadUInt16LittleEndian(o.AsSpan(i));
            var dlzka = BinaryPrimitives.ReadUInt16LittleEndian(o.AsSpan(i + 2));
            i += 4;
            if (i + dlzka > o.Length)
            {
                return;
            }

            if (typ == 0 && meno is "WM/ToolName" or "WM/EncodingSettings" or "WM/Encoder")
            {
                var hodnota = Encoding.Unicode.GetString(o, i, dlzka).TrimEnd('\0');
                if (hodnota.Length > 0)
                {
                    info.Aplikacia ??= hodnota;
                }
            }

            i += dlzka;
        }
    }

    /// <summary>The objects inside an object read into memory: a GUID, a 64-bit size, the content.</summary>
    private static IEnumerable<(Guid Guid, byte[] Obsah)> Objekty(ReadOnlySpan<byte> data)
    {
        var zoznam = new List<(Guid, byte[])>();
        var pos = 0;
        while (pos + 24 <= data.Length && zoznam.Count < 1000)
        {
            var guid = new Guid(data.Slice(pos, 16));
            var velkost = BinaryPrimitives.ReadUInt64LittleEndian(data[(pos + 16)..]);
            if (velkost < 24 || (ulong)pos + velkost > (ulong)data.Length)
            {
                break;
            }

            zoznam.Add((guid, data.Slice(pos + 24, (int)velkost - 24).ToArray()));
            pos += (int)velkost;
        }

        return zoznam;
    }
}
