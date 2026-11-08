namespace Katalog.Jadro.Model;

/// <summary>
/// Times in the library are stored as UTC ticks (100 ns since 0001-01-01): file times of NTFS keep their full precision,
/// so a difference update never mistakes an unchanged file for a changed one.
/// </summary>
public static class Cas
{
    public static long Teraz() => DateTime.UtcNow.Ticks;

    public static long Z(DateTime cas) => cas.Kind == DateTimeKind.Local ? cas.ToUniversalTime().Ticks : cas.Ticks;

    public static DateTime NaUtc(long tiky) => new(tiky, DateTimeKind.Utc);
}
