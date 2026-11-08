using System.IO;
using Katalog.Jadro.Prehravanie;
using Microsoft.Win32;

namespace Katalog.Okno;

/// <summary>
/// Players installed on this computer: found where Windows registers programs (App Paths) and in their usual folders.
/// Only looked up when the settings open; nothing is installed or changed.
/// </summary>
internal static class Prehravace
{
    private static readonly (string Program, string[] Priecinky)[] Zname =
    [
        ("vlc.exe", [@"VideoLAN\VLC"]),
        ("mpc-hc64.exe", [@"MPC-HC", @"K-Lite Codec Pack\MPC-HC64"]),
        ("mpc-hc.exe", [@"MPC-HC", @"K-Lite Codec Pack\MPC-HC"]),
        ("mpc-be64.exe", [@"MPC-BE x64", "MPC-BE"]),
        ("mpc-be.exe", ["MPC-BE"]),
        ("PotPlayerMini64.exe", [@"DAUM\PotPlayer", "PotPlayer"]),
        ("PotPlayerMini.exe", [@"DAUM\PotPlayer", "PotPlayer"]),
        ("mpv.exe", ["mpv"]),
    ];

    public static IReadOnlyList<Prehravac> Najdi()
    {
        var najdene = new List<Prehravac>();
        foreach (var (program, priecinky) in Zname)
        {
            if (Cesta(program, priecinky) is { } cesta && najdene.All(p => p.Druh != Prehravac.ZProgramu(cesta).Druh))
            {
                najdene.Add(Prehravac.ZProgramu(cesta));
            }
        }

        return najdene;
    }

    private static string? Cesta(string program, string[] priecinky)
    {
        foreach (var koren in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var kluc = koren.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + program);
                if (kluc?.GetValue(null) is string zapis && File.Exists(zapis.Trim('"')))
                {
                    return zapis.Trim('"');
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // an unreadable key is simply not a place where the player is
            }
        }

        var zaklady = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        return zaklady
            .Where(z => z.Length > 0)
            .SelectMany(z => priecinky.Select(p => Path.Combine(z, p, program)))
            .FirstOrDefault(File.Exists);
    }
}
