using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Katalog.Jadro;

namespace Katalog.Okno;

/// <summary>
/// Settings of this computer's user (not of the library): where the library is, the theme, the player, update checks. Stored as
/// JSON in %APPDATA%\KATALOG. Keys and passwords never go here (they are encrypted separately, decision A8).
/// </summary>
public sealed class Nastavenia
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    /// <summary>Full path of the library file; empty = the default in Documents\KATALOG.</summary>
    public string CestaKniznice { get; set; } = "";

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Motiv { get; set; } = "System";

    /// <summary>The detail of a title right of the library; false (default) = left of it.</summary>
    public bool DetailVpravo { get; set; }

    public bool AutomatickeAktualizacie { get; set; } = true;

    public DateTime? PoslednaKontrolaAktualizaciiUtc { get; set; }

    /// <summary>The folder with ffprobe.exe and ffmpeg.exe; empty = found by itself (UNIVERSUM, KATALOG's own, PATH).</summary>
    public string PriecinokFfmpeg { get; set; } = "";

    /// <summary>The folder with WinSCP.com for servers (E8); empty = found by itself (its installation, PATH).</summary>
    public string PriecinokWinScp { get; set; } = "";

    /// <summary>Technical data of videos are read by themselves after a scan and when a disk is connected.</summary>
    public bool CitatTechnickeUdaje { get; set; } = true;

    /// <summary>The player's program (.exe); empty = the program Windows opens videos with.</summary>
    public string Prehravac { get; set; } = "";

    [JsonIgnore]
    public Jadro.Prehravanie.Prehravac SkutocnyPrehravac => string.IsNullOrWhiteSpace(Prehravac)
        ? Jadro.Prehravanie.Prehravac.Predvoleny
        : Jadro.Prehravanie.Prehravac.ZProgramu(Prehravac);

    /// <summary>The order of criteria for the best of more versions (duplicates), as numbers of <c>KriteriumVerzie</c>.</summary>
    public string PoradieKvality { get; set; } = "1,2,3,4,5";

    /// <summary>TMDB is asked for names, plots, posters and collections (with its key: own, else UNIVERSUM's).</summary>
    public bool PouzivatTmdb { get; set; } = true;

    /// <summary>HISTRA's catalogue is searched and the user's lists can be read (with its token: own, else UNIVERSUM's).</summary>
    public bool PouzivatHistra { get; set; } = true;

    /// <summary>The user's Jellyfin server: collections and what was watched there (address and user; the key is a secret).</summary>
    public bool PouzivatJellyfin { get; set; }

    public string JellyfinAdresa { get; set; } = "";

    /// <summary>The user's name on the server; empty when the server has one user.</summary>
    public string JellyfinPouzivatel { get; set; } = "";

    /// <summary>How labels were printed last time (format, QR, frame, shift of the printer), used again next time.</summary>
    public Vystupy.MoznostiStitkov Stitky { get; set; } = new();

    /// <summary>The private web catalogue (E9): its folder, the name on its page and what it shows. Never the password.</summary>
    public string PriecinokWebu { get; set; } = "";

    public string NazovWebu { get; set; } = "";

    public bool PlagatyNaWebe { get; set; } = true;

    public bool CestyNaWebe { get; set; } = true;

    /// <summary>A version the user chose to skip ("Túto verziu vynechať").</summary>
    public string? VynechanaVerzia { get; set; }

    /// <summary>Where these settings are saved (the self-test uses its own folder).</summary>
    [JsonIgnore]
    public string Subor { get; private set; } = "";

    public static string PredvolenyPriecinok =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Produkt.Nazov);

    [JsonIgnore]
    public string SkutocnaCestaKniznice => string.IsNullOrWhiteSpace(CestaKniznice)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Produkt.Nazov, "Kniznica" + Produkt.PriponaKniznice)
        : CestaKniznice;

    public static Nastavenia Nacitaj(string? priecinok = null)
    {
        var subor = Path.Combine(priecinok ?? PredvolenyPriecinok, "nastavenia.json");
        Nastavenia? nastavenia = null;
        try
        {
            if (File.Exists(subor))
            {
                nastavenia = JsonSerializer.Deserialize<Nastavenia>(File.ReadAllText(subor));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // damaged settings: start with defaults, the library itself is untouched
        }

        nastavenia ??= new Nastavenia();
        nastavenia.Subor = subor;
        return nastavenia;
    }

    public void Uloz()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Subor)!);
        var docasny = Subor + ".tmp";
        File.WriteAllText(docasny, JsonSerializer.Serialize(this, Format));
        File.Move(docasny, Subor, overwrite: true);
    }
}
