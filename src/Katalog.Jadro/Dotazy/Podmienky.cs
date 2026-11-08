using Katalog.Jadro.Model;

namespace Katalog.Jadro.Dotazy;

/// <summary>The fields of the query language (pole:hodnota). Titles, audio, quality and the rest come with their stages.</summary>
public enum PoleDotazu
{
    Pripona = 1,
    Typ = 2,
    Velkost = 3,
    Zmenene = 4,
    Pridane = 5,
    Disk = 6,
    Katalog = 7,
    Pripojene = 8,
    Zmazane = 9,
    Kvalita = 10,
    Kodek = 11,
    Zvuk = 12,
    Titulky = 13,
    Hdr = 14,
    Dlzka = 15,
    Poskodene = 16,
}

/// <summary>Whether entries deleted from their disk (kept in the history) are searched.</summary>
public enum RezimZmazanych
{
    /// <summary>Only what was on the disk at its last scan (the default).</summary>
    Nie = 0,

    /// <summary>Both present and deleted entries.</summary>
    Aj = 1,

    /// <summary>Only deleted entries.</summary>
    Len = 2,
}

/// <summary>One condition of a query. <see cref="Zaporna"/> means "must not" (written with a leading minus).</summary>
public abstract record Podmienka(bool Zaporna);

/// <summary>The name contains this text (any part of a word, with or without diacritics, any letter case).</summary>
/// <param name="Fraza">Written in quotes: the words must follow each other exactly.</param>
public sealed record Slovo(string Text, bool Fraza, bool Zaporna) : Podmienka(Zaporna);

/// <summary>pripona:mkv,mp4 – files with one of these extensions (lower case, without the dot).</summary>
public sealed record Pripony(IReadOnlyList<string> Zoznam, bool Zaporna) : Podmienka(Zaporna);

/// <summary>typ:video,titulky or typ:priecinok – kinds of files, folders, or files of any kind (typ:subor).</summary>
public sealed record Typy(IReadOnlyList<DruhSuboru> Druhy, bool Priecinky, bool VsetkySubory, bool Zaporna) : Podmienka(Zaporna);

/// <summary>velkost:&gt;4GB – size in bytes, both bounds included; null = unbounded.</summary>
public sealed record Velkost(long? Od, long? Do, bool Zaporna) : Podmienka(Zaporna);

/// <summary>
/// zmenene:2024, pridane:7d – a time interval in local time: <see cref="Od"/> included, <see cref="Do"/> excluded, null =
/// unbounded. Relative bounds ("the last 7 days") are resolved when the query runs.
/// </summary>
public sealed record Obdobie(PoleDotazu Pole, Hranica? Od, Hranica? Do, bool Zaporna) : Podmienka(Zaporna);

/// <summary>disk:3,7 or disk:"Filmy 1" – disks by their label number or a part of their name.</summary>
public sealed record Disky(IReadOnlyList<int> Cisla, IReadOnlyList<string> Nazvy, bool Zaporna) : Podmienka(Zaporna);

/// <summary>katalog:Videotéka – catalogues by a part of their name.</summary>
public sealed record Katalogy(IReadOnlyList<string> Nazvy, bool Zaporna) : Podmienka(Zaporna);

/// <summary>pripojene:ano – only on disks connected now (or, with nie, only on disks that are not).</summary>
public sealed record Pripojene(bool Hodnota) : Podmienka(false);

/// <summary>zmazane:aj / zmazane:len – include deleted entries, or search only them.</summary>
public sealed record Zmazane(RezimZmazanych Rezim) : Podmienka(false);

/// <summary>A condition on the technical data of a video (stage E3): only files can match it.</summary>
public abstract record MedialnaPodmienka(bool Zaporna) : Podmienka(Zaporna);

/// <summary>kvalita:4k,fhd or kvalita:>=fhd – classes of resolution.</summary>
public sealed record Kvality(IReadOnlyList<TriedaKvality> Triedy, bool Zaporna) : MedialnaPodmienka(Zaporna);

/// <summary>kodek:hevc,av1 – the codec of the picture, as ffprobe names it.</summary>
public sealed record Kodeky(IReadOnlyList<string> Zoznam, bool Zaporna) : MedialnaPodmienka(Zaporna);

/// <summary>
/// zvuk:cs, titulky:sk,cs – a track of the kind in one of the languages; <see cref="Akakolvek"/> (zvuk:ano) any track
/// of the kind at all.
/// </summary>
public sealed record JazykyStop(DruhStopy Druh, IReadOnlyList<string> Jazyky, bool Akakolvek, bool Zaporna) : MedialnaPodmienka(Zaporna);

/// <summary>hdr:ano, hdr:dv,hdr10 – kinds of HDR.</summary>
public sealed record HdrDruhy(IReadOnlyList<Hdr> Druhy, bool Zaporna) : MedialnaPodmienka(Zaporna);

/// <summary>dlzka:>2h, dlzka:20min..60min – length in milliseconds, both bounds included.</summary>
public sealed record Dlzka(long? OdMs, long? DoMs, bool Zaporna) : MedialnaPodmienka(Zaporna);

/// <summary>poskodene:ano – files ffprobe could not read (N3).</summary>
public sealed record Poskodene(bool Hodnota) : MedialnaPodmienka(false);

/// <summary>A bound of a time interval: an absolute local time, or a number of days, weeks, months or years before now.</summary>
public readonly record struct Hranica(DateTime? Absolutna, int Pocet, JednotkaCasu Jednotka)
{
    public static Hranica Datum(DateTime lokalne) => new(lokalne, 0, JednotkaCasu.Den);

    public static Hranica Pred(int pocet, JednotkaCasu jednotka) => new(null, pocet, jednotka);

    /// <summary>The local time of this bound when the query runs at <paramref name="teraz"/> (local time).</summary>
    public DateTime Urci(DateTime teraz) => Absolutna ?? Jednotka switch
    {
        JednotkaCasu.Tyzden => teraz.AddDays(-7 * Pocet),
        JednotkaCasu.Mesiac => teraz.AddMonths(-Pocet),
        JednotkaCasu.Rok => teraz.AddYears(-Pocet),
        _ => teraz.AddDays(-Pocet),
    };
}

public enum JednotkaCasu
{
    Den,
    Tyzden,
    Mesiac,
    Rok,
}
