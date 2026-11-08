using System.Windows;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>A row of the file list: a folder or a file of the shown folder.</summary>
internal sealed record PolozkaZoznamu(long Id, bool JePriecinok, string Nazov, long Velkost, long? Zmeneny, string Pripona, bool Zmazana)
{
    public static PolozkaZoznamu Z(Priecinok p) => new(p.Id, true, p.Nazov, 0, null, "", p.Zmazany);

    public static PolozkaZoznamu Z(Subor s) => new(s.Id, false, s.Nazov, s.Velkost, s.Zmeneny, s.Pripona, s.Zmazany);

    public string Ikona => JePriecinok ? "" : "";

    public string VelkostText => JePriecinok ? "" : Format.Velkost(Velkost);

    public string ZmeneneText => Zmeneny is { } z ? Format.Cas(z) : "";

    public string Typ => JePriecinok ? "priečinok" : Pripona.ToUpperInvariant();

    /// <summary>Deleted entries (kept in the history) are shown faded and struck through.</summary>
    public double Priehladnost => Zmazana ? 0.5 : 1;

    public TextDecorationCollection? Preciarknutie => Zmazana ? TextDecorations.Strikethrough : null;
}

/// <summary>A line of the detail panel.</summary>
internal sealed record RiadokDetailu(string Popis, string Hodnota);
