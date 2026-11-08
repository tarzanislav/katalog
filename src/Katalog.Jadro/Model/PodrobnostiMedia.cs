namespace Katalog.Jadro.Model;

/// <summary>One line of the details of a file ("Bitová hĺbka", "10 bitov").</summary>
public sealed record PolozkaMedia(string Popis, string Hodnota);

/// <summary>A part of the details of a file, as MediaInfo shows them: "Všeobecné", "Video", "Zvuk #1", "Titulky #2".</summary>
public sealed record SekciaMedia(string Nazov, IReadOnlyList<PolozkaMedia> Polozky);
