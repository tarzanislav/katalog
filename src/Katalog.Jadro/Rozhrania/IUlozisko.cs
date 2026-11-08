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
/// and remote servers through WinSCP. A storage only reads; KATALOG never writes to the user's disks.
/// </summary>
public interface IUlozisko
{
    DruhDisku Druh { get; }

    /// <summary>Identity of the volume, null when it is not reachable now.</summary>
    Task<IdentitaZvazku?> IdentitaAsync(CancellationToken zrusenie);

    /// <summary>Entries of a folder, given by its path relative to the volume's root ("" is the root).</summary>
    IAsyncEnumerable<PolozkaUloziska> PolozkyAsync(string relativnaCesta, CancellationToken zrusenie);
}
