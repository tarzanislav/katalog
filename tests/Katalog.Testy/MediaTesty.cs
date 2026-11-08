using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Testy;

/// <summary>Technical data of videos: reading ffprobe, languages, previews, and the query fields that use them.</summary>
public sealed class MediaTesty
{
    /// <summary>Invented ffprobe output of a 4K HDR film with Czech and English sound and three subtitle tracks.</summary>
    internal const string Film4k = """
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "profile": "Main 10", "codec_type": "video", "width": 3840, "height": 1600,
              "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084", "avg_frame_rate": "24000/1001", "r_frame_rate": "24000/1001",
              "disposition": { "default": 1, "forced": 0, "hearing_impaired": 0, "attached_pic": 0 },
              "side_data_list": [ { "side_data_type": "DOVI configuration record", "dv_profile": 8 } ] },
            { "index": 1, "codec_name": "dts", "profile": "DTS-HD MA", "codec_type": "audio", "channels": 6,
              "disposition": { "default": 1, "forced": 0 }, "tags": { "language": "cze", "title": "Vymyslený dabing" } },
            { "index": 2, "codec_name": "eac3", "codec_type": "audio", "channels": 6,
              "disposition": { "default": 0 }, "tags": { "language": "eng" } },
            { "index": 3, "codec_name": "subrip", "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 1 }, "tags": { "language": "slo" } },
            { "index": 4, "codec_name": "hdmv_pgs_subtitle", "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 0, "hearing_impaired": 1 }, "tags": { "language": "eng" } },
            { "index": 5, "codec_name": "subrip", "codec_type": "subtitle", "tags": { "language": "und" } },
            { "index": 6, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 900,
              "disposition": { "attached_pic": 1 } }
          ],
          "format": { "format_name": "matroska,webm", "duration": "7395.123000", "bit_rate": "18500000" }
        }
        """;

    [Test]
    public void CitanieFfprobeFilmu()
    {
        var (udaje, stopy) = CitanieFfprobe.Citaj(Film4k);
        Over.Rovne(new MedialneUdaje("mkv", 7_395_123, 18_500_000, "hevc", 3840, 1600, Hdr.DolbyVision, 23.976, null), udaje, "údaje filmu");
        Over.Rovne(TriedaKvality.UHD4K, udaje.Trieda, "orezaný 4K je 4K");
        Over.Rovne(5, stopy.Count, "dva zvuky a troje titulky, obal nie");
        Over.Rovne(new Stopa(1, DruhStopy.Zvuk, "cs", "dts-hd ma", 6, true, false, false, "Vymyslený dabing"), stopy[0], "český zvuk");
        Over.Rovne(new Stopa(3, DruhStopy.Titulky, "sk", "subrip", null, false, true, false, null), stopy[2], "vynútené slovenské titulky");
        Over.Pravda(stopy[3].Sdh && stopy[3].Jazyk == "en", "titulky pre nepočujúcich");
        Over.Rovne<string?>(null, stopy[4].Jazyk, "neurčený jazyk");
    }

    [Test]
    public void CitanieFfprobeChyb()
    {
        var chyba = CitanieFfprobe.Citaj("""{ "error": { "code": -1094995529, "string": "Invalid data found when processing input" } }""");
        Over.Rovne("Invalid data found when processing input", chyba.Udaje.Chyby, "chyba ffprobe");
        Over.Pravda(CitanieFfprobe.Citaj("nie je to JSON", "moov atom not found").Udaje.Chyby == "moov atom not found", "nečitateľný výstup");
        Over.Pravda(CitanieFfprobe.Citaj("""{ "streams": [], "format": {} }""").Udaje.Chyby is not null, "bez stôp");

        var zvuk = CitanieFfprobe.Citaj("""
            { "streams": [ { "index": 0, "codec_name": "flac", "codec_type": "audio", "channels": 2 } ],
              "format": { "format_name": "flac", "duration": "61.5" } }
            """);
        Over.Pravda(zvuk.Udaje is { Chyby: null, VideoKodek: null, DlzkaMs: 61_500 }, "zvukový súbor bez obrazu nie je chyba");

        var hlg = CitanieFfprobe.Citaj("""
            { "streams": [ { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 1920, "height": 1080,
              "color_transfer": "arib-std-b67", "avg_frame_rate": "0/0", "r_frame_rate": "50/1",
              "tags": { "DURATION": "00:42:10.500000000" } } ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2" } }
            """);
        Over.Rovne(new MedialneUdaje("mp4", 2_530_500, null, "hevc", 1920, 1080, Hdr.Hlg, 50, null), hlg.Udaje, "HLG, dĺžka zo značky, snímky z r_frame_rate");
    }

