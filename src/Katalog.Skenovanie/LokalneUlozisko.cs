using System.IO.Enumeration;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Skenovanie;

/// <summary>
/// A storage reached through the file system: a local or USB volume, an optical disc, a network share or one folder of
/// any of them. Only reads; folders are listed without following links (junctions and symbolic links), so a scan can
/// never loop, and the system's own folders of a volume root are left out.
/// </summary>
public sealed class LokalneUlozisko(string koren, DruhDisku druh, IZvazky zvazky) : IUlozisko
{
    private static readonly HashSet<string> SystemoveVKoreni = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$RECYCLE.BIN", "RECYCLER", "$Recycle.Bin", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp",
    };

    private static readonly EnumerationOptions Moznosti = new()
    {
        RecurseSubdirectories = false,
        // an unreadable folder must say so (its contents are then kept as they were, never marked deleted)
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public DruhDisku Druh { get; } = druh;

    public string Koren { get; } = koren;

    /// <summary>True when the storage's root is the root of its volume ("E:\", "\\nas\filmy").</summary>
    public bool JeKorenZvazku => string.Equals(Path.GetPathRoot(Koren)?.TrimEnd('\\', '/'), Koren.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public IdentitaZvazku? Identita() => zvazky.Identita(Koren);

    public (long? Velkost, long? Volne) Miesto() => zvazky.Miesto(Koren);

    public IEnumerable<PolozkaUloziska> Polozky(string relativnaCesta)
    {
        var cesta = relativnaCesta.Length == 0 ? Koren : Path.Combine(Koren, relativnaCesta.Replace('/', Path.DirectorySeparatorChar));
        var vKoreniZvazku = relativnaCesta.Length == 0 && JeKorenZvazku;
        var zoznam = new FileSystemEnumerable<PolozkaUloziska>(cesta, Polozka, Moznosti)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !(e.IsDirectory && (e.Attributes & FileAttributes.ReparsePoint) != 0) &&
                !(vKoreniZvazku && SystemoveVKoreni.Contains(e.FileName.ToString())),
        };
        return zoznam;
    }

    private static PolozkaUloziska Polozka(ref FileSystemEntry e) => new(
        e.FileName.ToString(),
        e.IsDirectory,
        e.IsDirectory ? 0 : e.Length,
        e.LastWriteTimeUtc.UtcTicks,
        e.CreationTimeUtc.UtcTicks,
        (int)e.Attributes);
}
