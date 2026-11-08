namespace Katalog.Jadro.Model;

/// <summary>Parts of a file name.</summary>
public static class NazovSuboru
{
    /// <summary>Extension of a file name, lower case without the dot ("" when there is none; ".skryty" has none).</summary>
    public static string Pripona(string nazov)
    {
        var bodka = nazov.LastIndexOf('.');
        return bodka <= 0 || bodka == nazov.Length - 1 ? "" : nazov[(bodka + 1)..].ToLowerInvariant();
    }
}