    [Test]
    public void JazykyAPopisy()
    {
        Over.Rovne("cs", Jazyky.Normalizuj("cze"), "cze");
        Over.Rovne("cs", Jazyky.Normalizuj("CES"), "ces");
        Over.Rovne("sk", Jazyky.Normalizuj("slo"), "slo");
        Over.Rovne("xx", Jazyky.Normalizuj("xx"), "neznámy kód ostane");
        Over.Rovne<string?>(null, Jazyky.Normalizuj("und"), "neurčený");
        Over.Rovne(("sk", true, false), Jazyky.ZNazvuTitulkov("Vymyslený film (2024).sk.forced.srt", "Vymyslený film (2024).mkv"), "jazyk a vynútené z názvu");
        Over.Rovne(("cs", false, true), Jazyky.ZNazvuTitulkov("Vymyslený film.cze.sdh.srt", "Vymyslený film.mkv"), "SDH");
        Over.Rovne(((string?)null, false, false), Jazyky.ZNazvuTitulkov("It (2017).srt", "Iný film.mkv"), "názov filmu nie je jazyk");
        Over.Rovne("DTS-HD MA", PopisMedii.Kodek("dts-hd ma"), "kodek");
        Over.Rovne("H.265", PopisMedii.Kodek("hevc"), "H.265");
        Over.Rovne("5.1", PopisMedii.Kanaly(6), "kanály");
        Over.Rovne("2:03:15", PopisMedii.Dlzka(7_395_123), "dĺžka");
        Over.Rovne("42:10", PopisMedii.Dlzka(2_530_500), "krátka dĺžka");
    }

