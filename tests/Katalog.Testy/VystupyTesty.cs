using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Katalog.Vystupy;

namespace Katalog.Testy;

/// <summary>QR codes, labels, tables (CSV, Excel, HTML) and PDF – all written in the program, so all tested here.</summary>
public sealed class VystupyTesty
{
    private static string Odtlacok(bool[,] moduly)
    {
        var n = moduly.GetLength(0);
        var text = new StringBuilder();
        for (var y = 0; y < n; y++)
        {
            if (y > 0)
            {
                text.Append('/');
            }

            for (var x = 0; x < n; x++)
            {
                text.Append(moduly[y, x] ? '1' : '0');
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(text.ToString())));
    }

    /// <summary>
    /// The modules agree bit for bit with an independent encoder (the Python library qrcode, byte mode, the same mask),
    /// across versions with and without the version blocks and all four levels; text with diacritics carries ECI UTF-8.
    /// </summary>
    [Test]
    public void QrKodZhodny()
    {
        var dlhy = string.Concat(Enumerable.Range(0, 300).Select(i => (char)('a' + (i % 26))));
        (string Text, UrovenOpravy Uroven, int Maska, int Velkost, string Odtlacok)[] pripady =
        [
            ("KATALOG D007", UrovenOpravy.M, 2, 21, "402c64ff3d7568d47f8aa6ba243b6a71d7dd63369a4abf674b298772bc852122"),
            // with diacritics the code starts with ECI 26 (UTF-8), which the Python library does not write: this one was
            // checked by two readers instead (OpenCV and ZBar read the same text back)
            ("Ľúbostný príbeh (1999)", UrovenOpravy.Q, 5, 29, "dd69e4a5db2a3860785fb0844c75dbb96fd8ea65a19f675e4838e96f23b207e7"),
            (dlhy, UrovenOpravy.L, 0, 61, "cdd033e9b3341c4b2ae29a78036ea263e708a00d363034cd6d8ff0899a183226"),
            (dlhy, UrovenOpravy.H, 7, 89, "9c566cd1f03e4a5ec499fc5645cd841df704477ea88b6288c00f25d1db76fcc6"),
        ];
        foreach (var p in pripady)
        {
            var kod = QrKod.Vytvor(p.Text, p.Uroven, p.Maska);
            Over.Rovne(p.Velkost, kod.GetLength(0), $"veľkosť {p.Uroven}");
            Over.Rovne(p.Odtlacok, Odtlacok(kod), $"moduly {p.Uroven} maska {p.Maska}");
        }
    }

    [Test]
    public void QrKodVerzieAMaska()
    {
        // version 1-M holds 14 bytes, the 15th needs version 2
        Over.Rovne(21, QrKod.Vytvor(new string('x', 14)).GetLength(0), "14 bajtov");
        Over.Rovne(25, QrKod.Vytvor(new string('x', 15)).GetLength(0), "15 bajtov");
        // diacritics count as their UTF-8 bytes with the ECI header: 6 letters (12 bytes) fit version 1-M, 7 do not
        Over.Rovne(21, QrKod.Vytvor("ľščťžý").GetLength(0), "diakritika v UTF-8");
        Over.Rovne(25, QrKod.Vytvor("ľščťžýá").GetLength(0), "diakritika a hlavička ECI");
        Over.Vyhodi<ArgumentException>(() => QrKod.Vytvor(new string('x', QrKod.NajdlhsiText + 1)), "príliš dlhý text");

        // the chosen mask is one of the eight, and the same text gives the same code
        var kod = QrKod.Vytvor("D042 Vymyslený disk");
        Over.Rovne(Odtlacok(kod), Odtlacok(QrKod.Vytvor("D042 Vymyslený disk")), "rovnaký text, rovnaký kód");
        Over.Pravda(Enumerable.Range(0, 8).Any(m => Odtlacok(QrKod.Vytvor("D042 Vymyslený disk", UrovenOpravy.M, m)) == Odtlacok(kod)), "vybraná maska");

        // three finder patterns: a dark ring of 7, a light ring of 5 and a dark core of 3
        var n = kod.GetLength(0);
        foreach (var (x0, y0) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            for (var dy = 0; dy < 7; dy++)
            {
                for (var dx = 0; dx < 7; dx++)
                {
                    var v = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    Over.Rovne(v != 2, kod[y0 + dy, x0 + dx], $"hľadací vzor {x0},{y0}");
                }
            }
        }
    }

