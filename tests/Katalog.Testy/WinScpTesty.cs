using System.Net;
using System.Text;
using Katalog.Jadro.Model;
using Katalog.Skenovanie;

namespace Katalog.Testy;

/// <summary>Servers through WinSCP (stage E8): commands, its XML log and reading a tree – against an invented server.</summary>
public sealed class WinScpTesty
{
    /// <summary>
    /// An invented server answering as WinSCP.com's XML log does: a group per command, "ls" with its destination, files
    /// and result.
    /// </summary>
    private sealed class VymyslenyServer
    {
        public Dictionary<string, List<(string Nazov, string Druh, long Velkost)>> Priecinky { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Zakazane { get; } = new(StringComparer.Ordinal);

        public bool Pripojit { get; set; } = true;

        public int Spusteni { get; private set; }

        public List<IReadOnlyList<string>> Prikazy { get; } = [];

        public string Spusti(IReadOnlyList<string> prikazy, CancellationToken zrusenie)
        {
            Spusteni++;
            Prikazy.Add(prikazy);
            string E(string t) => WebUtility.HtmlEncode(t);
            var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><session xmlns="http://winscp.net/schema/session/1.0" name="tester@nas" start="2026-11-08T18:00:00.000Z">""");
            var otvorene = false;
            foreach (var prikaz in prikazy)
            {
                xml.Append($"""<group name="{E(prikaz)}">""");
                if (prikaz.StartsWith("open ", StringComparison.Ordinal))
                {
                    otvorene = Pripojit;
                    xml.Append(Pripojit
                        ? """<result success="true" />"""
                        : """<failure><message>Authentication failed.</message></failure><result success="false" />""");
                }
                else if (prikaz.StartsWith("ls ", StringComparison.Ordinal))
                {
                    var cesta = prikaz[4..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                    var kluc = cesta.Length > 1 ? cesta.TrimEnd('/') : cesta;
                    xml.Append($"""<ls><destination value="{E(cesta)}" />""");
                    if (otvorene && Priecinky.TryGetValue(kluc, out var polozky) && !Zakazane.Contains(kluc))
                    {
                        xml.Append("<files>");
                        foreach (var (nazov, druh, velkost) in new[] { (".", "d", 0L), ("..", "d", 0L) }.Concat(polozky))
                        {
                            xml.Append($"""<file><filename value="{E(nazov)}" /><type value="{druh}" />""");
                            if (druh == "-")
                            {
                                xml.Append($"""<size value="{velkost}" />""");
                            }

                            xml.Append("""<modification value="2026-11-08T17:41:00.000Z" /><permissions value="rw-r--r--" /></file>""");
                        }

                        xml.Append("""</files><result success="true" />""");
                    }
                    else
                    {
                        xml.Append("""<result success="false" /><failure><message>Permission denied.</message></failure>""");
                    }

                    xml.Append("</ls>");
                }
                else
                {
                    xml.Append("""<result success="true" />""");
                }

                xml.Append("</group>");
            }

            return xml.Append("</session>").ToString();
        }
    }

    private static VymyslenyServer Server()
    {
        var server = new VymyslenyServer();
        server.Priecinky["/volume1/video"] = [("Filmy", "d", 0), ("Seriály", "d", 0), ("odkaz", "l", 0), ("poznámka \"1\".txt", "-", 12)];
        server.Priecinky["/volume1/video/Filmy"] = [("Vymyslený film (2001).mkv", "-", 4_000_000_000), ("Súkromné", "d", 0)];
        server.Priecinky["/volume1/video/Filmy/Súkromné"] = [("tajné.mkv", "-", 10)];
        server.Priecinky["/volume1/video/Seriály"] = [("Vymyslený seriál S01E01.mkv", "-", 700_000_000)];
        return server;
    }

    [Test]
    public void StromServera()
    {
        var server = Server();
        server.Zakazane.Add("/volume1/video/Filmy/Súkromné");
        var ulozisko = new WinScpUlozisko("NAS doma", "/volume1/video/", server.Spusti);
        Over.Rovne("NAS doma:/volume1/video", ulozisko.Koren, "koreň");
        Over.Rovne(DruhDisku.Vzdialeny, ulozisko.Druh, "druh");

        var identita = ulozisko.Identita();
        Over.Pravda(identita is { Druh: DruhDisku.Vzdialeny, Adresa: "winscp:NAS doma", Menovka: "NAS doma" }, $"identita: {identita}");
        var koren = ulozisko.Polozky("").ToList();
        Over.Postupnost(["Filmy", "Seriály", "poznámka \"1\".txt"], koren.Select(p => p.Nazov), "bez . a .. a bez odkazu");
        Over.Pravda(koren[0].JePriecinok && !koren[2].JePriecinok && koren[2].Velkost == 12, "priečinky a súbory");
        Over.Rovne(new DateTime(2026, 11, 8, 17, 41, 0, DateTimeKind.Utc).Ticks, koren[2].Zmeneny, "čas zmeny v UTC");
        Over.Rovne(4_000_000_000L, ulozisko.Polozky("Filmy").Single(p => p.Nazov.EndsWith(".mkv", StringComparison.Ordinal)).Velkost, "veľký súbor");
        Over.Vyhodi<UnauthorizedAccessException>(() => ulozisko.Polozky("Filmy/Súkromné").ToList(), "nečitateľný priečinok");
        Over.Vyhodi<DirectoryNotFoundException>(() => ulozisko.Polozky("Iné").ToList(), "neexistujúci");

        // three levels, three connections; everything later from memory
        Over.Rovne(3, server.Spusteni, "po úrovniach");
        Over.Postupnost(["option batch continue", "option confirm off", "open \"NAS doma\"", "ls \"/volume1/video/\"", "exit"], server.Prikazy[0], "príkazy prvého spojenia");
        Over.Postupnost(["ls \"/volume1/video/Filmy/\"", "ls \"/volume1/video/Seriály/\""], server.Prikazy[1].Where(p => p.StartsWith("ls", StringComparison.Ordinal)), "druhá úroveň naraz");
        Over.Rovne(3, server.Spusteni, "nič ďalšie");
    }

    [Test]
    public void ChybyAVelkyStrom()
    {
        var server = Server();
        server.Pripojit = false;
        var ulozisko = new WinScpUlozisko("NAS", "volume1/video", server.Spusti);
        Over.Rovne(null, ulozisko.Identita(), "bez pripojenia nie je identita");
        var chyba = Over.Vyhodi<IOException>(() => ulozisko.Polozky("").ToList(), "bez pripojenia");
        Over.Pravda(chyba.Message.Contains("Authentication failed.", StringComparison.Ordinal), "správa WinSCP: " + chyba.Message);

        Over.Vyhodi<DirectoryNotFoundException>(() => new WinScpUlozisko("NAS", "nieje", Server().Spusti).Polozky("").ToList(), "zlý koreň");
        Over.Vyhodi<IOException>(() => ulozisko.Over(), "overenie bez pripojenia");
        Over.Vyhodi<DirectoryNotFoundException>(() => new WinScpUlozisko("NAS", "nieje", Server().Spusti).Over(), "overenie zlého koreňa");
        var dobry = Server();
        new WinScpUlozisko("NAS", "/volume1/video", dobry.Spusti).Over();
        Over.Rovne(1, dobry.Spusteni, "overenie jedným spojením");

        // many folders with long names: more connections, each under the length of a command line
        var velky = new VymyslenyServer();
        var mena = Enumerable.Range(0, 400).Select(i => $"Vymyslený priečinok s veľmi dlhým názvom číslo {i:000} " + new string('x', 60)).ToList();
        velky.Priecinky["/"] = mena.Select(m => (m, "d", 0L)).ToList();
        foreach (var m in mena)
        {
            velky.Priecinky["/" + m] = [("film.mkv", "-", 1)];
        }

        var ulozisko2 = new WinScpUlozisko("Server", "", velky.Spusti);
        Over.Rovne(400, ulozisko2.Polozky("").Count(), "koreň servera");
        Over.Rovne(1, ulozisko2.Polozky(mena[399]).Count(), "posledný priečinok");
        Over.Pravda(velky.Spusteni > 2, $"viac spojení ({velky.Spusteni})");
        Over.Pravda(velky.Prikazy.All(p => WinScp.Argumenty(@"C:\Temp\log.xml", p).Length < 32_000), "príkazový riadok pod limitom");
    }

    [Test]
    public void PrikazyALokality()
    {
        Over.Rovne("/xmllog=\"C:\\T\\l.xml\" /xmlgroups /nointeractiveinput /command \"open \"\"NAS\"\"\" \"ls \"\"/a \"\"\"\"b\"\"\"\"/\"\"\"",
            WinScp.Argumenty(@"C:\T\l.xml", ["open " + WinScp.Uvodzovky("NAS"), "ls " + WinScp.Uvodzovky("/a \"b\"/")]), "úvodzovky dvakrát");
        Over.Postupnost(["Moja%20lokalita", "Priečinok/NAS"],
            WinScp.LokalityZIni(["[Configuration]", @"[Sessions\Moja%20lokalita]", "HostName=nas", @"[Sessions\Priečinok/NAS]"]), "lokality z WinSCP.ini");
        Over.Rovne("Moja lokalita", WinScp.Dekoduj("Moja%20lokalita"), "dekódovanie");
        Over.Rovne("Zle%zz", WinScp.Dekoduj("Zle%zz"), "zlé kódovanie ostane");
        var vysledok = WinScp.CitajLog("<session><group name=\"open \"><result success=\"true\" /></group><group name=\"ls\"><ls><destination value=\"/x");
        Over.Pravda(vysledok.Pripojene && vysledok.Vypisy.Count == 0, "useknutý záznam");
    }
}