    [Test]
    public void RozmerNahladuZHlavickyJpeg()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0xB4, 0x01, 0x40, 0x03, 0, 0, 0, 0, 0, 0, 0, 0];
        Over.Rovne<(int, int)?>((320, 180), RozmerJpeg.Zisti(jpeg), "šírka a výška");
        Over.Rovne<(int, int)?>(null, RozmerJpeg.Zisti([0x89, 0x50, 0x4E, 0x47]), "PNG nie je JPEG");
    }

    [Test]
    public void DotazyNaTechnickeUdaje()
    {
        var dotaz = Dotaz.Citaj("kvalita:>=fhd kodek:x265,AVC zvuk:cze,sk titulky:nie hdr:dv dlzka:>1,5h poskodene:nie");
        Over.Rovne(0, dotaz.Upozornenia.Count, string.Join(" ", dotaz.Upozornenia));
        Over.Postupnost([TriedaKvality.FHD, TriedaKvality.UHD4K], dotaz.Podmienky.OfType<Kvality>().Single().Triedy, "kvalita od FHD");
        Over.Postupnost(["hevc", "h264"], dotaz.Podmienky.OfType<Kodeky>().Single().Zoznam, "kodeky ako ich volá ffprobe");
        var stopy = dotaz.Podmienky.OfType<JazykyStop>().ToList();
        Over.Postupnost(["cs", "sk"], stopy[0].Jazyky, "jazyky zvuku");
        Over.Pravda(stopy[1] is { Druh: DruhStopy.Titulky, Akakolvek: true, Zaporna: true }, "titulky:nie = bez titulkov");
        Over.Postupnost([Hdr.DolbyVision], dotaz.Podmienky.OfType<HdrDruhy>().Single().Druhy, "Dolby Vision");
        Over.Rovne(new Dlzka(5_400_001, null, false), dotaz.Podmienky.OfType<Dlzka>().Single(), "dĺžka viac ako 1,5 h");
        Over.Rovne(new Poskodene(false), dotaz.Podmienky.OfType<Poskodene>().Single(), "nepoškodené");
        Over.Pravda(!dotaz.HladaPriecinky && dotaz.HladaSubory, "technické údaje majú len súbory");

        Over.Rovne(new Dlzka(80 * 60_000L, 100 * 60_000L, false), Dotaz.Citaj("dlzka:80..100min").Podmienky.Single(), "holé číslo sú minúty");
        Over.Rovne(new Dlzka(null, 90 * 60_000L, false), Dotaz.Citaj("dlzka:<=1:30").Podmienky.Single(), "h:mm");
        Over.Rovne(3, Dotaz.Citaj("kvalita:8k kodek:theora zvuk:").Upozornenia.Count, "nezrozumiteľné hodnoty");
    }

    /// <summary>
    /// The real ffprobe and ffmpeg (where they are installed – GitHub Actions installs them on Linux): an invented video
    /// made by ffmpeg itself is read and gets a preview.
    /// </summary>
    [Test]
    public async Task SkutocnyFfprobeAFfmpeg()
    {
        var nastroje = HladanieFfmpeg.Najdi(Environment.GetEnvironmentVariable("KATALOG_FFMPEG_DIR"), AppContext.BaseDirectory);
        if (nastroje?.Ffmpeg is null)
        {
            Console.WriteLine("ffprobe a ffmpeg tu nie sú – test vynechaný");
            return;
        }

        using var priecinok = new DocasnyPriecinok();
        var video = Path.Combine(priecinok.Cesta, "Vymyslený film (2024).mkv");
        await VymysleneVideo.VytvorAsync(nastroje.Ffmpeg, video);

        var ffmpeg = new Ffmpeg(nastroje);
        var (udaje, stopy) = await ffmpeg.CitajAsync(video, CancellationToken.None);
        Over.Pravda(udaje is { Chyby: null, Sirka: 320, Vyska: 180, Kontajner: "mkv" }, $"údaje vymysleného videa ({udaje})");
        Over.Pravda(udaje.DlzkaMs is >= 1900 and <= 2100, $"dĺžka okolo 2 s ({udaje.DlzkaMs})");
        Over.Pravda(stopy.Any(s => s is { Druh: DruhStopy.Zvuk, Jazyk: "sk" }), "slovenský zvuk");

        var nahlad = await ffmpeg.NahladAsync(video, 1000, 160, CancellationToken.None);
        Over.Pravda(nahlad is not null && RozmerJpeg.Zisti(nahlad) == (160, 90), "náhľad 160×90");

        var pokazene = Path.Combine(priecinok.Cesta, "Vymyslený pokazený.mkv");
        await File.WriteAllBytesAsync(pokazene, new byte[4096]);
        Over.Pravda((await ffmpeg.CitajAsync(pokazene, CancellationToken.None)).Udaje.Chyby is not null, "pokazený súbor má chybu, nie výnimku");
        Over.Pravda(await ffmpeg.NahladAsync(pokazene, 0, 160, CancellationToken.None) is null, "pokazený súbor nemá náhľad");
    }
}

/// <summary>An invented two-second video made by ffmpeg itself (test picture, a tone, Slovak sound).</summary>
internal static class VymysleneVideo
{
    public static async Task VytvorAsync(string ffmpeg, string ciel)
    {
        var start = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[]
                 {
                     "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=25:duration=2",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "mjpeg", "-q:v", "8", "-c:a", "pcm_s16le",
                     "-metadata:s:a:0", "language=slo", "-y", ciel,
                 })
        {
            start.ArgumentList.Add(a);
        }

        using var proces = System.Diagnostics.Process.Start(start)!;
        var chyby = await proces.StandardError.ReadToEndAsync();
        await proces.WaitForExitAsync();
        if (proces.ExitCode != 0)
        {
            throw new InvalidOperationException("ffmpeg nevytvoril vymyslené video: " + chyby);
        }
    }
}
