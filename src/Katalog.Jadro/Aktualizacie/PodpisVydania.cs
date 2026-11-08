using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Katalog.Jadro.Aktualizacie;

/// <summary>
/// Signature of a release: the checksum list SHA256SUMS.txt of every release is signed with the KATALOG release key
/// (ECDSA P-256, SHA-256). The public half is built into the program, the private half exists only as the secret
/// KATALOG_RELEASE_KEY of the GitHub environment "release". A changed or replaced installer – even one uploaded to
/// the release by someone else – is therefore never started.
/// </summary>
public static class PodpisVydania
{
    /// <summary>
    /// Public release key (SubjectPublicKeyInfo, Base64). Empty until the owner creates the key with
    /// tools/Nastav-Vydania.ps1; while empty, KATALOG never installs an update itself and only opens the release page.
    /// </summary>
    public const string VerejnyKluc = "";

    public static bool JeNastaveny => VerejnyKluc.Length > 0;

    /// <summary>True when <paramref name="podpis"/> (IEEE P1363 or DER) signs <paramref name="data"/> with the key.</summary>
    public static bool Over(ReadOnlySpan<byte> data, ReadOnlySpan<byte> podpis, string verejnyKluc = VerejnyKluc)
    {
        if (verejnyKluc.Length == 0)
        {
            return false;
        }

        try
        {
            using var kluc = ECDsa.Create();
            kluc.ImportSubjectPublicKeyInfo(Convert.FromBase64String(verejnyKluc), out _);
            return kluc.VerifyData(data, podpis, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation) ||
                   kluc.VerifyData(data, podpis, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// SHA-256 (upper-case hex) that the installer of <paramref name="verzia"/> must have, taken from a checksum list signed
    /// by the release key; null when the list is not signed by the key, belongs to another version (its first line
    /// "# KATALOG 1.2.3" is signed too, so an older release cannot pose as a newer one) or does not name the installer.
    /// </summary>
    public static string? OcakavanySucetInstalatora(byte[] zoznam, byte[] podpis, Version verzia, string verejnyKluc = VerejnyKluc)
    {
        if (!Over(zoznam, podpis, verejnyKluc))
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(zoznam);
        var hlavicka = text.Split('\n')[0].Trim();
        return hlavicka == RiadokVerzie(verzia) && ZoznamSuctov.Citaj(text).TryGetValue(NazovInstalatora(verzia), out var sucet) ? sucet : null;
    }

    /// <summary>First line of the signed checksum list of a release.</summary>
    public static string RiadokVerzie(Version verzia) => "# " + Produkt.Nazov + " " + verzia.ToString(3);

    /// <summary>The only installer name a release may have ("KATALOG_Setup_1.2.3.exe").</summary>
    public static string NazovInstalatora(Version verzia) =>
        string.Create(CultureInfo.InvariantCulture, $"{Produkt.Nazov}_Setup_{verzia.ToString(3)}.exe");
}

/// <summary>Reads a SHA256SUMS.txt list ("HASH  name" per line, comments start with #).</summary>
public static class ZoznamSuctov
{
    public static IReadOnlyDictionary<string, string> Citaj(string text)
    {
        var vysledok = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var surovy in text.Split('\n'))
        {
            var riadok = surovy.Trim();
            if (riadok.Length == 0 || riadok.StartsWith('#'))
            {
                continue;
            }

            var medzera = riadok.IndexOf(' ', StringComparison.Ordinal);
            if (medzera != 64)
            {
                continue;
            }

            var sucet = riadok[..64];
            var nazov = riadok[64..].TrimStart(' ', '*');
            if (nazov.Length > 0 && sucet.All(Uri.IsHexDigit))
            {
                vysledok[nazov] = sucet.ToUpperInvariant();
            }
        }

        return vysledok;
    }
}
