using System.Text;

namespace Katalog.Jadro.Prehravanie;

/// <summary>Players KATALOG knows how to hand a file, a playlist, a Blu-ray or a DVD to.</summary>
public enum DruhPrehravaca
{
    /// <summary>The program Windows opens the file with.</summary>
    Predvoleny = 0,
    Vlc = 1,
    MpcHc = 2,
    MpcBe = 3,
    PotPlayer = 4,
    Mpv = 5,

    /// <summary>Another program: it gets the paths of the files.</summary>
    Iny = 6,
}

/// <summary>A player: its kind and, unless it is the Windows default, its program (.exe).</summary>
public sealed record Prehravac(DruhPrehravaca Druh, string? Program)
{
    public static Prehravac Predvoleny { get; } = new(DruhPrehravaca.Predvoleny, null);

    /// <summary>A player by the path of its program; the kind comes from the program's file name.</summary>
    public static Prehravac ZProgramu(string program)
    {
        var meno = MenoProgramu(program).ToLowerInvariant();
        var druh = meno switch
        {
            "vlc" => DruhPrehravaca.Vlc,
            _ when meno.StartsWith("mpc-hc", StringComparison.Ordinal) => DruhPrehravaca.MpcHc,
            _ when meno.StartsWith("mpc-be", StringComparison.Ordinal) => DruhPrehravaca.MpcBe,
            _ when meno.StartsWith("potplayer", StringComparison.Ordinal) => DruhPrehravaca.PotPlayer,
            "mpv" => DruhPrehravaca.Mpv,
            _ => DruhPrehravaca.Iny,
        };
        return new Prehravac(druh, program);
    }

    public string Nazov => Druh switch
    {
        DruhPrehravaca.Predvoleny => "Predvolený prehrávač Windows",
        DruhPrehravaca.Vlc => "VLC",
        DruhPrehravaca.MpcHc => "MPC-HC",
        DruhPrehravaca.MpcBe => "MPC-BE",
        DruhPrehravaca.PotPlayer => "PotPlayer",
        DruhPrehravaca.Mpv => "mpv",
        _ => Program is null ? "Iný prehrávač" : MenoProgramu(Program),
    };

    /// <summary>"C:\Program Files\VideoLAN\VLC\vlc.exe" → "vlc" (Windows paths, also when read elsewhere).</summary>
    private static string MenoProgramu(string program)
    {
        var meno = program[(program.LastIndexOfAny(['\\', '/']) + 1)..];
        var bodka = meno.LastIndexOf('.');
        return bodka > 0 ? meno[..bodka] : meno;
    }

    /// <summary>Whether it plays a Blu-ray or DVD folder by itself (the Windows default usually does not).</summary>
    public bool PrehraStrukturuDisku => Druh is not (DruhPrehravaca.Predvoleny or DruhPrehravaca.Iny);
}

/// <summary>What is played: a file, or the folder that holds a Blu-ray (BDMV) or DVD (VIDEO_TS) structure.</summary>
public enum DruhPolozkyPrehravania
{
    Subor = 0,
    BluRay = 1,
    Dvd = 2,
}

/// <param name="Cesta">The full path of the file, or of the folder that holds BDMV or VIDEO_TS.</param>
public sealed record PolozkaPrehravania(string Cesta, DruhPolozkyPrehravania Druh);

/// <summary>How to start the player.</summary>
/// <param name="Program">The player's program, or (with <see cref="CezShell"/>) the file Windows opens.</param>
/// <param name="Playlist">When not null: the content of a playlist to write to <see cref="Program"/> first.</param>
public sealed record Spustenie(string Program, string Argumenty, bool CezShell, string? Playlist);

