using System.Windows;
using Katalog.Data;
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

/// <summary>A row of the search results: a file or folder, its disk and the folder it lies in.</summary>
/// <param name="CestaPriecinka">The folder it lies in, inside its disk ("Filmy/Nové"; "" for the root).</param>
internal sealed record PolozkaVysledku(NajdenaPolozka Polozka, Disk Disk, string CestaPriecinka, bool Pripojeny)
{
    public bool JePriecinok => Polozka.Druh == DruhHladania.Priecinok;

    public string Nazov => Polozka.Nazov;

    public string Ikona => JePriecinok ? "\uE8B7" : DruhySuborov.Urci(Polozka.Pripona) == DruhSuboru.Video ? "\uE8B2" : "\uE8A5";

    public string DiskCislo => Disk.CisloText;

    public string DiskPopis => $"Disk {Disk.CisloText} – {Disk.Nazov}" + (Pripojeny ? " (pripojený)" : " (nie je pripojený)");

    public string CestaText => "\\" + CestaPriecinka.Replace('/', '\\');

    public string VelkostText => JePriecinok ? "" : Format.Velkost(Polozka.Velkost);

    public double Priehladnost => Polozka.Zmazana ? 0.5 : 1;

    public TextDecorationCollection? Preciarknutie => Polozka.Zmazana ? TextDecorations.Strikethrough : null;

    /// <summary>The same entry as a row of a folder's content (for the detail panel).</summary>
    public PolozkaZoznamu AkoPolozkaZoznamu => new(Polozka.Id, JePriecinok, Polozka.Nazov, Polozka.Velkost, Polozka.Zmeneny, Polozka.Pripona, Polozka.Zmazana);
}

/// <summary>A line of the detail panel.</summary>
internal sealed record RiadokDetailu(string Popis, string Hodnota);
