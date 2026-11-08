namespace Katalog.Jadro.Model;

// Codes stored in the library as integers. A stored number never changes its meaning: new values are only added,
// and the CHECK constraints of the schema (Katalog.Data/Schema) list exactly these values – a test keeps both in step.

/// <summary>Kind of a disk (storage) in a catalogue.</summary>
public enum DruhDisku
{
    Nezisteny = 0,
    Usb = 1,
    Interny = 2,
    Sietovy = 3,
    Opticky = 4,
    Vzdialeny = 5,
}

/// <summary>State of a file or folder: deleted ones stay in the history with the date of deletion.</summary>
public enum StavPolozky
{
    Aktivna = 0,
    Zmazana = 1,
}

/// <summary>Kind of a track of a video file.</summary>
public enum DruhStopy
{
    Video = 1,
    Zvuk = 2,
    Titulky = 3,
}

/// <summary>High dynamic range of a video.</summary>
public enum Hdr
{
    Ziadne = 0,
    Hdr10 = 1,
    Hdr10Plus = 2,
    DolbyVision = 3,
    Hlg = 4,
}

/// <summary>Quality class by resolution, the same as in UNIVERSUM file names.</summary>
public enum TriedaKvality
{
    Nezistena = 0,
    SD = 1,
    HD = 2,
    FHD = 3,
    UHD4K = 4,
}

/// <summary>Film or series; episodes belong only to a series.</summary>
public enum DruhTitulu
{
    Film = 1,
    Serial = 2,
}

/// <summary>Where a value came from (field origin) or which service an identifier belongs to.</summary>
public enum ZdrojUdajov
{
    /// <summary>Typed or changed by the user: such a field is locked.</summary>
    Pouzivatel = 0,
    NazovSuboru = 1,
    Ffprobe = 2,
    Tmdb = 3,
    Histra = 4,
    Jellyfin = 5,
    Imdb = 6,
}

/// <summary>Kind of object that an identifier or a field origin describes.</summary>
public enum DruhObjektu
{
    Titul = 1,
    Epizoda = 2,
    Kolekcia = 3,
    Osoba = 4,
}

/// <summary>Role of a person in a title.</summary>
public enum RolaOsoby
{
    Herec = 1,
    Rezia = 2,
    Scenar = 3,
}

/// <summary>Playlist kinds: manual, smart (a query) and random (a query drawn at random).</summary>
public enum DruhPlaylistu
{
    Rucny = 1,
    Inteligentny = 2,
    Nahodny = 3,
}

/// <summary>Kinds of stored pictures.</summary>
public enum DruhObrazka
{
    Plagat = 1,
    Pozadie = 2,
    Nahlad = 3,
    Osoba = 4,
}

/// <summary>State of a job in the persistent queue (also of a scan).</summary>
public enum StavUlohy
{
    Caka = 0,
    Bezi = 1,
    Hotova = 2,
    Chyba = 3,
    Zrusena = 4,
    /// <summary>Waits until its disk is connected, then starts by itself.</summary>
    CakaNaDisk = 5,
}

/// <summary>Kinds of jobs.</summary>
public enum DruhUlohy
{
    Skenovanie = 1,
    Ffprobe = 2,
    Udaje = 3,
    Nahlad = 4,
    Zdravie = 5,
    Export = 6,
    Prehranie = 7,
}

/// <summary>Health of a disk as read from SMART.</summary>
public enum StavZdravia
{
    Neznamy = 0,
    Dobry = 1,
    Varovanie = 2,
    Zly = 3,
}

/// <summary>
/// Kind of an entry of the full-text index. The index row id is <c>id * 8 + kind</c>, so one index serves files,
/// folders, titles, episodes, people, collections and disks without storing their names twice.
/// </summary>
public enum DruhHladania
{
    Subor = 1,
    Priecinok = 2,
    Titul = 3,
    Epizoda = 4,
    Osoba = 5,
    Kolekcia = 6,
    Disk = 7,
}

/// <summary>Row id of the full-text index for an object, and back.</summary>
public static class KlucHladania
{
    public const int Nasobok = 8;

    public static long Z(DruhHladania druh, long id) => checked(id * Nasobok + (int)druh);

    public static (DruhHladania Druh, long Id) Rozloz(long kluc) => ((DruhHladania)(kluc % Nasobok), kluc / Nasobok);
}