    [Test]
    public void StitkyNaHarku()
    {
        var polozky = Enumerable.Range(1, 30)
            .Select(i => new UdajeStitku($"D{i:000}", $"Vymyslený disk {i}", ["Katalóg Domov", "1,8 TB · 320 súborov"], $"KATALOG D{i:000}"))
            .ToList();
        var moznosti = new MoznostiStitkov { Sablona = "a4-3x8", ZacatOd = 20 };
        var strany = Stitky.Rozloz(polozky, moznosti);
        // 5 labels fill the first sheet (20–24), 24 the second, 1 the third
        Over.Rovne(3, strany.Count, "počet strán");
        Over.Rovne(3, Stitky.PocetStran(polozky.Count, moznosti), "počet strán vopred");
        Over.Rovne(5, strany[0].Prvky.OfType<QrNakresu>().Count(), "prvá strana");
        Over.Rovne(24, strany[1].Prvky.OfType<QrNakresu>().Count(), "druhá strana");

        var sablona = Stitky.Sablona("a4-3x8");
        foreach (var strana in strany)
        {
            Over.Rovne(Nakres.SirkaA4, strana.Sirka, "A4");
            foreach (var prvok in strana.Prvky)
            {
                var (x, y, w, h) = prvok switch
                {
                    TextNakresu t => (t.X, t.Y, t.Sirka, t.Vyska),
                    QrNakresu q => (q.X, q.Y, q.Strana, q.Strana),
                    _ => (0d, 0d, 0d, 0d),
                };
                // every element stays inside its label
                var stlpec = (int)Math.Floor((x - sablona.OkrajVlavo) / sablona.KrokVodorovne);
                var riadok = (int)Math.Floor((y - sablona.OkrajHore) / sablona.KrokZvisle);
                var lx = sablona.OkrajVlavo + (stlpec * sablona.KrokVodorovne);
                var ly = sablona.OkrajHore + (riadok * sablona.KrokZvisle);
                Over.Pravda(x >= lx - 0.01 && x + w <= lx + sablona.Sirka + 0.01 && y >= ly - 0.01 && y + h <= ly + sablona.Vyska + 0.01,
                    $"prvok mimo štítku: {prvok}");
            }
        }

        // the first label on the first page is the 20th position: second column, seventh row
        var prvy = strany[0].Prvky.OfType<TextNakresu>().First(t => t.Text == "D001");
        Over.Pravda(prvy.X is > 70 and < 140 && prvy.Y is > 6 * 37 and < 7 * 37, $"prvý štítok na 20. mieste: {prvy}");

        // a label printer: one label a page, copies
        var rola = Stitky.Rozloz(polozky.Take(2).ToList(), new MoznostiStitkov { Sablona = "rola-62x29", Kopie = 2, ZacatOd = 9 });
        Over.Rovne(4, rola.Count, "rola, dve kópie");
        Over.Rovne(62d, rola[0].Sirka, "strana ako štítok");

        // without QR the texts take the whole width
        var bezKodu = Stitky.Rozloz(polozky.Take(1).ToList(), new MoznostiStitkov { Qr = false });
        Over.Pravda(!bezKodu[0].Prvky.OfType<QrNakresu>().Any() && bezKodu[0].Prvky.OfType<TextNakresu>().First().Sirka > 60, "bez QR kódu");
    }

