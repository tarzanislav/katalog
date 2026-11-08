using System.Reflection;

namespace Katalog.Jadro;

/// <summary>
/// The program's name and home in one place: the final name is still open (owner, 8 Nov 2026), so every window,
/// file name and installer reads it from here (and from Directory.Build.props).
/// </summary>
public static class Produkt
{
    /// <summary>Displayed name of the program (working name).</summary>
    public const string Nazov = "KATALOG";

    /// <summary>GitHub repository of the program and its releases.</summary>
    public const string Repozitar = "tarzanislav/katalog";

    /// <summary>Extension of a library file.</summary>
    public const string PriponaKniznice = ".katalog";

    /// <summary>Version of the running program (1.2.3); 0.0.0 in development builds.</summary>
    public static Version Verzia { get; } = ZistiVerziu();

    /// <summary>Version as text for windows and logs ("0.1.0").</summary>
    public static string VerziaText => Verzia.ToString(3);

    private static Version ZistiVerziu()
    {
        var text = typeof(Produkt).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        // "0.1.0+commit" – the build metadata is not part of the version
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        return Version.TryParse(text, out var verzia) ? new Version(verzia.Major, verzia.Minor, Math.Max(verzia.Build, 0)) : new Version(0, 0, 0);
    }
}
