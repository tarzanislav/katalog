using System.Security.Cryptography;
using Katalog.Jadro.Model;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>A picture kept by the library: its SHA-256 (the name of its file), kind and size.</summary>
public sealed record UlozenyObrazok(byte[] Odtlacok, DruhObrazka Druh, int Sirka, int Vyska, long Velkost)
{
    public string Meno => Convert.ToHexStringLower(Odtlacok);
}

/// <summary>
/// Pictures (previews now, posters later) lie as files next to the library, in the folder "&lt;library&gt;.obrazky",
/// named by their SHA-256: the same picture is kept once, and the library itself stays small. They are the user's
/// private data like the library and never leave the computer.
/// </summary>
public sealed class UlozisteObrazkov(string priecinok)
{
    public string Priecinok { get; } = priecinok;

    /// <summary>The folder of pictures of a library file ("Kniznica.katalog" → "Kniznica.obrazky").</summary>
    public static UlozisteObrazkov PreKniznicu(string cestaKniznice) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cestaKniznice))!, Path.GetFileNameWithoutExtension(cestaKniznice) + ".obrazky"));

    /// <summary>Writes a JPEG (unless the same one is already there); its size the caller read from its header.</summary>
    public UlozenyObrazok Uloz(byte[] jpeg, DruhObrazka druh, int sirka, int vyska)
    {
        var odtlacok = SHA256.HashData(jpeg);
        var obrazok = new UlozenyObrazok(odtlacok, druh, sirka, vyska, jpeg.Length);
        var cesta = Cesta(obrazok.Meno);
        if (!File.Exists(cesta))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
            var docasny = cesta + ".tmp";
            File.WriteAllBytes(docasny, jpeg);
            File.Move(docasny, cesta, overwrite: true);
        }

        return obrazok;
    }

    /// <summary>"ab/abcdef….jpg": 256 subfolders, so no folder holds too many files.</summary>
    public string Cesta(string meno) => Path.Combine(Priecinok, meno[..2], meno + ".jpg");

    /// <summary>The id of the picture in the library (written now when it is not there yet).</summary>
    public static long Zaregistruj(SqliteConnection spojenie, UlozenyObrazok obrazok, long teraz)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            INSERT INTO obrazok(odtlacok, druh, sirka, vyska, velkost, pridany) VALUES ($o, $d, $s, $v, $b, $t)
            ON CONFLICT(odtlacok) DO UPDATE SET druh = druh
            RETURNING id
            """;
        prikaz.Parameters.AddWithValue("$o", obrazok.Odtlacok);
        prikaz.Parameters.AddWithValue("$d", (int)obrazok.Druh);
        prikaz.Parameters.AddWithValue("$s", obrazok.Sirka);
        prikaz.Parameters.AddWithValue("$v", obrazok.Vyska);
        prikaz.Parameters.AddWithValue("$b", obrazok.Velkost);
        prikaz.Parameters.AddWithValue("$t", teraz);
        return (long)prikaz.ExecuteScalar()!;
    }
}
