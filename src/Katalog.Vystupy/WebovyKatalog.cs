using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Katalog.Vystupy;

/// <summary>A disk as the web catalogue shows it.</summary>
public sealed record DiskWebu(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("c")] string Cislo,
    [property: JsonPropertyName("n")] string Nazov,
    [property: JsonPropertyName("k")] string Katalog,
    [property: JsonPropertyName("d")] string Druh,
    [property: JsonPropertyName("vel")] long? Velkost,
    [property: JsonPropertyName("vol")] long? Volne,
    [property: JsonPropertyName("sub")] long Suborov,
    [property: JsonPropertyName("tit")] int Titulov,
    [property: JsonPropertyName("lez")] string? Umiestnenie,
    [property: JsonPropertyName("sken")] string? PoslednySken);

/// <summary>A file of a version: disk, path, size, quality; an episode's season and number.</summary>
public sealed record VerziaWebu(
    [property: JsonPropertyName("d")] long Disk,
    [property: JsonPropertyName("p")] string Cesta,
    [property: JsonPropertyName("b")] long Velkost,
    [property: JsonPropertyName("q")] string? Kvalita,
    [property: JsonPropertyName("s")] int? Seria,
    [property: JsonPropertyName("e")] int? Epizoda);

/// <summary>A title of the web catalogue; <see cref="Plagat"/> is the key of its poster in the poster packs.</summary>
public sealed record TitulWebu(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("s")] bool Serial,
    [property: JsonPropertyName("n")] string Nazov,
    [property: JsonPropertyName("o")] string? Original,
    [property: JsonPropertyName("r")] int? Rok,
    [property: JsonPropertyName("dej")] string? Dej,
    [property: JsonPropertyName("z")] IReadOnlyList<string> Zanre,
    [property: JsonPropertyName("rez")] IReadOnlyList<string> Rezia,
    [property: JsonPropertyName("her")] IReadOnlyList<string> Herci,
    [property: JsonPropertyName("kol")] IReadOnlyList<string> Kolekcie,
    [property: JsonPropertyName("st")] IReadOnlyList<string> Stitky,
    [property: JsonPropertyName("h")] double? Hodnotenie,
    [property: JsonPropertyName("m")] int? MojeHodnotenie,
    [property: JsonPropertyName("v")] bool Videne,
    [property: JsonPropertyName("w")] bool Zelany,
    [property: JsonPropertyName("mam")] bool Mam,
    [property: JsonPropertyName("q")] string? Kvalita,
    [property: JsonPropertyName("ep")] int Epizod,
    [property: JsonPropertyName("ver")] IReadOnlyList<VerziaWebu> Verzie,
    [property: JsonPropertyName("pl")] string? Plagat);

/// <summary>Everything of one web catalogue.</summary>
public sealed record ObsahWebu(
    [property: JsonPropertyName("nazov")] string Nazov,
    [property: JsonPropertyName("vytvorene")] string Vytvorene,
    [property: JsonPropertyName("program")] string Program,
    [property: JsonPropertyName("tituly")] IReadOnlyList<TitulWebu> Tituly,
    [property: JsonPropertyName("disky")] IReadOnlyList<DiskWebu> Disky)
{
    /// <summary>Where each poster lies: its pack's number by key (filled when the packs are written).</summary>
    [JsonPropertyName("balik")]
    public IReadOnlyDictionary<string, int> Baliky { get; init; } = new Dictionary<string, int>();
}

/// <summary>A poster for the web: its key (the title's) and a small JPEG.</summary>
public sealed record PlagatWebu(string Kluc, byte[] Jpeg);

/// <summary>What was written.</summary>
public sealed record VysledokWebu(int Titulov, int Plagatov, int Balikov, long Bajtov);

/// <summary>
/// The private web catalogue (stage E9): static pages in a folder, opened straight from the disk (or put anywhere by the
/// user), with everything encrypted by a password – AES-256-GCM with a key from PBKDF2-SHA-256 (600 000 rounds), the
/// same the browser's own Web Crypto computes. Without the password the folder holds only noise; the password is never
/// written anywhere. The data and the posters come as scripts (data.js, plagaty/NNN.js), because a page opened from the
/// disk may load scripts but not read files.
/// </summary>
public static class WebovyKatalog
{
    public const int Iteracie = 600_000;