/// <summary>
/// How each player is given what to play. Players differ: VLC opens a Blu-ray as bluray:///path, MPC-HC and MPC-BE open
/// BDMV\index.bdmv, mpv needs bd:// with the folder as its device; several files are passed together, or as an M3U8
/// playlist to the Windows default.
/// </summary>
public static class PrikazyPrehravania
{
    /// <summary>The structure of a disc a folder holds: its own name (BDMV, VIDEO_TS) or the name of a subfolder.</summary>
    public static DruhPolozkyPrehravania? StrukturaDisku(IEnumerable<string> podpriecinky)
    {
        foreach (var nazov in podpriecinky)
        {
            if (nazov.Equals("BDMV", StringComparison.OrdinalIgnoreCase))
            {
                return DruhPolozkyPrehravania.BluRay;
            }

            if (nazov.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
            {
                return DruhPolozkyPrehravania.Dvd;
            }
        }

        return null;
    }

    /// <summary>The file that stands for a disc structure (for players that open a file, and for Windows).</summary>
    public static string HlavnySubor(PolozkaPrehravania polozka) => polozka.Druh switch
    {
        DruhPolozkyPrehravania.BluRay => Path.Combine(polozka.Cesta, "BDMV", "index.bdmv"),
        DruhPolozkyPrehravania.Dvd => Path.Combine(polozka.Cesta, "VIDEO_TS", "VIDEO_TS.IFO"),
        _ => polozka.Cesta,
    };

    /// <param name="suborPlaylistu">Where a playlist goes when one is needed (a temporary .m3u8 file).</param>
    public static Spustenie Priprav(Prehravac prehravac, IReadOnlyList<PolozkaPrehravania> polozky, string suborPlaylistu)
    {
        ArgumentOutOfRangeException.ThrowIfZero(polozky.Count);
        if (prehravac.Druh == DruhPrehravaca.Predvoleny || prehravac.Program is null)
        {
            return polozky.Count == 1
                ? new Spustenie(HlavnySubor(polozky[0]), "", CezShell: true, Playlist: null)
                : new Spustenie(suborPlaylistu, "", CezShell: true, Playlist(polozky.Select(HlavnySubor)));
        }

        var argumenty = new List<string>();
        foreach (var polozka in polozky)
        {
            switch (prehravac.Druh, polozka.Druh)
            {
                case (_, DruhPolozkyPrehravania.Subor):
                    argumenty.Add(Argument(polozka.Cesta));
                    break;
                case (DruhPrehravaca.Vlc, DruhPolozkyPrehravania.BluRay):
                    argumenty.Add(Argument("bluray:///" + UriCesta(polozka.Cesta)));
                    break;
                case (DruhPrehravaca.Vlc, DruhPolozkyPrehravania.Dvd):
                    argumenty.Add(Argument("dvd:///" + UriCesta(polozka.Cesta)));
                    break;
                case (DruhPrehravaca.Mpv, DruhPolozkyPrehravania.BluRay):
                    argumenty.Add("bd:// --bluray-device=" + Argument(polozka.Cesta));
                    break;
                case (DruhPrehravaca.Mpv, DruhPolozkyPrehravania.Dvd):
                    argumenty.Add("dvd:// --dvd-device=" + Argument(Path.Combine(polozka.Cesta, "VIDEO_TS")));
                    break;
                default:
                    argumenty.Add(Argument(HlavnySubor(polozka)));
                    break;
            }
        }

        return new Spustenie(prehravac.Program, string.Join(' ', argumenty), CezShell: false, Playlist: null);
    }

    /// <summary>An M3U8 playlist (UTF-8) of full paths.</summary>
    public static string Playlist(IEnumerable<string> cesty)
    {
        var text = new StringBuilder("#EXTM3U\r\n");
        foreach (var cesta in cesty)
        {
            text.Append(cesta).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// One command-line argument quoted as Windows programs read it (CommandLineToArgvW): in quotes, with quotes escaped and
    /// backslashes before a quote doubled.
    /// </summary>
    public static string Argument(string text)
    {
        if (text.Length > 0 && !text.Any(z => char.IsWhiteSpace(z) || z == '"'))
        {
            return text;
        }

        var vysledok = new StringBuilder("\"");
        var lomky = 0;
        foreach (var znak in text)
        {
            if (znak == '\\')
            {
                lomky++;
                continue;
            }

            if (znak == '"')
            {
                vysledok.Append('\\', lomky * 2 + 1).Append('"');
            }
            else
            {
                vysledok.Append('\\', lomky).Append(znak);
            }

            lomky = 0;
        }

        // backslashes before the closing quote are doubled, so they do not escape it
        return vysledok.Append('\\', lomky * 2).Append('"').ToString();
    }

    /// <summary>"D:\Filmy\Disk 1" → "D:/Filmy/Disk 1/" (the form VLC takes after bluray:///).</summary>
    private static string UriCesta(string cesta) => cesta.Replace('\\', '/').TrimEnd('/') + "/";
}
