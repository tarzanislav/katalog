using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Skenovanie;

/// <summary>A volume connected now: where it is, what it is and how much is free.</summary>
/// <param name="Koren">"E:\" or "\\nas\filmy".</param>
public sealed record PripojenyZvazok(string Koren, IdentitaZvazku Identita, long? Volne);

/// <summary>The volumes of this computer (Windows: <see cref="ZvazkyWindows"/>; elsewhere a plain fallback for tests).</summary>
public interface IZvazky
{
    /// <summary>Volumes that are connected and ready now (disc in the drive, share reachable).</summary>
    IReadOnlyList<PripojenyZvazok> Pripojene();

    /// <summary>Identity of the volume that holds <paramref name="cesta"/> (a root or any folder); null when unreachable.</summary>
    IdentitaZvazku? Identita(string cesta);

    (long? Velkost, long? Volne) Miesto(string cesta);
}

/// <summary>The volumes of the running system.</summary>
public static class Zvazky
{
    public static IZvazky Systemove { get; } = OperatingSystem.IsWindows() ? new ZvazkyWindows() : new ZvazkyInde();
}

/// <summary>Volumes outside Windows (tests on Linux): the mount point stands in for the identity.</summary>
internal sealed class ZvazkyInde : IZvazky
{
    public IReadOnlyList<PripojenyZvazok> Pripojene() =>
        DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new PripojenyZvazok(d.RootDirectory.FullName, Identita(d), d.AvailableFreeSpace)).ToList();

    public IdentitaZvazku? Identita(string cesta)
    {
        var plna = Path.GetFullPath(cesta);
        if (!Directory.Exists(plna))
        {
            return null;
        }

        var disk = DriveInfo.GetDrives().Where(d => d.IsReady && plna.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
            .MaxBy(d => d.RootDirectory.FullName.Length);
        return disk is null ? new IdentitaZvazku(DruhDisku.Interny, null, null, null, null, null, null, plna) : Identita(disk);
    }

    public (long? Velkost, long? Volne) Miesto(string cesta)
    {
        try
        {
            var disk = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cesta))!);
            return disk.IsReady ? (disk.TotalSize, disk.AvailableFreeSpace) : (null, null);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static IdentitaZvazku Identita(DriveInfo d) =>
        new(DruhDisku.Interny, d.RootDirectory.FullName, null, null, d.VolumeLabel, d.DriveFormat, d.TotalSize, null);
}
