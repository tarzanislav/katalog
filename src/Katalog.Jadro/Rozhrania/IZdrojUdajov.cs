using Katalog.Jadro.Model;

namespace Katalog.Jadro.Rozhrania;

/// <summary>What a title search knows: the name as searched, an optional year and kind.</summary>
public sealed record Hladanie(string Nazov, int? Rok = null, DruhTitulu? Druh = null);

/// <summary>One candidate a source offers for a searched title (shown side by side for confirmation).</summary>
/// <param name="Identifikator">The source's own identifier ("movie/603" for TMDB).</param>
/// <param name="Zhoda">How well it matches the search, 0 to 1.</param>
public sealed record Kandidat(ZdrojUdajov Zdroj, string Identifikator, DruhTitulu Druh, string Nazov, string? Original, int? Rok, double Zhoda);

/// <summary>An episode as a source describes it.</summary>
public sealed record UdajeEpizody(int Seria, int Cislo, string? Nazov, DateOnly? Vysielanie, string? Dej);

/// <summary>A person in a title.</summary>
public sealed record UdajeOsoby(string Identifikator, string Meno, RolaOsoby Rola, string? Postava, int Poradie);

/// <summary>Data of a title from one source; null fields are unknown to the source.</summary>
public sealed record UdajeTitulu(
    ZdrojUdajov Zdroj,
    string Identifikator,
    DruhTitulu Druh,
    string Nazov,
    string? Original,
    int? Rok,
    string? Dej,
    int? DlzkaMinut,
    double? Hodnotenie,
    IReadOnlyList<string> Zanre,
    IReadOnlyList<UdajeOsoby> Osoby,
    IReadOnlyList<UdajeEpizody> Epizody,
    Uri? Plagat,
    IReadOnlyList<(ZdrojUdajov Zdroj, string Identifikator)> DalsieIdentifikatory);

/// <summary>A collection (of films) as a source describes it, with all its parts – also those the user does not have.</summary>
public sealed record UdajeKolekcie(ZdrojUdajov Zdroj, string Identifikator, string Nazov, string? Dej, IReadOnlyList<Kandidat> Diely);

/// <summary>
/// One interface for every data source (architecture decision A6): a new source is added without rebuilding the program,
/// and a switched-off source breaks nothing. Only the searched name of a title is ever sent to a source.
/// </summary>
public interface IZdrojUdajov
{
    ZdrojUdajov Zdroj { get; }

    /// <summary>True when the source is switched on and has what it needs (a key, an address).</summary>
    bool JeZapnuty { get; }

    Task<IReadOnlyList<Kandidat>> HladajAsync(Hladanie hladanie, CancellationToken zrusenie);

    Task<UdajeTitulu?> NacitajAsync(string identifikator, CancellationToken zrusenie);

    /// <summary>Collections the title belongs to; empty when the source knows none.</summary>
    Task<IReadOnlyList<UdajeKolekcie>> KolekcieAsync(string identifikator, CancellationToken zrusenie);
}
