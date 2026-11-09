using System.Globalization;
using Katalog.Jadro.Model;

namespace Katalog.Media;

/// <summary>What KATALOG's own reader found in a file: the whole file and its tracks.</summary>
public sealed class InfoSuboru
{
    public string Format { get; set; } = "";

    public string? VerziaFormatu { get; set; }

    public long Velkost { get; set; }

    public double? DlzkaSekund { get; set; }

    public string? Nazov { get; set; }

    public DateTime? DatumKodovania { get; set; }

    public string? Aplikacia { get; set; }

    public string? Kniznica { get; set; }

    public List<InfoStopy> Stopy { get; } = [];
}

/// <summary>A track of a file as the own reader found it (null = not known).</summary>
public sealed class InfoStopy
{
    public DruhStopy? Druh { get; set; }

    /// <summary>True for the picture of the film (not a cover image).</summary>
    public bool JeVideo { get; set; }

    public int Id { get; set; }

    public ulong Uid { get; set; }

    /// <summary>The codec as KATALOG names it elsewhere (ffprobe's names: "hevc", "eac3", "subrip").</summary>
    public string? Kodek { get; set; }

    /// <summary>The format as MediaInfo writes it ("HEVC", "E-AC-3", "UTF-8").</summary>
    public string? Format { get; set; }

    public string? FormatInfo { get; set; }

    public string? ObchodnyNazov { get; set; }

    public string? Profil { get; set; }

    public string? IdKodeku { get; set; }

    public double? DlzkaSekund { get; set; }

    public long? DatovyTok { get; set; }

    public bool? KonstantnyTok { get; set; }

    public long? VelkostStreamu { get; set; }

    public long? PocetSnimok { get; set; }

    public int? Sirka { get; set; }

    public int? Vyska { get; set; }

    public double? PomerStran { get; set; }

    public double? Snimky { get; set; }

    public bool? KonstantneSnimky { get; set; }

    public string? FarebnyPriestor { get; set; }

    public string? Podvzorkovanie { get; set; }

    public int? BitovaHlbka { get; set; }

    public bool? Prekladane { get; set; }

    public string? FarebnyRozsah { get; set; }

    public string? PrimarneFarby { get; set; }

    public string? Prenos { get; set; }

    public string? Matica { get; set; }

    public Hdr Hdr { get; set; }

    public string? HdrPopis { get; set; }

    public int? Kanaly { get; set; }

    public string? RozlozenieKanalov { get; set; }

    public double? Vzorkovanie { get; set; }

    public string? Jazyk { get; set; }

    public string? Nazov { get; set; }

    public bool Predvolena { get; set; }

    public bool Vynutena { get; set; }

    public bool Sdh { get; set; }

    /// <summary>The maximum bit rate the stream declares (MPEG-2 video).</summary>
    public long? MaxTok { get; set; }

    /// <summary>The PID of a transport stream, or the stream id of a program stream.</summary>
    public int? IdToku { get; set; }

    /// <summary>The size is estimated from parts of the file (TS, PS), not known exactly.</summary>
    public bool Odhad { get; set; }

    /// <summary>The data rate comes from the estimated size (not from the stream itself).</summary>
    public bool OdhadToku { get; set; }

    /// <summary>The dialogue level of Dolby Digital (dialnorm, dB).</summary>
    public int? Dialogy { get; set; }

    /// <summary>Dolby Atmos (TrueHD with its 16-channel presentation, E-AC-3 with JOC).</summary>
    public bool Atmos { get; set; }

    /// <summary>HDR10+ (SMPTE ST 2094-40) metadata in the picture.</summary>
    public bool Hdr10Plus { get; set; }

    /// <summary>The length of NAL unit sizes in the frames of HEVC and AVC (from hvcC/avcC).</summary>
    internal int DlzkaNal { get; set; } = 4;

    /// <summary>The first frame of the track, when the reader fetched it for <see cref="AnalyzaRamcov"/>.</summary>
    internal byte[]? PrvyRamec { get; set; }

    /// <summary>MP4: where the first sample lies and how big it is.</summary>
    internal (long Pozicia, long Velkost)? PrvaVzorka { get; set; }

    /// <summary>Whether the reader wants the first frame: audio whose frame says more, and HEVC (HDR10+, Dolby Vision).</summary>
    internal bool ChceRamec => Kodek is "ac3" or "eac3" or "truehd" or "dts" || (JeVideo && Kodek == "hevc");

