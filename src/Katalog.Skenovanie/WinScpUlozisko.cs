using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Skenovanie;

/// <summary>
/// A server reached through the user's own WinSCP (SFTP, FTP, WebDAV, S3 – owner's decision, 8 Nov 2026): KATALOG names a
/// site saved in WinSCP and never sees a password or key; it only lists folders. WinSCP.com runs with the commands as
/// arguments (Unicode, so Slovak names come through) and reports in its XML log. The tree is read level by level, many
/// folders per connection, and kept for the scan.
/// </summary>
public sealed class WinScpUlozisko : IUlozisko
{
    /// <summary>Commands of one connection stay well under the 32 767 characters of a Windows command line.</summary>
    private const int NajviacZnakov = 28_000;

    private readonly Func<IReadOnlyList<string>, CancellationToken, string> _spusti;
    private readonly string _lokalita;
    private readonly string _zaklad;
    private Dictionary<string, List<PolozkaUloziska>>? _strom;
    private HashSet<string> _necitatelne = [];

    /// <param name="lokalita">The name of a site saved in WinSCP ("NAS", "Server/Filmy").</param>
    /// <param name="koren">The folder on the server, relative to "/" ("" for the root, "volume1/video").</param>
    /// <param name="spusti">Runs WinSCP.com with the commands and returns its XML log (tests give their own).</param>
    public WinScpUlozisko(string lokalita, string koren, Func<IReadOnlyList<string>, CancellationToken, string> spusti)
    {
        _lokalita = lokalita;
        _zaklad = "/" + koren.Trim('/');
        _spusti = spusti;
        Koren = $"{lokalita}:{_zaklad}";
    }

    /// <summary>The storage with WinSCP.com of this computer.</summary>
    public static WinScpUlozisko SWinScp(string winScpCom, string lokalita, string koren) =>
        new(lokalita, koren, (prikazy, zrusenie) => WinScp.Spusti(winScpCom, prikazy, zrusenie));

    public DruhDisku Druh => DruhDisku.Vzdialeny;

    /// <summary>"NAS:/volume1/video" – the site and the folder, as the window shows it.</summary>
    public string Koren { get; }

    /// <summary>Cancels a long reading of the tree (the scan's cancel).</summary>
    public CancellationToken Zrusenie { get; set; }

    /// <summary>The address that identifies the server: the WinSCP site (A10: the folder is the disk's root).</summary>
    public static string Adresa(string lokalita) => "winscp:" + lokalita;

