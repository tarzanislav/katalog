using System.ComponentModel;
using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Media;

namespace Katalog.Ulohy;

/// <param name="Hotove">Videos of the disk with technical data (read before or now).</param>
/// <param name="Spolu">Present videos of the disk.</param>
public readonly record struct PriebehCitania(int Hotove, int Spolu)
{
    public double Podiel => Spolu == 0 ? 1 : Math.Clamp((double)Hotove / Spolu, 0, 1);
}

/// <summary>How a run of reading ended.</summary>
/// <param name="Stav">Hotova, Zrusena (by the user or the closing program), CakaNaDisk (the disk went away), Chyba.</param>
public sealed record VysledokCitania(StavUlohy Stav, int Precitane, int Necitatelne, string? Sprava);

/// <summary>
/// Reads the technical data of the videos of a disk (stage E3): KATALOG's own reader for MKV and MP4 (only the head of
/// the file), ffprobe for the other videos, external subtitles
/// next to it, and a preview made by ffmpeg; and the fingerprint of each video for finding exact copies (stage E5). One video at a time per disk; what is read is written at once, so a run
/// interrupted at any moment goes on where it stopped.
/// </summary>
public static class CitanieMedii
{
    /// <summary>Width of a preview in pixels (about 15 kB as JPEG).</summary>
    public const int SirkaNahladu = 320;

    private const int Davka = 32;