    private static Tabulka Ukazka() => new(
        "Filmy na disku 7",
        [
            new StlpecTabulky("Názov", Sirka: 3),
            new StlpecTabulky("Rok", DruhStlpca.Rok),
            new StlpecTabulky("Veľkosť", DruhStlpca.Velkost),
            new StlpecTabulky("Pridané", DruhStlpca.Datum),
            new StlpecTabulky("Dĺžka", DruhStlpca.Trvanie),
            new StlpecTabulky("Hodnotenie", DruhStlpca.Desatinne),
        ],
        [
            ["Vymyslený film; časť \"prvá\"", 1999L, 4L * 1024 * 1024 * 1024, new DateTime(2026, 11, 8, 9, 41, 0), 6330d, 7.5],
            ["=HYPERLINK(\"x\")", null, 512L, null, null, null],
            ["<script>alert(1)</script>\u0001", 2026L, 0L, new DateTime(2026, 1, 2), 59d, 10d],
        ])
    {
        Popis = "typ:video disk:7",
    };

    [Test]
    public void TabulkaCsv()
    {
        using var prud = new MemoryStream();
        ZapisTabulky.Csv(Ukazka(), prud);
        var bajty = prud.ToArray();
        Over.Pravda(bajty is [0xEF, 0xBB, 0xBF, ..], "BOM pre Excel");
        var riadky = Encoding.UTF8.GetString(bajty, 3, bajty.Length - 3).Split("\r\n");
        Over.Rovne("Názov;Rok;Veľkosť (GB);Pridané;Dĺžka;Hodnotenie", riadky[0], "hlavička");
        Over.Rovne("\"Vymyslený film; časť \"\"prvá\"\"\";1999;4;8.11.2026 9:41;1:45:30;7,5", riadky[1], "prvý riadok");
        Over.Rovne("\"'=HYPERLINK(\"\"x\"\")\";;0;;;", riadky[2], "vzorec sa nespustí");
        Over.Rovne("<script>alert(1)</script>\u0001;2026;0;2.1.2026;0:59;10", riadky[3], "tretí riadok");
    }

