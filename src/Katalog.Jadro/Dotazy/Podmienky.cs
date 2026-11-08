using Katalog.Jadro.Model;

namespace Katalog.Jadro.Dotazy;

/// <summary>The fields of the query language (pole:hodnota). Duplicates, tags and the rest come with their stages.</summary>
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
    Rok = 17,
    Zaner = 18,
    Videne = 19,
    Hodnotenie = 20,
    MojeHodnotenie = 21,
    Herec = 22,
    Rezia = 23,
    Kolekcia = 24,
    Kniznica = 25,
    Mam = 26,
    Zelane = 27,
    Verzie = 28,
    Zalohovane = 29,
    Duplikat = 30,
}

/// <summary>Where a file or title stands in the library of titles (kniznica:film, kniznica:potvrdit…).</summary>
public enum StavVKniznici
{
    Film = 1,
    Serial = 2,
    /// <summary>A file that is a version of a title (kniznica:ano).</summary>
    VKniznici = 3,
    /// <summary>A video set aside as no title (kniznica:nie).</summary>
    MimoKniznice = 4,
    /// <summary>A version waiting for confirmation (kniznica:potvrdit).</summary>
    NaPotvrdenie = 5,
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

/// <summary>
/// A condition on the library of titles (stage E4): a title matches it, a file when it is a version of a matching title
/// (folders never).
/// </summary>
public abstract record TitulovaPodmienka(bool Zaporna) : Podmienka(Zaporna);

/// <summary>rok:2001, rok:1990..1999, rok:&gt;=2020 – the year of the title, both bounds included.</summary>
public sealed record Roky(int? Od, int? Do, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>zaner:komedia,horor – genres by a part of their name (without diacritics).</summary>
public sealed record Zanre(IReadOnlyList<string> Nazvy, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>videne:ano – a watched film, or a series with a watched episode (a file: its own film or episode).</summary>
public sealed record Videne(bool Hodnota) : TitulovaPodmienka(false);

/// <summary>hodnotenie:&gt;=7.5 (the source's rating) or moje:&gt;=8 (the user's own), both bounds included.</summary>
public sealed record Hodnotenia(double? Od, double? Do, bool Moje, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>herec:"Meno Priezvisko", rezia:Meno – people of the title by a part of their name.</summary>
public sealed record Osoby(RolaOsoby Rola, IReadOnlyList<string> Mena, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>kolekcia:ano, or kolekcia:"Vymyslená sága" – in any collection, or in one by a part of its name.</summary>
public sealed record Kolekcie(IReadOnlyList<string> Nazvy, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>kniznica:film, kniznica:serial, kniznica:ano, kniznica:nie, kniznica:potvrdit.</summary>
public sealed record VKniznici(StavVKniznici Stav, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>mam:nie – titles the user does not have (missing parts of collections, HISTRA lists, wishes).</summary>
public sealed record Mam(bool Hodnota) : TitulovaPodmienka(false);

/// <summary>zelane:ano – titles on the wish list.</summary>
public sealed record Zelane(bool Hodnota) : TitulovaPodmienka(false);

/// <summary>verzie:&gt;1 – titles with more present versions (duplicates; a series: of one of its episodes).</summary>
public sealed record PocetVerzii(int? Od, int? Do, bool Zaporna) : TitulovaPodmienka(Zaporna);

/// <summary>
/// zalohovane:nie – titles that lie on one disk only (N1): a film whose present versions are all on one disk, a series
/// with an episode on one disk only.
/// </summary>
public sealed record Zalohovane(bool Hodnota) : TitulovaPodmienka(false);

/// <summary>duplikat:ano – files with an exact copy elsewhere (the same fingerprint: size, first and last megabyte).</summary>
public sealed record Duplikat(bool Hodnota) : Podmienka(false);

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