    /// <summary>The identity when the server answers (its root folder can be listed), else null.</summary>
    public IdentitaZvazku? Identita()
    {
        try
        {
            Nacitaj();
            return new IdentitaZvazku(DruhDisku.Vzdialeny, null, null, null, _lokalita, null, null, Adresa(_lokalita));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks in one connection that the site opens and its folder can be listed (when adding a disk); throws
    /// <see cref="IOException"/> or <see cref="DirectoryNotFoundException"/> with what WinSCP said.
    /// </summary>
    public void Over()
    {
        var prikazy = new List<string> { "option batch continue", "option confirm off", "open " + WinScp.Uvodzovky(_lokalita), "ls " + WinScp.Uvodzovky(_zaklad.TrimEnd('/') + "/"), "exit" };
        var vysledok = WinScp.CitajLog(_spusti(prikazy, Zrusenie));
        if (!vysledok.Pripojene)
        {
            throw new IOException($"WinSCP sa nepripojil k lokalite „{_lokalita}“: {vysledok.Chyba ?? "neznáma chyba"}. " +
                                  "Ulož si v nej heslo (alebo kľúč) a pripoj sa ňou raz vo WinSCP, aby poznal kľúč servera.");
        }

        if (!vysledok.Vypisy.ContainsKey(NormalizujCestu(_zaklad)))
        {
            throw new DirectoryNotFoundException($"Priečinok {_zaklad} na serveri „{_lokalita}“ nie je alebo sa nedá prečítať.");
        }
    }

    /// <summary>A server tells no size or free space over these protocols.</summary>
    public (long? Velkost, long? Volne) Miesto() => (null, null);

    public IEnumerable<PolozkaUloziska> Polozky(string relativnaCesta)
    {
        var strom = Nacitaj();
        var kluc = relativnaCesta.Trim('/');
        if (_necitatelne.Contains(kluc))
        {
            throw new UnauthorizedAccessException($"Priečinok {kluc} sa na serveri nedá prečítať.");
        }

        return strom.TryGetValue(kluc, out var polozky) ? polozky : throw new DirectoryNotFoundException($"Priečinok {kluc} na serveri nie je.");
    }

    /// <summary>
    /// The whole tree, read once: the root, then all folders of each next level in as few connections as the command
    /// line allows. Links are left out (a link to a folder above could loop).
    /// </summary>
    private Dictionary<string, List<PolozkaUloziska>> Nacitaj()
    {
        if (_strom is not null)
        {
            return _strom;
        }

        var strom = new Dictionary<string, List<PolozkaUloziska>>(StringComparer.Ordinal);
        var necitatelne = new HashSet<string>(StringComparer.Ordinal);
        var uroven = new List<string> { "" };
        var prvy = true;
        while (uroven.Count > 0)
        {
            var dalsia = new List<string>();
            foreach (var davka in Davky(uroven))
            {
                Zrusenie.ThrowIfCancellationRequested();
                var prikazy = new List<string> { "option batch continue", "option confirm off", "open " + WinScp.Uvodzovky(_lokalita) };
                prikazy.AddRange(davka.Select(c => "ls " + WinScp.Uvodzovky(PlnaCesta(c).TrimEnd('/') + "/")));
                prikazy.Add("exit");
                var vysledok = WinScp.CitajLog(_spusti(prikazy, Zrusenie));
                if (!vysledok.Pripojene)
                {
                    throw new IOException($"WinSCP sa nepripojil k lokalite „{_lokalita}“: {vysledok.Chyba ?? "neznáma chyba"}. " +
                                          "Ulož si v nej heslo (alebo kľúč) a pripoj sa ňou raz vo WinSCP, aby poznal kľúč servera.");
                }

                foreach (var cesta in davka)
                {
                    if (!vysledok.Vypisy.TryGetValue(NormalizujCestu(PlnaCesta(cesta)), out var polozky))
                    {
                        if (prvy)
                        {
                            throw new DirectoryNotFoundException($"Priečinok {_zaklad} na serveri „{_lokalita}“ nie je alebo sa nedá prečítať.");
                        }

                        necitatelne.Add(cesta);
                        continue;
                    }

                    var vlastne = polozky.Where(p => p.Druh != DruhPolozkyServera.Odkaz && p.Nazov is not ("." or ".."))
                        .Select(p => new PolozkaUloziska(p.Nazov, p.Druh == DruhPolozkyServera.Priecinok, p.Velkost, p.Zmeneny, null, 0))
                        .ToList();
                    strom[cesta] = vlastne;
                    dalsia.AddRange(vlastne.Where(p => p.JePriecinok).Select(p => cesta.Length == 0 ? p.Nazov : cesta + "/" + p.Nazov));
                }

                prvy = false;
            }

            uroven = dalsia;
        }

        _necitatelne = necitatelne;
        _strom = strom;
        return strom;
    }

    private string PlnaCesta(string relativna) => relativna.Length == 0 ? _zaklad : (_zaklad == "/" ? "" : _zaklad) + "/" + relativna;

    private static string NormalizujCestu(string cesta) => cesta.Length > 1 ? cesta.TrimEnd('/') : cesta;

    private IEnumerable<List<string>> Davky(List<string> cesty)
    {
        var davka = new List<string>();
        var znakov = 200 + _lokalita.Length;
        foreach (var cesta in cesty)
        {
            // each command is quoted twice (WinSCP's own quotes, then the command line's)
            var dlzka = (2 * (PlnaCesta(cesta).Length + 8)) + 8;
            if (davka.Count > 0 && znakov + dlzka > NajviacZnakov)
            {
                yield return davka;
                davka = [];
                znakov = 200 + _lokalita.Length;
            }

            davka.Add(cesta);
            znakov += dlzka;
        }

        if (davka.Count > 0)
        {
            yield return davka;
        }
    }
}

public enum DruhPolozkyServera
{
    Subor,
    Priecinok,
    Odkaz,
}

/// <summary>An entry of a folder as WinSCP's log reports it.</summary>
public sealed record PolozkaServera(string Nazov, DruhPolozkyServera Druh, long Velkost, long Zmeneny);

/// <summary>What one WinSCP.com run reported: whether the site opened, its error, and the listings by folder.</summary>
public sealed record VysledokWinScp(bool Pripojene, string? Chyba, IReadOnlyDictionary<string, IReadOnlyList<PolozkaServera>> Vypisy);

/// <summary>Finding and running WinSCP.com, its saved sites and its XML log.</summary>
public static class WinScp
{
    /// <summary>WinSCP.com in the folder chosen in Settings, else where the installer puts it, else on the PATH.</summary>
    public static string? Najdi(string? priecinok = null)
    {
        var kandidati = new List<string>();
        if (!string.IsNullOrWhiteSpace(priecinok))
        {
            kandidati.Add(Path.Combine(priecinok, "WinSCP.com"));
        }

        foreach (var programy in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            if (Environment.GetFolderPath(programy) is { Length: > 0 } p)
            {
                kandidati.Add(Path.Combine(p, "WinSCP", "WinSCP.com"));
            }
        }

        kandidati.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinSCP", "WinSCP.com"));
        kandidati.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p.Trim('"'), "WinSCP.com")));
        return kandidati.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The sites saved in WinSCP: in its WinSCP.ini next to the program (a portable WinSCP), else in the registry of the
    /// user. Only names are read – never a password.
    /// </summary>
    public static IReadOnlyList<string> UlozeneLokality(string winScpCom)
    {
        var ini = Path.Combine(Path.GetDirectoryName(winScpCom) ?? "", "WinSCP.ini");
        IEnumerable<string> surove = File.Exists(ini) ? LokalityZIni(File.ReadAllLines(ini)) : LokalityZRegistra();
        return surove.Select(Dekoduj).Where(n => n.Length > 0 && n != "Default Settings").Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Section names "[Sessions\Moja%20lokalita]" of WinSCP.ini.</summary>
    public static IEnumerable<string> LokalityZIni(IEnumerable<string> riadky)
    {
        const string predpona = @"[Sessions\";
        foreach (var riadok in riadky)
        {
            var r = riadok.Trim();
            if (r.StartsWith(predpona, StringComparison.OrdinalIgnoreCase) && r.EndsWith(']'))
            {
                yield return r[predpona.Length..^1];
            }
        }
    }

    private static IEnumerable<string> LokalityZRegistra()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        using var kluc = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Martin Prikryl\WinSCP 2\Sessions");
        return kluc?.GetSubKeyNames() ?? [];
    }

