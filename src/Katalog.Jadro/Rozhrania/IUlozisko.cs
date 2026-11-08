using Katalog.Jadro.Model;

namespace Katalog.Jadro.Rozhrania;

/// <summary>
/// What identifies a volume regardless of its drive letter. Every part may be missing (an optical disc has no physical
/// serial, a network share no volume GUID); the identity is matched by points (Katalog.Skenovanie).
/// </summary>
public sealed record IdentitaZvazku(
    DruhDisku Druh,
    string? SerioveCisloZvazku,
    string? GuidZvazku,
    string? SerioveCisloDisku,
    string? Menovka,
    string? SuborovySystem,
    long? Velkost,
    string? Adresa);

/// <summary>One entry of a folder as a storage reports it.</summary>
/// <param name="Zmeneny">Last change, UTC ticks.</param>
public sealed record PolozkaUloziska(string Nazov, bool JePriecinok, long Velkost, long Zmeneny, long? Vytvoreny, int Atributy);

/// <summary>
/// One interface for every kind of storage (architecture decision A6): local and USB disks, network shares, optical discs
/// and remote servers through WinSCP. A storage only reads; KATALOG never writes to the user's disks. Reading is blocking
/// (it runs on the scanner's own low-priority thread).
/// </summary>
public interface IUlozisko
{
    DruhDisku Druh { get; }

    /// <summary>Where the storage is now, as the user knows it ("E:\", "E:\Filmy", "\\nas\filmy").</summary>
    string Koren { get; }

    /// <summary>Identity of the volume, null when it is not reachable now.</summary>
    IdentitaZvazku? Identita();

    /// <summary>Size and free space of the volume in bytes, when known.</summary>
    (long? Velkost, long? Volne) Miesto();

    /// <summary>
    /// Entries of a folder given by its path relative to <see cref="Koren"/> ("" is the root, parts joined by '/').
    /// Throws <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/> when the folder cannot be read.
    /// </summary>
    IEnumerable<PolozkaUloziska> Polozky(string relativnaCesta);
}