    [Test]
    public void TabulkaHtml()
    {
        using var prud = new MemoryStream();
        ZapisTabulky.Html(Ukazka(), prud);
        var html = Encoding.UTF8.GetString(prud.ToArray());
        Over.Pravda(!html.Contains("<script>alert", StringComparison.Ordinal) && html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", StringComparison.Ordinal), "text sa nestane kódom");
        Over.Pravda(html.Contains("<td class=\"c\" data-p=\"4294967296\">4,0 GB</td>", StringComparison.Ordinal), $"veľkosť na čítanie a na triedenie");
        Over.Pravda(html.Contains("8. 11. 2026 9:41", StringComparison.Ordinal) && html.Contains("1:45:30", StringComparison.Ordinal), "dátum a dĺžka");
        Over.Pravda(html.Contains("typ:video disk:7 · 3 riadky", StringComparison.Ordinal), "popis a počet");
        Over.Pravda(!html.Contains("http", StringComparison.Ordinal), "nič z internetu");
    }

    [Test]
    public void TabulkaExcel()
    {
        using var prud = new MemoryStream();
        ZapisTabulky.Xlsx(Ukazka(), prud);
        prud.Position = 0;
        using var zip = new ZipArchive(prud, ZipArchiveMode.Read);
        var casti = zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        Over.Postupnost(["[Content_Types].xml", "_rels/.rels", "docProps/core.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml"], casti, "časti zošita");

        XDocument Citaj(string cesta)
        {
            using var s = zip.GetEntry(cesta)!.Open();
            return XDocument.Load(s);
        }

        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var harok = Citaj("xl/worksheets/sheet1.xml");
        var bunky = harok.Descendants(m + "c").ToDictionary(c => (string)c.Attribute("r")!);
        string Text(string r) => string.Concat(bunky[r].Descendants(m + "t").Select(t => t.Value));
        string? Hodnota(string r) => bunky[r].Element(m + "v")?.Value;

        Over.Rovne("Veľkosť (GB)", Text("C1"), "hlavička");
        Over.Rovne("1", (string?)bunky["A1"].Attribute("s"), "hlavička tučne");
        Over.Rovne("Vymyslený film; časť \"prvá\"", Text("A2"), "text");
        Over.Rovne("1999", Hodnota("B2"), "rok ako číslo");
        Over.Rovne("4", Hodnota("C2"), "4 GB");
        Over.Rovne("2", (string?)bunky["C2"].Attribute("s"), "formát GB");
        Over.Pravda(Math.Abs(double.Parse(Hodnota("D2")!, System.Globalization.CultureInfo.InvariantCulture) - 46334.40347) < 0.0001, "dátum ako číslo Excelu");
        Over.Rovne("=HYPERLINK(\"x\")", Text("A3"), "vzorec ostane textom");
        Over.Pravda(bunky["A3"].Element(m + "f") is null, "žiadny vzorec");
        Over.Pravda(!bunky.ContainsKey("B3"), "prázdna bunka chýba");
        Over.Rovne("<script>alert(1)</script> ", Text("A4"), "riadiaci znak preč");
        Over.Rovne("A1:F4", (string?)harok.Descendants(m + "autoFilter").Single().Attribute("ref"), "filter");
        Over.Rovne("Filmy na disku 7", (string?)Citaj("xl/workbook.xml").Descendants(m + "sheet").Single().Attribute("name"), "názov hárka");

        Over.Rovne("Z", ZapisTabulky.Stlpec(25), "stĺpec 26");
        Over.Rovne("AA", ZapisTabulky.Stlpec(26), "stĺpec 27");
        Over.Rovne("XFD", ZapisTabulky.Stlpec(16_383), "posledný stĺpec");
        Over.Rovne("Filmy 2026 výber", ZapisTabulky.NazovHarku("Filmy: 2026/výber"), "znaky, ktoré Excel nechce");
        Over.Rovne(31, ZapisTabulky.NazovHarku(new string('a', 40)).Length, "najviac 31 znakov");
    }

    [Test]
    public void StranyTabulkyAPdf()
    {
        var riadky = Enumerable.Range(1, 120).Select(i => new object?[] { $"Vymyslený film {i}", (long)(1990 + (i % 30)), (long)i << 30, null, null, null }).ToList();
        var tabulka = Ukazka() with { Riadky = riadky };
        var strany = StranyTabulky.Rozloz(tabulka, naSirku: false, new DateTime(2026, 11, 8, 10, 0, 0));
        Over.Pravda(strany.Count == 3, $"120 riadkov na 3 strany ({strany.Count})");
        Over.Pravda(strany.All(s => s.Prvky.OfType<TextNakresu>().Any(t => t.Text == "Veľkosť")), "hlavička na každej strane");
        Over.Pravda(strany[2].Prvky.OfType<TextNakresu>().Any(t => t.Text == "Strana 3 z 3"), "číslo strany");
        Over.Pravda(strany.SelectMany(s => s.Prvky.OfType<TextNakresu>()).Count(t => t.Text.StartsWith("Vymyslený film ", StringComparison.Ordinal)) == 120, "všetky riadky");
        Over.Pravda(strany.All(s => s.Prvky.OfType<TextNakresu>().All(t => t.Y + t.Vyska <= Nakres.VyskaA4 - 11.9)), "nič pod okrajom");

        // a grey page and a coloured one
        var siva = new byte[20 * 10 * 3];
        Array.Fill(siva, (byte)200);
        var farebna = (byte[])siva.Clone();
        farebna[0] = 255;
        using var prud = new MemoryStream();
        PdfSubor.Zapis(prud, [new RastrovaStrana(210, 297, 20, 10, siva), new RastrovaStrana(62, 29, 20, 10, farebna)], "Štítky diskov");
        var pdf = Encoding.Latin1.GetString(prud.ToArray());
        Over.Pravda(pdf.StartsWith("%PDF-1.4", StringComparison.Ordinal) && pdf.EndsWith("%%EOF\n", StringComparison.Ordinal), "hlavička a koniec");
        Over.Pravda(pdf.Contains("/MediaBox [0 0 595.276 841.89]", StringComparison.Ordinal), "A4 v bodoch");
        Over.Pravda(pdf.Contains("/DeviceGray", StringComparison.Ordinal) && pdf.Contains("/DeviceRGB", StringComparison.Ordinal), "sivá aj farebná strana");
        Over.Pravda(pdf.Contains("/Count 2", StringComparison.Ordinal), "dve strany");

        // every entry of the cross-reference table points at its object
        var xref = int.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        var tabulkaOdkazov = pdf[xref..].Split('\n');
        var pocet = int.Parse(tabulkaOdkazov[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        for (var id = 1; id < pocet; id++)
        {
            var pozicia = int.Parse(tabulkaOdkazov[2 + id][..10], System.Globalization.CultureInfo.InvariantCulture);
            Over.Pravda(pdf[pozicia..].StartsWith($"{id} 0 obj", StringComparison.Ordinal), $"odkaz na objekt {id}");
        }

        Over.Vyhodi<ArgumentException>(() => PdfSubor.Zapis(new MemoryStream(), [], "nič"), "PDF bez strán");
    }

    [Test]
    public void PlaylistM3u()
    {
        var text = M3u.Text(
        [
            new PolozkaM3u(@"E:\Filmy\Vymyslený film (2001).mkv", "Vymyslený film (2001)", TimeSpan.FromMinutes(101.5)),
            new PolozkaM3u(@"F:\Seriál\S01E02.mkv", "Vymyslený seriál\r\nS01E02"),
        ], "Na víkend");
        Over.Rovne(
            "#EXTM3U\r\n#PLAYLIST:Na víkend\r\n#EXTINF:6090,Vymyslený film (2001)\r\nE:\\Filmy\\Vymyslený film (2001).mkv\r\n" +
            "#EXTINF:-1,Vymyslený seriál S01E02\r\nF:\\Seriál\\S01E02.mkv\r\n",
            text, "M3U");
    }

    private static ObsahWebu UkazkaWebu() => new(
        "Vymyslená videotéka",
        "2026-11-08T19:00:00Z",
        "KATALOG",
        [
            new TitulWebu(1, false, "Vymyslený film", "Invented Film", 2001, "Dej o diskoch.", ["Komédia"], ["Vymyslený Režisér"], ["Vymyslená Herečka"],
                ["Vymyslená sága"], ["Vianoce"], 7.5, 8, true, false, true, "FHD", 0,
                [new VerziaWebu(1, "Filmy/Vymyslený film (2001).mkv", 4_000_000_000, "FHD", null, null)], "t1"),
            new TitulWebu(2, true, "Vymyslený seriál", null, 2020, null, [], [], [], [], [], null, null, false, false, true, "HD", 10,
                [new VerziaWebu(1, "Seriály/S01E01.mkv", 700_000_000, null, 1, 1)], null),
        ],
        [new DiskWebu(1, "D001", "Vymyslený disk", "Domov", "USB disk", 2_000_000_000_000, 500_000_000_000, 320, 2, "skriňa", "2026-11-08")]);

    [Test]
    public void WebovyKatalogSifrovanie()
    {
        var obsah = System.Text.Encoding.UTF8.GetBytes("{\"a\":\"ľščťžý\"}");
        var sifra = WebovyKatalog.ZasifrujObsah(obsah, "vymyslené heslo", out var kluc, iteracie: 1000);
        Over.Pravda(sifra.Length == 1 + 16 + 4 + 12 + obsah.Length + 16, "hlavička, nonce, šifra a značka");
        Over.Postupnost(obsah, WebovyKatalog.OdsifrujObsah(sifra, "vymyslené heslo", out var kluc2, out _ ), "odšifrované");
        Over.Postupnost(kluc, kluc2, "ten istý kľúč");
        Over.Vyhodi<System.Security.Cryptography.CryptographicException>(() => WebovyKatalog.OdsifrujObsah(sifra, "iné heslo", out _, out _), "zlé heslo");
        sifra[^1] ^= 1;
        Over.Vyhodi<System.Security.Cryptography.CryptographicException>(() => WebovyKatalog.OdsifrujObsah(sifra, "vymyslené heslo", out _, out _), "zmenený obsah");
        Over.Pravda(!WebovyKatalog.Zasifruj(obsah, kluc).SequenceEqual(WebovyKatalog.Zasifruj(obsah, kluc)), "každé šifrovanie s novým nonce");
        // the standard vector of PBKDF2-HMAC-SHA256 (RFC 7914: "passwd", "salt", 1 round, first 32 bytes)
        Over.Rovne("55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc",
            Convert.ToHexStringLower(WebovyKatalog.Kluc("passwd", System.Text.Encoding.ASCII.GetBytes("salt"), 1)), "PBKDF2");
    }

    [Test]
    public void WebovyKatalogPriecinok()
    {
        using var priecinok = new DocasnyPriecinok();
        var web = priecinok.Subor("web");
        var plagaty = Enumerable.Range(0, 45).Select(i => new PlagatWebu("t" + i, [0xFF, 0xD8, (byte)i, 0xFF, 0xD9])).ToList();
        var vysledok = WebovyKatalog.Vytvor(web, UkazkaWebu(), plagaty, "vymyslené heslo", iteracie: 1000);
        Over.Rovne((2, 45, 2), (vysledok.Titulov, vysledok.Plagatov, vysledok.Balikov), "tituly, plagáty, balíky po 40");
        Over.Pravda(File.Exists(Path.Combine(web, "index.html")) && File.Exists(Path.Combine(web, "plagaty", "001.js")), "stránka a balíky");

        // data.js opened as the page does: header, salt and rounds, then AES-GCM
        var dataJs = File.ReadAllText(Path.Combine(web, "data.js"));
        Over.Pravda(dataJs.StartsWith("KATALOG.data(\"", StringComparison.Ordinal) && !dataJs.Contains("Vymyslen", StringComparison.Ordinal), "údaje len zašifrované");
        var data = WebovyKatalog.OdsifrujObsah(Convert.FromBase64String(dataJs[14..^3]), "vymyslené heslo", out var kluc, out var iteracie);
        Over.Rovne(1000, iteracie, "kolá v hlavičke");
        using var json = System.Text.Json.JsonDocument.Parse(data);
        var koren = json.RootElement;
        Over.Rovne("Vymyslený film", koren.GetProperty("tituly")[0].GetProperty("n").GetString(), "názov titulu");
        Over.Rovne(1, koren.GetProperty("balik").GetProperty("t44").GetInt32(), "plagát 45 v druhom balíku");
        Over.Pravda(!koren.GetProperty("tituly")[1].TryGetProperty("o", out _), "prázdne hodnoty sa nepíšu");

        var balik = File.ReadAllText(Path.Combine(web, "plagaty", "001.js"));
        Over.Pravda(balik.StartsWith("KATALOG.plagaty(1,\"", StringComparison.Ordinal), "balík volá stránku");
        var mapa = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(WebovyKatalog.Odsifruj(Convert.FromBase64String(balik[19..^3]), kluc))!;
        Over.Postupnost(["t40", "t41", "t42", "t43", "t44"], mapa.Keys.Order(StringComparer.Ordinal), "päť plagátov v druhom balíku");

        var stranka = File.ReadAllText(Path.Combine(web, "index.html"));
        Over.Pravda(stranka.Contains("crypto.subtle", StringComparison.Ordinal) && !stranka.Contains("http://", StringComparison.Ordinal) &&
                    !stranka.Contains("https://", StringComparison.Ordinal) && !stranka.Contains("{{", StringComparison.Ordinal), "stránka bez cudzích adries");

        // written again into its own folder: old packs go
        WebovyKatalog.Vytvor(web, UkazkaWebu(), plagaty.Take(3).ToList(), "iné vymyslené heslo", iteracie: 1000);
        Over.Rovne(1, Directory.GetFiles(Path.Combine(web, "plagaty")).Length, "staré balíky preč");
        File.WriteAllText(priecinok.Subor("cudzi.txt"), "x");
        Over.Vyhodi<IOException>(() => WebovyKatalog.Vytvor(priecinok.Cesta, UkazkaWebu(), [], "vymyslené heslo", iteracie: 1000), "cudzí priečinok");
        Over.Vyhodi<ArgumentException>(() => WebovyKatalog.Vytvor(priecinok.Subor("iny"), UkazkaWebu(), [], "kratke", iteracie: 1000), "krátke heslo");
    }
}