    /// <summary>Applies what the first frame says, once.</summary>
    internal void AnalyzujRamec()
    {
        if (PrvyRamec is { } ramec && JeVideo && Kodek == "hevc")
        {
            AnalyzaRamcov.ObrazHevc(ramec, DlzkaNal, this);
        }
        else if (PrvyRamec is { } zvuk)
        {
            AnalyzaRamcov.Zvuk(zvuk, this);
        }

        PrvyRamec = null;
        if (Atmos && Kodek is "truehd" or "eac3")
        {
            Kodek += " atmos";
        }
    }
}

/// <summary>What the own reader gives the library: the data ffprobe would give, and the details for the detail panel.</summary>
public sealed record VysledokKontajnera(MedialneUdaje Udaje, IReadOnlyList<Stopa> Stopy, IReadOnlyList<SekciaMedia> Podrobnosti);

/// <summary>
/// Turns what the own reader found into the library's data and into the details in the way of MediaInfo (in Slovak):
/// Všeobecné, Video, Zvuk #n, Titulky #n.
/// </summary>
public static class PopisKontajnera
{
    private static readonly CultureInfo Sk = CultureInfo.GetCultureInfo("sk-SK");

    public static VysledokKontajnera Vytvor(InfoSuboru info)
    {
        var video = info.Stopy.FirstOrDefault(s => s.JeVideo);
        var dlzka = info.DlzkaSekund ?? video?.DlzkaSekund ?? info.Stopy.Max(s => s.DlzkaSekund);
        long? celkovyTok = dlzka is > 0 ? (long)Math.Round(info.Velkost * 8 / dlzka.Value) : null;
        var udaje = new MedialneUdaje(
            info.Format switch
            {
                "Matroska" => "mkv",
                "WebM" => "webm",
                "MPEG-4" => "mp4",
                "QuickTime" => "mov",
                "AVI" => "avi",
                "BDAV (M2TS)" => "m2ts",
                "MPEG-TS" => "mpegts",
                "MPEG-PS" => "mpeg",
                var f => f.ToLowerInvariant(),
            },
            dlzka is { } d ? (long)Math.Round(d * 1000) : null,
            celkovyTok,
            video?.Kodek,
            video?.Sirka,
            video?.Vyska,
            video is null ? Hdr.Ziadne : video.Hdr == Hdr.DolbyVision ? Hdr.DolbyVision : video.Hdr10Plus ? Hdr.Hdr10Plus : video.Hdr,
            video?.Snimky is { } f2 ? Math.Round(f2, 3) : null,
            video is null && info.Stopy.All(s => s.Druh != DruhStopy.Zvuk) ? "súbor nemá obraz ani zvuk" : null);

        var stopy = new List<Stopa>();
        var poradie = 0;
        foreach (var s in info.Stopy)
        {
            if (s.Druh is DruhStopy.Zvuk or DruhStopy.Titulky)
            {
                stopy.Add(new Stopa(poradie, s.Druh.Value, s.Jazyk, s.Kodek, s.Druh == DruhStopy.Zvuk ? s.Kanaly : null, s.Predvolena,
                    s.Vynutena || (s.Nazov?.Contains("forced", StringComparison.OrdinalIgnoreCase) ?? false),
                    s.Sdh || (s.Nazov?.Contains("SDH", StringComparison.Ordinal) ?? false),
                    string.IsNullOrWhiteSpace(s.Nazov) ? null : s.Nazov.Trim()));
            }

            poradie++;
        }

        return new VysledokKontajnera(udaje, stopy, Sekcie(info, dlzka, celkovyTok, video));
    }

