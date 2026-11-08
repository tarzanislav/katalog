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
    /// across versions with and without the version blocks and all four levels.
    /// </summary>
    [Test]
    public void QrKodZhodny()
    {
        var dlhy = string.Concat(Enumerable.Range(0, 300).Select(i => (char)('a' + (i % 26))));
        (string Text, UrovenOpravy Uroven, int Maska, int Velkost, string Odtlacok)[] pripady =
        [
            ("KATALOG D007", UrovenOpravy.M, 2, 21, "402c64ff3d7568d47f8aa6ba243b6a71d7dd63369a4abf674b298772bc852122"),
            ("Ľúbostný príbeh (1999)", UrovenOpravy.Q, 5, 29, "f3961983282870338c2eb0d83ebe28a3a028ba80411477e11e52139061aceaac"),
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
        // diacritics count as their UTF-8 bytes (7 letters, 14 bytes)
        Over.Rovne(21, QrKod.Vytvor("ľščťžýá").GetLength(0), "diakritika v UTF-8");
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
}
