using Katalog.Jadro.Rozhrania;

namespace Katalog.Jadro.Model;

/// <summary>A catalogue of the library (e.g. "Videotéka", "Zálohy"), decision A1. (Not named "Katalog": that is the root namespace.)</summary>
public sealed record KatalogKniznice(long Id, string Nazov, string? Farba, int Poradie);

/// <summary>
/// A disk of a catalogue: a whole volume (<see cref="Koren"/> is "") or one folder of it ("Filmy/Nové"). It is recognised by
/// the identity of its volume, never by its drive letter.
/// </summary>
/// <param name="Cislo">The number printed on its label ("disk 003").</param>
/// <param name="PoslednySken">UTC ticks of the last finished scan; null before the first one.</param>
public sealed record Disk(
    long Id,
    long KatalogId,
    int Cislo,
    string Nazov,
    IdentitaZvazku Identita,
    string Koren,
    long? Volne,
    string? Umiestnenie,
    string? Poznamka,
    long Pridany,
    long? PoslednySken,
    long? PoslednePripojenie,
    bool Nahlady = false)
{
    public DruhDisku Druh => Identita.Druh;

    /// <summary>"003" – the number as it is printed and searched.</summary>
    public string CisloText => Cislo.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A folder of a disk in the library.</summary>
/// <param name="MaPodpriecinky">True when it has at least one subfolder that is not deleted.</param>
public sealed record Priecinok(long Id, long DiskId, long? RodicId, string Nazov, bool Zmazany, bool MaPodpriecinky);

/// <summary>A file of a disk in the library.</summary>
public sealed record Subor(long Id, long PriecinokId, string Nazov, string Pripona, long Velkost, long Zmeneny, int Atributy, bool Zmazany);

/// <summary>Size of what a disk or a folder holds (files that are not deleted).</summary>
public sealed record Suhrn(long Subory, long Priecinky, long Bajty);
