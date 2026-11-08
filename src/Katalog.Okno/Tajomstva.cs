using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Katalog.Zdroje;

namespace Katalog.Okno;

/// <summary>The sources' keys this program uses now (own or UNIVERSUM's), for building the sources and showing in Settings.</summary>
internal sealed record KluceProgramu(KlucZdroja Tmdb, KlucZdroja Histra, string? Jellyfin);

/// <summary>
/// Keys of the sources (decision A8): outside the library, in %APPDATA%\KATALOG\tajomstva.json, each encrypted by the
/// Windows data protection for the current user – only the same Windows account on the same computer can read them.
/// Never shown again after saving, never written to the library, logs or reports.
/// </summary>
internal sealed class Tajomstva
{
    private static readonly byte[] Entropia = "KATALOG secret v1"u8.ToArray();

    /// <summary>UNIVERSUM protects its keys the same way with its own entropy (read only, never written).</summary>
    private static readonly byte[] EntropiaUniversum = "UNIVERSUM secret v1"u8.ToArray();

    private readonly string _subor;
    private Dictionary<string, string> _chranene;

    private Tajomstva(string subor, Dictionary<string, string> chranene)
    {
        _subor = subor;
        _chranene = chranene;
    }

    public static Tajomstva Nacitaj(string priecinok)
    {
        var subor = Path.Combine(priecinok, "tajomstva.json");
        Dictionary<string, string>? chranene = null;
        try
        {
            if (File.Exists(subor))
            {
                chranene = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(subor));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new Tajomstva(subor, chranene ?? []);
    }

    public bool Ma(string meno) => _chranene.ContainsKey(meno);

    public string? Citaj(string meno) => _chranene.TryGetValue(meno, out var chraneny) ? Odchran(chraneny, Entropia) : null;

    /// <summary>Stores a secret encrypted (an empty one removes it).</summary>
    public void Zapis(string meno, string? hodnota)
    {
        var nove = new Dictionary<string, string>(_chranene);
        if (string.IsNullOrWhiteSpace(hodnota))
        {
            nove.Remove(meno);
        }
        else
        {
            nove[meno] = Convert.ToBase64String(Transformuj(Encoding.UTF8.GetBytes(hodnota.Trim()), Entropia, chranit: true));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_subor)!);
        var docasny = _subor + ".tmp";
        File.WriteAllText(docasny, JsonSerializer.Serialize(nove));
        File.Move(docasny, _subor, overwrite: true);
        _chranene = nove;
    }

    /// <summary>The keys to use: own ones first, else UNIVERSUM's (read each time: the user may have changed them there).</summary>
    public KluceProgramu Kluce()
    {
        var (tmdb, histra) = KluceZdrojov.Universum(KluceZdrojov.SuborUniversum, t => Odchran(t, EntropiaUniversum));
        return new KluceProgramu(KluceZdrojov.Vyber(Citaj("tmdb"), tmdb), KluceZdrojov.Vyber(Citaj("histra"), histra), Citaj("jellyfin"));
    }

    private static string? Odchran(string chraneny, byte[] entropia)
    {
        try
        {
            return Encoding.UTF8.GetString(Transformuj(Convert.FromBase64String(chraneny), entropia, chranit: false));
        }
        catch (Exception ex) when (ex is FormatException or Win32Exception)
        {
            return null;
        }
    }

    private static byte[] Transformuj(byte[] data, byte[] entropia, bool chranit)
    {
        var vstup = GCHandle.Alloc(data, GCHandleType.Pinned);
        var ent = GCHandle.Alloc(entropia, GCHandleType.Pinned);
        try
        {
            var vstupnyBlob = new Blob { Velkost = data.Length, Data = vstup.AddrOfPinnedObject() };
            var entropiaBlob = new Blob { Velkost = entropia.Length, Data = ent.AddrOfPinnedObject() };
            var vystup = default(Blob);
            var ok = chranit
                ? CryptProtectData(ref vstupnyBlob, null, ref entropiaBlob, 0, 0, BezOkien, ref vystup)
                : CryptUnprotectData(ref vstupnyBlob, 0, ref entropiaBlob, 0, 0, BezOkien, ref vystup);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                var vysledok = new byte[vystup.Velkost];
                Marshal.Copy(vystup.Data, vysledok, 0, vystup.Velkost);
                return vysledok;
            }
            finally
            {
                LocalFree(vystup.Data);
            }
        }
        finally
        {
            vstup.Free();
            ent.Free();
        }
    }

    private const int BezOkien = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Velkost;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob dataIn, string? popis, ref Blob entropia, IntPtr rezervovane, IntPtr vyzva, int priznaky, ref Blob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr popis, ref Blob entropia, IntPtr rezervovane, IntPtr vyzva, int priznaky, ref Blob dataOut);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr pamat);
}