    private static List<SekciaMedia> Sekcie(InfoSuboru info, double? dlzka, long? celkovyTok, InfoStopy? video)
    {
        var sekcie = new List<SekciaMedia>();
        var vseobecne = new List<PolozkaMedia>();
        Pridaj(vseobecne, "Formát", info.Format);
        Pridaj(vseobecne, "Verzia formátu", info.VerziaFormatu);
        Pridaj(vseobecne, "Veľkosť súboru", Velkost(info.Velkost));
        Pridaj(vseobecne, "Dĺžka", dlzka is { } d ? Dlzka(d) : null);
        Pridaj(vseobecne, "Celkový dátový tok", Tok(celkovyTok));
        Pridaj(vseobecne, "Snímková frekvencia", video?.Snimky is { } f ? Fps(f) : null);
        Pridaj(vseobecne, "Názov", info.Nazov);
        Pridaj(vseobecne, "Dátum kódovania", info.DatumKodovania?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        Pridaj(vseobecne, "Aplikácia", info.Aplikacia);
        Pridaj(vseobecne, "Knižnica", info.Kniznica);
        sekcie.Add(new SekciaMedia("Všeobecné", vseobecne));

        var pocty = new Dictionary<DruhStopy, int>();
        var celkom = info.Stopy.GroupBy(s => s.Druh).ToDictionary(g => g.Key ?? (DruhStopy)0, g => g.Count());
        foreach (var s in info.Stopy.Where(s => s.Druh is not null))
        {
            var druh = s.Druh!.Value;
            pocty[druh] = pocty.GetValueOrDefault(druh) + 1;
            var meno = druh switch { DruhStopy.Video => "Video", DruhStopy.Zvuk => "Zvuk", _ => "Titulky" };
            var nazov = celkom.GetValueOrDefault(druh) > 1 ? $"{meno} #{pocty[druh]}" : meno;
            var p = new List<PolozkaMedia>();
            Pridaj(p, "ID", s.IdToku is { } pid ? $"{s.Id} (0x{pid.ToString("X", CultureInfo.InvariantCulture)})" : s.Id.ToString(CultureInfo.InvariantCulture));
            Pridaj(p, "Formát", s.Format);
            Pridaj(p, "Formát/Info", s.FormatInfo);
            Pridaj(p, "Obchodný názov", s.ObchodnyNazov);
            Pridaj(p, "Profil formátu", s.Profil);
            Pridaj(p, "ID kodeku", s.IdKodeku);
            Pridaj(p, "Dĺžka", s.DlzkaSekund is { } ds ? Dlzka(ds) : null);
            if (druh != DruhStopy.Titulky)
            {
                Pridaj(p, "Režim dátového toku", s.KonstantnyTok switch { true => "konštantný", false => "premenlivý", _ => null });
            }

            Pridaj(p, "Dátový tok", Tok(s.DatovyTok) is { } t ? (s.OdhadToku ? "~" + t : t) : null);
            Pridaj(p, "Maximálny dátový tok", Tok(s.MaxTok));
            if (druh == DruhStopy.Video)
            {
                Pridaj(p, "Šírka", s.Sirka is { } w ? w.ToString("N0", Sk) + " pixelov" : null);
                Pridaj(p, "Výška", s.Vyska is { } h ? h.ToString("N0", Sk) + " pixelov" : null);
                Pridaj(p, "Pomer strán", s.PomerStran is { } r ? PomerStran(r) : null);
                Pridaj(p, "Režim snímkovej frekvencie", s.KonstantneSnimky switch { true => "konštantný", false => "premenlivý", _ => null });
                Pridaj(p, "Snímková frekvencia", s.Snimky is { } fps ? Fps(fps) : null);
                Pridaj(p, "Farebný priestor", s.FarebnyPriestor);
                Pridaj(p, "Podvzorkovanie farieb", s.Podvzorkovanie);
                Pridaj(p, "Bitová hĺbka", s.BitovaHlbka is { } b ? $"{b} bitov" : null);
                Pridaj(p, "Typ riadkovania", s.Prekladane switch { true => "prekladané", false => "progresívne", _ => null });
                if (s.DatovyTok is { } tok && s.Sirka is > 0 && s.Vyska is > 0 && s.Snimky is > 0)
                {
                    Pridaj(p, "Bitov/(pixel × snímka)", (tok / (s.Sirka.Value * (double)s.Vyska.Value * s.Snimky.Value)).ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

            if (druh == DruhStopy.Zvuk)
            {
                Pridaj(p, "Kanály", s.Kanaly is { } k ? k + (k == 1 ? " kanál" : k < 5 ? " kanály" : " kanálov") : null);
                Pridaj(p, "Rozloženie kanálov", s.RozlozenieKanalov ?? s.Kanaly switch
                {
                    1 => "C",
                    2 => "L R",
                    3 => "L R C",
                    6 => "L R C LFE Ls Rs",
                    8 => "L R C LFE Ls Rs Lb Rb",
                    _ => null,
                });
                Pridaj(p, "Vzorkovacia frekvencia", s.Vzorkovanie is { } hz ? (hz / 1000).ToString("0.0", Sk) + " kHz" : null);
                // the bit depth of lossy codecs is that of the decoder, not of the stream: only lossless ones show it
                Pridaj(p, "Bitová hĺbka", s.BitovaHlbka is { } bz && s.Kodek is not null &&
                    (s.Kodek.StartsWith("pcm", StringComparison.Ordinal) || s.Kodek is "flac" or "alac" or "wavpack" or "dts-hd ma" or "dts:x" ||
                     s.Kodek.StartsWith("truehd", StringComparison.Ordinal)) ? $"{bz} bitov" : null);
                Pridaj(p, "Normalizácia dialógov", s.Dialogy is { } dn ? AnalyzaRamcov.Decibely(dn) : null);
                Pridaj(p, "Dolby Atmos", s.Atmos ? "áno" : null);
            }

            if (druh == DruhStopy.Titulky)
            {
                Pridaj(p, "Počet prvkov", s.PocetSnimok?.ToString("N0", Sk));
            }

            Pridaj(p, "Veľkosť streamu", s.VelkostStreamu is { } v
                ? (s.Odhad ? "~" : "") + Velkost(v) + (info.Velkost > 0 ? $" ({Math.Round(100.0 * v / info.Velkost).ToString(CultureInfo.InvariantCulture)} %)" : "")
                : null);
            Pridaj(p, "Názov", s.Nazov);
            Pridaj(p, "Jazyk", s.Jazyk is { } j ? Jazyky.Nazov(j) : null);
            Pridaj(p, "Predvolená", s.Predvolena ? "áno" : "nie");
            Pridaj(p, "Vynútená", s.Vynutena ? "áno" : "nie");
            if (s.Sdh)
            {
                Pridaj(p, "Pre nepočujúcich", "áno");
            }

            if (druh == DruhStopy.Video)
            {
                Pridaj(p, "Farebný rozsah", s.FarebnyRozsah);
                Pridaj(p, "Primárne farby", s.PrimarneFarby);
                Pridaj(p, "Prenosové charakteristiky", s.Prenos);
                Pridaj(p, "Maticové koeficienty", s.Matica);
                var hdr = new List<string>();
                if (s.Hdr == Hdr.DolbyVision)
                {
                    hdr.Add(s.HdrPopis ?? "Dolby Vision");
                }

                if (s.Hdr10Plus)
                {
                    hdr.Add("HDR10+ (SMPTE ST 2094-40)");
                }

                if (s.Prenos == "PQ (SMPTE ST 2084)" && !s.Hdr10Plus)
                {
                    hdr.Add(s.Hdr != Hdr.DolbyVision && s.HdrPopis is { } popisHdr ? popisHdr : "HDR10 (SMPTE ST 2086)");
                }
                else if (s.Hdr == Hdr.Hlg || s.Prenos == "HLG")
                {
                    hdr.Add("HLG");
                }

                Pridaj(p, "HDR", hdr.Count > 0 ? string.Join(", ", hdr.Distinct()) : null);
            }

            sekcie.Add(new SekciaMedia(nazov, p));
        }

        return sekcie;
    }

    private static void Pridaj(List<PolozkaMedia> polozky, string popis, string? hodnota)
    {
        if (!string.IsNullOrWhiteSpace(hodnota))
        {
            polozky.Add(new PolozkaMedia(popis, hodnota.Trim()));
        }
    }

    /// <summary>"2,26 GiB", "168 MiB", "37,0 KiB", "454 bajtov".</summary>
    public static string Velkost(long bajty) => bajty switch
    {
        >= 1L << 30 => (bajty / (double)(1L << 30)).ToString("0.00", Sk) + " GiB",
        >= 1L << 20 => (bajty / (double)(1L << 20)).ToString(bajty >= 100L << 20 ? "0" : "0.0", Sk) + " MiB",
        >= 1L << 10 => (bajty / 1024.0).ToString("0.0", Sk) + " KiB",
        _ => bajty.ToString(CultureInfo.InvariantCulture) + " bajtov",
    };

    /// <summary>"1 h 31 min", "42 min 10 s".</summary>
    public static string Dlzka(double sekundy)
    {
        var cas = TimeSpan.FromSeconds(sekundy);
        return cas.TotalHours >= 1
            ? $"{(int)cas.TotalHours} h {cas.Minutes} min"
            : cas.TotalMinutes >= 1 ? $"{cas.Minutes} min {cas.Seconds} s" : $"{cas.TotalSeconds.ToString("0.000", Sk)} s";
    }

    /// <summary>"3 525 kb/s", "58 b/s", "25,3 Mb/s".</summary>
    public static string? Tok(long? bity) => bity switch
    {
        null or < 0 => null,
        >= 100_000_000 => (bity.Value / 1_000_000.0).ToString("0.0", Sk) + " Mb/s",
        >= 1000 => (bity.Value / 1000).ToString("N0", Sk) + " kb/s",
        _ => bity.Value.ToString(CultureInfo.InvariantCulture) + " b/s",
    };

    public static string Fps(double fps) => fps.ToString("0.000", Sk) + " FPS";

    /// <summary>"2,40:1", "16:9", "4:3".</summary>
    public static string PomerStran(double pomer) => Math.Abs(pomer - 16 / 9.0) < 0.01 ? "16:9"
        : Math.Abs(pomer - 4 / 3.0) < 0.01 ? "4:3"
        : pomer.ToString("0.00", Sk) + ":1";
}