    /// <param name="korenDisku">Where the disk's root is now (its path when connected).</param>
    /// <param name="obrazky">Where previews go; null = no previews.</param>
    public static async Task<VysledokCitania> SpustiAsync(
        Kniznica kniznica, long diskId, string korenDisku, Ffmpeg? ffmpeg, UlozisteObrazkov? obrazky,
        IProgress<PriebehCitania>? priebeh, CancellationToken zrusenie)
    {
        int spolu, hotove;
        using (var c = kniznica.OtvorCitanie())
        {
            var (videa, zostava) = TechnickeUdaje.Pocty(c, diskId);
            spolu = videa;
            hotove = videa - zostava;
        }

        priebeh?.Report(new PriebehCitania(hotove, spolu));
        int precitane = 0, necitatelne = 0;
        long po = 0;
        try
        {
            while (true)
            {
                IReadOnlyList<VideoNaCitanie> davka;
                var miesta = new Dictionary<long, (string Priecinok, IReadOnlyList<Subor> Subory)>();
                using (var c = kniznica.OtvorCitanie())
                {
                    davka = TechnickeUdaje.NaCitanie(c, diskId, Davka, po);
                    foreach (var priecinok in davka.Select(v => v.PriecinokId).Distinct())
                    {
                        var vnutri = KatalogyADisky.Cesta(c, priecinok).Replace('/', Path.DirectorySeparatorChar);
                        miesta[priecinok] = (Path.Combine(korenDisku, vnutri), KatalogyADisky.Subory(c, priecinok));
                    }
                }

                if (davka.Count == 0)
                {
                    return new VysledokCitania(StavUlohy.Hotova, precitane, necitatelne, null);
                }

                foreach (var video in davka)
                {
                    zrusenie.ThrowIfCancellationRequested();
                    po = video.SuborId;
                    if (!Directory.Exists(korenDisku))
                    {
                        return new VysledokCitania(StavUlohy.CakaNaDisk, precitane, necitatelne, "disk sa odpojil");
                    }

                    var (cestaPriecinka, subory) = miesta[video.PriecinokId];
                    var cesta = Path.Combine(cestaPriecinka, video.Nazov);
                    if (!File.Exists(cesta))
                    {
                        // the disk changed since its scan: the next scan sorts it out
                        continue;
                    }

                    var odtlacok = OdtlacokSuboru.Vypocitaj(cesta, video.Velkost);
                    if (video.MaUdaje)
                    {
                        // read in an earlier version: the fingerprint, or the details of the own reader are missing
                        var vlastne = video.BezPodrobnosti ? CitanieKontajnera.Citaj(cesta) : null;
                        var doplnene = await kniznica.Zapis.ZapisAsync(s =>
                        {
                            var hotovo = false;
                            if (video.BezPodrobnosti)
                            {
                                if (vlastne is not null)
                                {
                                    hotovo = TechnickeUdaje.Zapis(s, video, vlastne.Udaje, vlastne.Stopy.Concat(ExterneTitulky(video, subory)).ToList(), null, Cas.Teraz(), vlastne.Podrobnosti);
                                }
                                else
                                {
                                    TechnickeUdaje.BezPodrobnosti(s, video.SuborId);
                                    hotovo = true;
                                }
                            }

                            return (odtlacok is not null && TechnickeUdaje.ZapisOdtlacok(s, video, odtlacok)) || hotovo;
                        }).ConfigureAwait(false);
                        if (doplnene)
                        {
                            hotove++;
                        }

                        priebeh?.Report(new PriebehCitania(hotove, spolu));
                        continue;
                    }

                    // KATALOG's own reader first (by the content: MKV, MP4, AVI, TS, M2TS, VOB, MPG – the head and a few windows of
                    // the file), ffprobe only for formats it does not know
                    var vlastny = CitanieKontajnera.Citaj(cesta);
                    if (vlastny is null && ffmpeg is null)
                    {
                        priebeh?.Report(new PriebehCitania(hotove, spolu));
                        continue; // waits for FFmpeg
                    }

                    var (udaje, stopy) = vlastny is not null
                        ? new VysledokFfprobe(vlastny.Udaje, vlastny.Stopy)
                        : await ffmpeg!.CitajAsync(cesta, zrusenie).ConfigureAwait(false);
                    IReadOnlyList<SekciaMedia>? podrobnosti = vlastny?.Podrobnosti ??
                        (TechnickeUdaje.VlastnaCitacka.Contains(Path.GetExtension(cesta).TrimStart('.')) ? [] : null);
                    var vsetkyStopy = stopy.Concat(ExterneTitulky(video, subory)).ToList();
                    var nahlad = ffmpeg is not null && obrazky is not null && udaje is { Chyby: null, VideoKodek: not null, DlzkaMs: > 0 }
                        ? await NahladAsync(ffmpeg, obrazky, cesta, udaje.DlzkaMs.Value, zrusenie).ConfigureAwait(false)
                        : null;
                    var zapisane = await kniznica.Zapis.ZapisAsync(s =>
                    {
                        var teraz = Cas.Teraz();
                        IReadOnlyList<(long, long)> nahlady = nahlad is { } n ? [(UlozisteObrazkov.Zaregistruj(s, n.Obrazok, teraz), n.CasMs)] : [];
                        var zapisane = TechnickeUdaje.Zapis(s, video, udaje, vsetkyStopy, nahlady, teraz, podrobnosti);
                        if (zapisane && odtlacok is not null)
                        {
                            TechnickeUdaje.ZapisOdtlacok(s, video, odtlacok);
                        }

                        return zapisane;
                    }).ConfigureAwait(false);
                    if (zapisane)
                    {
                        hotove++;
                        precitane++;
                        necitatelne += udaje.Chyby is null ? 0 : 1;
                    }

                    priebeh?.Report(new PriebehCitania(hotove, spolu));
                }
            }
        }
        catch (OperationCanceledException) when (zrusenie.IsCancellationRequested)
        {
            return new VysledokCitania(StavUlohy.Zrusena, precitane, necitatelne, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // ffprobe itself could not start (deleted, blocked): the job waits for the next time
            return new VysledokCitania(StavUlohy.Chyba, precitane, necitatelne, "ffprobe sa nespustil: " + ex.Message);
        }
    }

    /// <summary>
    /// Subtitle files next to the video whose name begins with the video's name ("Film (2024).sk.srt" for
    /// "Film (2024).mkv"); their language and flags come from the end of the name.
    /// </summary>
    internal static IEnumerable<Stopa> ExterneTitulky(VideoNaCitanie video, IReadOnlyList<Subor> subory)
    {
        var meno = Path.GetFileNameWithoutExtension(video.Nazov);
        var poradie = 1000;
        foreach (var subor in subory.Where(s => DruhySuborov.Urci(s.Pripona) == DruhSuboru.Titulky &&
                                                s.Nazov.StartsWith(meno, StringComparison.OrdinalIgnoreCase) && s.Pripona != "idx")
                                    .OrderBy(s => s.Nazov, StringComparer.OrdinalIgnoreCase))
        {
            var (jazyk, vynutene, sdh) = Jazyky.ZNazvuTitulkov(subor.Nazov, video.Nazov);
            yield return new Stopa(poradie++, DruhStopy.Titulky, jazyk, subor.Pripona, null, false, vynutene, sdh, null, subor.Id);
        }
    }

    /// <summary>A preview a third into the video (past the opening titles, before the end).</summary>
    private static async Task<(UlozenyObrazok Obrazok, long CasMs)?> NahladAsync(
        Ffmpeg ffmpeg, UlozisteObrazkov obrazky, string cesta, long dlzkaMs, CancellationToken zrusenie)
    {
        var cas = dlzkaMs / 3;
        var jpeg = await ffmpeg.NahladAsync(cesta, cas, SirkaNahladu, zrusenie).ConfigureAwait(false);
        if (jpeg is null || RozmerJpeg.Zisti(jpeg) is not { } rozmer)
        {
            return null;
        }

        try
        {
            return (obrazky.Uloz(jpeg, DruhObrazka.Nahlad, rozmer.Sirka, rozmer.Vyska), cas);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no room for pictures: the technical data are kept without a preview
            return null;
        }
    }
}