    /// <summary>The shortest password the page accepts to be made with.</summary>
    public const int NajkratsieHeslo = 8;

    /// <summary>Posters in one pack (about half a megabyte): the page loads only the packs of the tiles it shows.</summary>
    public const int PlagatovVBaliku = 40;

    /// <summary>The file marking a folder as a web catalogue of this program (only such a folder is written over).</summary>
    public const string Znacka = "katalog-web.txt";

    private const byte VerziaFormatu = 1;

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The key of a password: PBKDF2 with SHA-256, 32 bytes.</summary>
    public static byte[] Kluc(string heslo, byte[] sol, int iteracie = Iteracie) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(heslo), sol, iteracie, HashAlgorithmName.SHA256, 32);

    /// <summary>Nonce (12 bytes) + ciphertext + tag (16 bytes).</summary>
    public static byte[] Zasifruj(ReadOnlySpan<byte> data, byte[] kluc)
    {
        var vysledok = new byte[12 + data.Length + 16];
        RandomNumberGenerator.Fill(vysledok.AsSpan(0, 12));
        using var aes = new AesGcm(kluc, 16);
        aes.Encrypt(vysledok.AsSpan(0, 12), data, vysledok.AsSpan(12, data.Length), vysledok.AsSpan(12 + data.Length));
        return vysledok;
    }

    public static byte[] Odsifruj(ReadOnlySpan<byte> sifra, byte[] kluc)
    {
        var data = new byte[sifra.Length - 28];
        using var aes = new AesGcm(kluc, 16);
        aes.Decrypt(sifra[..12], sifra[12..^16], sifra[^16..], data);
        return data;
    }

    /// <summary>
    /// The header of the data: format, salt (16 bytes) and rounds (4 bytes, big-endian), then the encrypted data – what the
    /// page needs to derive the key again.
    /// </summary>
    public static byte[] ZasifrujObsah(byte[] data, string heslo, out byte[] kluc, int iteracie = Iteracie)
    {
        var sol = RandomNumberGenerator.GetBytes(16);
        kluc = Kluc(heslo, sol, iteracie);
        return Hlavicka(sol, iteracie).Concat(Zasifruj(data, kluc)).ToArray();
    }

    private static byte[] Hlavicka(byte[] sol, int iteracie)
    {
        var hlavicka = new byte[21];
        hlavicka[0] = VerziaFormatu;
        sol.CopyTo(hlavicka, 1);
        hlavicka[17] = (byte)(iteracie >> 24);
        hlavicka[18] = (byte)(iteracie >> 16);
        hlavicka[19] = (byte)(iteracie >> 8);
        hlavicka[20] = (byte)iteracie;
        return hlavicka;
    }

    /// <summary>Opens what <see cref="ZasifrujObsah"/> made (tests; the page does the same in the browser).</summary>
    public static byte[] OdsifrujObsah(byte[] obsah, string heslo, out byte[] kluc, out int iteracie)
    {
        if (obsah.Length < 21 + 28 || obsah[0] != VerziaFormatu)
        {
            throw new CryptographicException("Neznámy formát webového katalógu.");
        }

        iteracie = (obsah[17] << 24) | (obsah[18] << 16) | (obsah[19] << 8) | obsah[20];
        kluc = Kluc(heslo, obsah[1..17], iteracie);
        return Odsifruj(obsah.AsSpan(21), kluc);
    }

    /// <summary>The page of the web catalogue (one file with its styles and script, no library from elsewhere).</summary>
    public static string Stranka()
    {
        using var prud = typeof(WebovyKatalog).Assembly.GetManifestResourceStream("Katalog.Vystupy.Web.index.html")
            ?? throw new InvalidOperationException("Chýba stránka webového katalógu.");
        using var citac = new StreamReader(prud, Encoding.UTF8);
        return citac.ReadToEnd().Replace("{{PROGRAM}}", Jadro.Produkt.Nazov, StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes the web catalogue into <paramref name="priecinok"/>: an empty folder, or one this program wrote before (its
    /// own files are replaced, nothing else is touched).
    /// </summary>
    public static VysledokWebu Vytvor(string priecinok, ObsahWebu obsah, IEnumerable<PlagatWebu> plagaty, string heslo,
        IProgress<string>? priebeh = null, int iteracie = Iteracie)
    {
        ArgumentNullException.ThrowIfNull(obsah);
        if (heslo.Length < NajkratsieHeslo)
        {
            throw new ArgumentException($"Heslo musí mať aspoň {NajkratsieHeslo} znakov.", nameof(heslo));
        }

        Directory.CreateDirectory(priecinok);
        var znacka = Path.Combine(priecinok, Znacka);
        if (Directory.EnumerateFileSystemEntries(priecinok).Any() && !File.Exists(znacka))
        {
            throw new IOException("Priečinok nie je prázdny. Vyber prázdny priečinok alebo ten, kam sa webový katalóg uložil predtým.");
        }

        var plagatyPriecinok = Path.Combine(priecinok, "plagaty");
        if (Directory.Exists(plagatyPriecinok))
        {
            foreach (var stary in Directory.EnumerateFiles(plagatyPriecinok, "*.js"))
            {
                File.Delete(stary);
            }
        }

        Directory.CreateDirectory(plagatyPriecinok);

        // the key: the data's own salt; the packs are encrypted with the same key (each with its own nonce)
        var sol = RandomNumberGenerator.GetBytes(16);
        priebeh?.Report("Odvodzujem kľúč z hesla…");
        var kluc = Kluc(heslo, sol, iteracie);
        var baliky = new Dictionary<string, int>(StringComparer.Ordinal);
        var bajtov = 0L;
        var balik = new Dictionary<string, string>(StringComparer.Ordinal);
        var cisloBaliku = 0;
        var plagatov = 0;
        void ZapisBalik()
        {
            if (balik.Count == 0)
            {
                return;
            }

            var sifra = Zasifruj(JsonSerializer.SerializeToUtf8Bytes(balik), kluc);
            bajtov += ZapisSkript(Path.Combine(plagatyPriecinok, $"{cisloBaliku:000}.js"), $"KATALOG.plagaty({cisloBaliku},\"{Convert.ToBase64String(sifra)}\");");
            cisloBaliku++;
            balik.Clear();
        }

        foreach (var plagat in plagaty)
        {
            if (baliky.ContainsKey(plagat.Kluc))
            {
                continue;
            }

            balik[plagat.Kluc] = Convert.ToBase64String(plagat.Jpeg);
            baliky[plagat.Kluc] = cisloBaliku;
            plagatov++;
            if (balik.Count == PlagatovVBaliku)
            {
                ZapisBalik();
                priebeh?.Report($"Plagáty: {plagatov}");
            }
        }

        ZapisBalik();

        priebeh?.Report("Šifrujem údaje…");
        var data = JsonSerializer.SerializeToUtf8Bytes(obsah with { Baliky = baliky }, Json);
        var zasifrovane = Hlavicka(sol, iteracie).Concat(Zasifruj(data, kluc)).ToArray();
        bajtov += ZapisSkript(Path.Combine(priecinok, "data.js"), $"KATALOG.data(\"{Convert.ToBase64String(zasifrovane)}\");");
        bajtov += ZapisSkript(Path.Combine(priecinok, "index.html"), Stranka());
        File.WriteAllText(znacka, $"{Jadro.Produkt.Nazov} – súkromný webový katalóg. Obsah je zašifrovaný heslom; otvor index.html.\r\n");
        return new VysledokWebu(obsah.Tituly.Count, plagatov, cisloBaliku, bajtov);
    }

    private static long ZapisSkript(string cesta, string obsah)
    {
        var docasny = cesta + ".tmp";
        File.WriteAllText(docasny, obsah, new UTF8Encoding(false));
        File.Move(docasny, cesta, overwrite: true);
        return new FileInfo(cesta).Length;
    }
}
