namespace Katalog.Vystupy;

/// <summary>
/// A page drawn as simple shapes in millimetres from the top-left corner. The outputs lay their pages out here (where
/// they can be tested on any system); the window then draws them for the printer, the preview and the PDF.
/// </summary>
public sealed record StranaNakresu(double Sirka, double Vyska, IReadOnlyList<PrvokNakresu> Prvky);

public abstract record PrvokNakresu;

public enum ZarovnanieTextu
{
    Vlavo = 0,
    Stred = 1,
    Vpravo = 2,
}

/// <summary>
/// Text in a box; it never leaves the box: what does not fit ends with "…" (or wraps over more lines when
/// <see cref="Riadkov"/> is above 1). The size is in typographic points.
/// </summary>
public sealed record TextNakresu(
    double X,
    double Y,
    double Sirka,
    double Vyska,
    string Text,
    double Velkost,
    bool Tucne = false,
    ZarovnanieTextu Zarovnanie = ZarovnanieTextu.Vlavo,
    int Riadkov = 1,
    string Farba = Nakres.Cierna) : PrvokNakresu;

public sealed record CiaraNakresu(double X1, double Y1, double X2, double Y2, double Hrubka, string Farba = Nakres.Cierna) : PrvokNakresu;

/// <summary>A rectangle: outline of <see cref="Hrubka"/> (0 = none) and an optional fill.</summary>
public sealed record ObdlznikNakresu(double X, double Y, double Sirka, double Vyska, double Hrubka, string? Vypln = null, string Farba = Nakres.Cierna)
    : PrvokNakresu;

/// <summary>A QR code (without its quiet zone, which the layout leaves free around it) in a square of <see cref="Strana"/>.</summary>
public sealed record QrNakresu(double X, double Y, double Strana, bool[,] Moduly) : PrvokNakresu;

/// <summary>A picture file (a poster) fitted into the box keeping its proportions.</summary>
public sealed record ObrazokNakresu(double X, double Y, double Sirka, double Vyska, string Cesta) : PrvokNakresu;

public static class Nakres
{
    public const string Cierna = "#000000";

    /// <summary>A4 portrait.</summary>
    public const double SirkaA4 = 210;

    public const double VyskaA4 = 297;

    /// <summary>Millimetres of one typographic point.</summary>
    public const double MmNaBod = 25.4 / 72;
}