    /// <summary>WinSCP keeps names percent-encoded ("Moja%20lokalita"); a folder of sites is a slash.</summary>
    public static string Dekoduj(string nazov)
    {
        try
        {
            return Uri.UnescapeDataString(nazov);
        }
        catch (UriFormatException)
        {
            return nazov;
        }
    }

    /// <summary>A value for a WinSCP command in its own quotes (a quote inside doubled).</summary>
    public static string Uvodzovky(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The command line of WinSCP.com: the XML log and each command as one quoted argument.</summary>
    public static string Argumenty(string xmlLog, IEnumerable<string> prikazy)
    {
        var text = new StringBuilder();
        text.Append("/xmllog=").Append(Uvodzovky(xmlLog)).Append(" /xmlgroups /nointeractiveinput /command");
        foreach (var prikaz in prikazy)
        {
            text.Append(' ').Append(Uvodzovky(prikaz));
        }

        return text.ToString();
    }

    /// <summary>Runs WinSCP.com once (hidden, at most 30 minutes) and returns its XML log.</summary>
    public static string Spusti(string winScpCom, IReadOnlyList<string> prikazy, CancellationToken zrusenie)
    {
        var log = Path.Combine(Path.GetTempPath(), $"{Jadro.Produkt.Nazov}-winscp-{Guid.NewGuid():N}.xml");
        try
        {
            using var proces = new Process
            {
                StartInfo = new ProcessStartInfo(winScpCom, Argumenty(log, prikazy))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            proces.Start();
            proces.StandardInput.Close();
            // the console output is not needed (the XML log says it all), but must be read so WinSCP never waits on it
            _ = proces.StandardOutput.ReadToEndAsync(zrusenie);
            _ = proces.StandardError.ReadToEndAsync(zrusenie);
            using (zrusenie.Register(() => Zabi(proces)))
            {
                if (!proces.WaitForExit(TimeSpan.FromMinutes(30)))
                {
                    Zabi(proces);
                    throw new IOException("WinSCP neodpovedal ani za 30 minút.");
                }
            }

            zrusenie.ThrowIfCancellationRequested();
            return File.Exists(log) ? File.ReadAllText(log, Encoding.UTF8) : throw new IOException("WinSCP nezapísal svoj záznam.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException("WinSCP sa nedá spustiť: " + ex.Message, ex);
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static void Zabi(Process proces)
    {
        try
        {
            proces.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Reads WinSCP's XML log: whether "open" succeeded, the first error, and every successful "ls" by its folder. A log cut
    /// short (WinSCP killed) gives what it holds.
    /// </summary>
    public static VysledokWinScp CitajLog(string xml)
    {
        var vypisy = new Dictionary<string, IReadOnlyList<PolozkaServera>>(StringComparer.Ordinal);
        string? chyba = null;
        var pripojene = false;
        var nastavenia = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true };
        try
        {
            using var citac = XmlReader.Create(new StringReader(xml), nastavenia);
            string? skupina = null;
            while (citac.Read())
            {
                if (citac.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (citac.LocalName)
                {
                    case "group":
                        skupina = citac.GetAttribute("name");
                        break;
                    case "message" when chyba is null:
                        chyba = citac.ReadElementContentAsString().Trim();
                        break;
                    case "ls":
                        var (cesta, polozky, uspech) = CitajLs(citac.ReadSubtree());
                        // an "ls" that answered means the site is open
                        pripojene |= uspech;
                        if (uspech && cesta is not null)
                        {
                            vypisy[cesta.Length > 1 ? cesta.TrimEnd('/') : cesta] = polozky;
                        }

                        break;
                    case "result" when skupina?.StartsWith("open", StringComparison.OrdinalIgnoreCase) == true:
                        pripojene |= citac.GetAttribute("success") == "true";
                        break;
                }
            }
        }
        catch (XmlException)
        {
            // cut short: what was read stays
        }

        return new VysledokWinScp(pripojene, chyba, vypisy);
    }

    private static (string? Cesta, IReadOnlyList<PolozkaServera> Polozky, bool Uspech) CitajLs(XmlReader ls)
    {
        string? cesta = null;
        var uspech = false;
        var polozky = new List<PolozkaServera>();
        string? nazov = null, druh = null, velkost = null, zmena = null;
        var vSubore = false;
        while (ls.Read())
        {
            if (ls.NodeType == XmlNodeType.EndElement && ls.LocalName == "file" && vSubore)
            {
                if (nazov is not null)
                {
                    polozky.Add(new PolozkaServera(nazov,
                        druh?.ToLowerInvariant() switch { "d" => DruhPolozkyServera.Priecinok, "l" => DruhPolozkyServera.Odkaz, _ => DruhPolozkyServera.Subor },
                        long.TryParse(velkost, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0,
                        DateTimeOffset.TryParse(zmena, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var z) ? z.UtcTicks : 0));
                }

                vSubore = false;
                continue;
            }

            if (ls.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (ls.LocalName)
            {
                case "destination" when !vSubore:
                    cesta = ls.GetAttribute("value");
                    break;
                case "file":
                    vSubore = true;
                    nazov = druh = velkost = zmena = null;
                    break;
                case "filename" when vSubore:
                    nazov = ls.GetAttribute("value");
                    break;
                case "type" when vSubore:
                    druh = ls.GetAttribute("value");
                    break;
                case "size" when vSubore:
                    velkost = ls.GetAttribute("value");
                    break;
                case "modification" when vSubore:
                    zmena = ls.GetAttribute("value");
                    break;
                case "result" when !vSubore:
                    uspech = ls.GetAttribute("success") == "true";
                    break;
            }
        }

        return (cesta, polozky, uspech);
    }
}
