using System.Security.Cryptography;
using System.Text.Json;

namespace Katalog.Media;

/// <summary>Where ffprobe and ffmpeg were found, and from where (shown in the settings).</summary>
public sealed record NastrojeFfmpeg(string Ffprobe, string? Ffmpeg, ZdrojFfmpeg Zdroj, string Priecinok);

public enum ZdrojFfmpeg
{
    /// <summary>The folder chosen in the settings.</summary>
    Zvoleny,

    /// <summary>The FFmpeg installed with UNIVERSUM.</summary>
    Universum,

    /// <summary>The FFmpeg installed with KATALOG (built from source, LGPL).</summary>
    Pribaleny,

    /// <summary>A program on the PATH.</summary>
    Cesta,
}

/// <summary>
/// Finds FFmpeg (owner, 8. 11. 2026): the folder chosen in the settings, then UNIVERSUM's, then the one installed with
/// KATALOG, then the PATH. KATALOG starts its own copy only when every file matches its manifest (SHA-256).
/// </summary>
public static class HladanieFfmpeg
{
    /// <summary>AppId of UNIVERSUM's installer: its uninstall entry says where it is installed.</summary>
    private const string UninstallUniversum = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{3F7A2C91-6B4E-4D8A-9E15-7C2B0D4F8A63}_is1";

    public static NastrojeFfmpeg? Najdi(string? zvolenyPriecinok, string priecinokProgramu)
    {
        if (!string.IsNullOrWhiteSpace(zvolenyPriecinok) && V(zvolenyPriecinok, ZdrojFfmpeg.Zvoleny) is { } zvoleny)
        {
            return zvoleny;
        }

        foreach (var universum in PriecinkyUniversum())
        {
            if (V(Path.Combine(universum, "speech", "ffmpeg"), ZdrojFfmpeg.Universum) is { } u)
            {
                return u;
            }
        }

        var pribaleny = Path.Combine(priecinokProgramu, "ffmpeg");
        if (V(pribaleny, ZdrojFfmpeg.Pribaleny) is { } p && SediManifest(pribaleny))
        {
            return p;
        }

        foreach (var priecinok in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (V(priecinok.Trim('"'), ZdrojFfmpeg.Cesta) is { } c)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The tools in one folder: ffprobe is required, ffmpeg (for previews) optional.</summary>
    public static NastrojeFfmpeg? V(string priecinok, ZdrojFfmpeg zdroj)
    {
        try
        {
            var ffprobe = Path.Combine(priecinok, Exe("ffprobe"));
            if (!File.Exists(ffprobe))
            {
                return null;
            }

            var ffmpeg = Path.Combine(priecinok, Exe("ffmpeg"));
            return new NastrojeFfmpeg(ffprobe, File.Exists(ffmpeg) ? ffmpeg : null, zdroj, priecinok);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> PriecinkyUniversum()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var koren in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                string? miesto = null;
                try
                {
                    using var kluc = koren.OpenSubKey(UninstallUniversum);
                    miesto = kluc?.GetValue("InstallLocation") as string;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // an unreadable key is not a place where UNIVERSUM is
                }

                if (!string.IsNullOrWhiteSpace(miesto))
                {
                    yield return miesto.TrimEnd('\\');
                }
            }
        }

        var programy = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programy.Length > 0)
        {
            yield return Path.Combine(programy, "UNIVERSUM");
        }
    }

    /// <summary>
    /// Every file of manifest.json (written when FFmpeg was built for KATALOG) has its SHA-256: a changed or missing file
    /// means the copy is not started.
    /// </summary>
    public static bool SediManifest(string priecinok)
    {
        try
        {
            using var dokument = JsonDocument.Parse(File.ReadAllText(Path.Combine(priecinok, "manifest.json")));
            var subory = dokument.RootElement.GetProperty("files");
            var pocet = 0;
            foreach (var subor in subory.EnumerateObject())
            {
                var cesta = Path.Combine(priecinok, subor.Name);
                if (subor.Name.Contains("..", StringComparison.Ordinal) || !File.Exists(cesta))
                {
                    return false;
                }

                using var prud = File.OpenRead(cesta);
                if (!Convert.ToHexString(SHA256.HashData(prud)).Equals(subor.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                pocet++;
            }

            return pocet > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string Exe(string meno) => OperatingSystem.IsWindows() ? meno + ".exe" : meno;
}
