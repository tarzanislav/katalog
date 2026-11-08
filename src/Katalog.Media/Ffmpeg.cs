using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Katalog.Media;

/// <summary>
/// Runs ffprobe and ffmpeg on one file: with low priority (the computer stays usable), a time limit (a hanging network
/// disk does not hold up the queue) and cancellation that ends the process. They only read the video; previews are
/// written to a temporary file of KATALOG, never next to the video.
/// </summary>
public sealed class Ffmpeg(NastrojeFfmpeg nastroje)
{
    /// <summary>How long one file may take (reading a header over a slow network included).</summary>
    public TimeSpan Limit { get; init; } = TimeSpan.FromMinutes(2);

    public NastrojeFfmpeg Nastroje => nastroje;

    /// <summary>Technical data of a file; a file ffprobe cannot read is a result with its error, not an exception.</summary>
    public async Task<VysledokFfprobe> CitajAsync(string subor, CancellationToken zrusenie)
    {
        var (kod, vystup, chyby) = await SpustiAsync(nastroje.Ffprobe,
            ["-v", "error", "-hide_banner", "-print_format", "json", "-show_format", "-show_streams", "-show_error", Vstup(subor)],
            zrusenie).ConfigureAwait(false);
        var vysledok = CitanieFfprobe.Citaj(vystup, chyby);
        return kod != 0 && vysledok.Udaje.Chyby is null && vysledok.Stopy.Count == 0 && vysledok.Udaje.VideoKodek is null
            ? new VysledokFfprobe(Jadro.Model.MedialneUdaje.Necitatelne(chyby.Length > 0 ? chyby : "ffprobe skončil s chybou"), [])
            : vysledok;
    }

    /// <summary>
    /// One frame at <paramref name="casMs"/> as a JPEG <paramref name="sirka"/> pixels wide; null when ffmpeg is not
    /// there or cannot make it (a damaged file, no picture).
    /// </summary>
    public async Task<byte[]?> NahladAsync(string subor, long casMs, int sirka, CancellationToken zrusenie)
    {
        if (nastroje.Ffmpeg is not { } ffmpeg)
        {
            return null;
        }

        var docasny = Path.Combine(Path.GetTempPath(), "KATALOG", "nahlad-" + Guid.NewGuid().ToString("N") + ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(docasny)!);
        try
        {
            var cas = (casMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            var (kod, _, _) = await SpustiAsync(ffmpeg,
                ["-v", "error", "-hide_banner", "-nostdin", "-ss", cas, "-i", Vstup(subor), "-frames:v", "1", "-an", "-sn",
                 "-vf", $"scale={sirka}:-2", "-q:v", "5", "-f", "image2", "-y", docasny],
                zrusenie).ConfigureAwait(false);
            return kod == 0 && File.Exists(docasny) && new FileInfo(docasny).Length > 0
                ? await File.ReadAllBytesAsync(docasny, zrusenie).ConfigureAwait(false)
                : null;
        }
        finally
        {
            try
            {
                File.Delete(docasny);
            }
            catch (IOException)
            {
                // a temporary file a scanner holds goes away with the next cleanup of the temporary folder
            }
        }
    }

    /// <summary>The file always through FFmpeg's file protocol: no name can be taken for another protocol ("concat:…").</summary>
    private static string Vstup(string subor) => "file:" + subor;

    private async Task<(int Kod, string Vystup, string Chyby)> SpustiAsync(string program, IReadOnlyList<string> argumenty, CancellationToken zrusenie)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in argumenty)
        {
            start.ArgumentList.Add(argument);
        }

        using var proces = Process.Start(start) ?? throw new InvalidOperationException(program + " sa nespustil");
        try
        {
            proces.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // the process already ended, or the system does not allow it: it runs at normal priority
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(zrusenie);
        limit.CancelAfter(Limit);
        var vystup = proces.StandardOutput.ReadToEndAsync(limit.Token);
        var chyby = proces.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await proces.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return (proces.ExitCode, await vystup.ConfigureAwait(false), (await chyby.ConfigureAwait(false)).Trim());
        }
        catch (OperationCanceledException)
        {
            try
            {
                proces.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // it ended by itself in the meantime
            }

            zrusenie.ThrowIfCancellationRequested();
            return (-1, "", "čítanie trvalo dlhšie ako " + Limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
        }
    }
}

/// <summary>The size of a JPEG from its header (SOF marker), without decoding it.</summary>
public static class RozmerJpeg
{
    public static (int Sirka, int Vyska)? Zisti(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            var znacka = jpeg[i + 1];
            if (znacka is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF)
            {
                i += znacka == 0xFF ? 1 : 2;
                continue;
            }

            var dlzka = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (znacka is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var vyska = (jpeg[i + 5] << 8) | jpeg[i + 6];
                var sirka = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return sirka > 0 && vyska > 0 ? (sirka, vyska) : null;
            }

            i += 2 + dlzka;
        }

        return null;
    }
}
