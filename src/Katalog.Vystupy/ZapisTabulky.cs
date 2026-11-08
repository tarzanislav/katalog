using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Katalog.Vystupy;

/// <summary>Formats a table can be saved in (written here, without libraries: owner, 8. 11. 2026).</summary>
public enum FormatTabulky
{
    Csv = 1,
    Xlsx = 2,
    Html = 3,
    Pdf = 4,
}

/// <summary>CSV, Excel and HTML of a table; PDF goes through the drawn pages (<see cref="StranyTabulky"/>).</summary>
public static class ZapisTabulky
{
    public static string Pripona(FormatTabulky format) => format switch
    {
        FormatTabulky.Csv => ".csv",
        FormatTabulky.Xlsx => ".xlsx",
        FormatTabulky.Html => ".html",
        _ => ".pdf",
    };

    // ------------------------------------------------------------------ CSV

    /// <summary>
    /// CSV as Excel opens it in Slovak Windows: UTF-8 with a BOM, semicolons, decimal comma, sizes in GB. Text that
    /// starts like a formula (= + - @) gets an apostrophe in front, so no spreadsheet runs it.
    /// </summary>
    public static void Csv(Tabulka tabulka, Stream vystup)
    {
        ArgumentNullException.ThrowIfNull(tabulka);
        using var zapis = new StreamWriter(vystup, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        zapis.NewLine = "\r\n";
        zapis.WriteLine(string.Join(';', tabulka.Stlpce.Select(s => PoleCsv(FormatVystupu.NazovNaVypocet(s)))));
        foreach (var riadok in tabulka.Riadky)
        {
            var polia = new string[tabulka.Stlpce.Count];
            for (var i = 0; i < polia.Length; i++)
            {
                polia[i] = PoleCsv(HodnotaCsv(i < riadok.Length ? riadok[i] : null, tabulka.Stlpce[i].Druh));
            }

            zapis.WriteLine(string.Join(';', polia));
        }
    }

    private static string HodnotaCsv(object? hodnota, DruhStlpca druh) => hodnota switch
    {
        null => "",
        string s => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s,
        DateTime d => d.ToString(d.TimeOfDay == TimeSpan.Zero ? "d.M.yyyy" : "d.M.yyyy H:mm", FormatVystupu.Slovensky),
        _ when druh == DruhStlpca.Velkost => (Convert.ToDouble(hodnota, CultureInfo.InvariantCulture) / FormatVystupu.BajtovNaGb).ToString("0.###", FormatVystupu.Slovensky),
        _ when druh == DruhStlpca.Trvanie => FormatVystupu.Trvanie(Convert.ToDouble(hodnota, CultureInfo.InvariantCulture)),
        IFormattable f => f.ToString(null, FormatVystupu.Slovensky),
        _ => hodnota.ToString() ?? "",
    };

    private static string PoleCsv(string text) =>
        text.AsSpan().IndexOfAny(";\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    // ------------------------------------------------------------------ HTML

    /// <summary>One self-contained page (no scripts or styles from elsewhere); a click on a heading sorts the table.</summary>
    public static void Html(Tabulka tabulka, Stream vystup)
    {
        ArgumentNullException.ThrowIfNull(tabulka);
        using var zapis = new StreamWriter(vystup, new UTF8Encoding(false), leaveOpen: true);
        string E(string text) => Html(text);
        zapis.Write($"""
            <!doctype html>
            <html lang="sk">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="{E(Jadro.Produkt.Nazov)}">
            <title>{E(tabulka.Nazov)}</title>
            <style>
            :root {"{"} color-scheme: light dark; --pozadie: #fff; --text: #1a1a1a; --tlmene: #666; --ciara: #ddd; --pruh: #f5f5f5; {"}"}
            @media (prefers-color-scheme: dark) {"{"} :root {"{"} --pozadie: #1e1e1e; --text: #eee; --tlmene: #aaa; --ciara: #444; --pruh: #262626; {"}"} {"}"}
            body {"{"} margin: 24px 16px; background: var(--pozadie); color: var(--text); font: 14px/1.4 "Segoe UI", system-ui, sans-serif; {"}"}
            h1 {"{"} font-size: 20px; margin: 0 0 4px; {"}"}
            p {"{"} margin: 0 0 16px; color: var(--tlmene); {"}"}
            .obal {"{"} overflow-x: auto; {"}"}
            table {"{"} border-collapse: collapse; min-width: 100%; {"}"}
            th, td {"{"} padding: 4px 10px; border-bottom: 1px solid var(--ciara); text-align: left; vertical-align: top; {"}"}
            th {"{"} position: sticky; top: 0; background: var(--pozadie); cursor: pointer; user-select: none; white-space: nowrap; {"}"}
            tbody tr:nth-child(even) {"{"} background: var(--pruh); {"}"}
            .c {"{"} text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; {"}"}
            @media print {"{"} th {"{"} position: static; {"}"} {"}"}
            </style>
            </head>
            <body>
            <h1>{E(tabulka.Nazov)}</h1>

            """);
        var popis = string.Join(" · ", new[] { tabulka.Popis, Mnozne(tabulka.Riadky.Count) }.Where(t => !string.IsNullOrEmpty(t)));
        zapis.Write($"<p>{E(popis)}</p>\n<div class=\"obal\"><table>\n<thead><tr>");
        foreach (var stlpec in tabulka.Stlpce)
        {
            zapis.Write(JeCislo(stlpec.Druh) ? $"<th class=\"c\">{E(stlpec.Nazov)}</th>" : $"<th>{E(stlpec.Nazov)}</th>");
        }

        zapis.Write("</tr></thead>\n<tbody>\n");
        foreach (var riadok in tabulka.Riadky)
        {
            zapis.Write("<tr>");
            for (var i = 0; i < tabulka.Stlpce.Count; i++)
            {
                var hodnota = i < riadok.Length ? riadok[i] : null;
                var druh = tabulka.Stlpce[i].Druh;
                var text = E(FormatVystupu.NaCitanie(hodnota, druh));
                var poradie = Poradie(hodnota);
                zapis.Write(JeCislo(druh) || poradie is not null
                    ? $"<td{(JeCislo(druh) ? " class=\"c\"" : "")}{(poradie is null ? "" : $" data-p=\"{poradie}\"")}>{text}</td>"
                    : $"<td>{text}</td>");
            }

            zapis.Write("</tr>\n");
        }

        zapis.Write("""
            </tbody></table></div>
            <script>
            document.querySelectorAll('th').forEach((th, i) => th.addEventListener('click', () => {
              const telo = th.closest('table').tBodies[0];
              const smer = th.dataset.smer === '1' ? -1 : 1;
              th.parentNode.querySelectorAll('th').forEach(t => delete t.dataset.smer);
              th.dataset.smer = smer === 1 ? '1' : '-1';
              const hodnota = r => { const c = r.cells[i]; return c.dataset.p !== undefined ? Number(c.dataset.p) : c.textContent; };
              [...telo.rows].sort((a, b) => {
                const x = hodnota(a), y = hodnota(b);
                return smer * (typeof x === 'number' ? x - y : x.localeCompare(y, 'sk'));
              }).forEach(r => telo.appendChild(r));
            }));
            </script>
            </body>
            </html>

            """);
    }

    /// <summary>Only the five characters HTML needs escaped; letters with diacritics stay as they are (UTF-8).</summary>
    internal static string Html(string text)
    {
        var vysledok = new StringBuilder(text.Length + 16);
        foreach (var c in BezRiadiacich(text))
        {
            vysledok.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }

        return vysledok.ToString();
    }

    private static bool JeCislo(DruhStlpca druh) => druh is DruhStlpca.Cislo or DruhStlpca.Desatinne or DruhStlpca.Velkost or DruhStlpca.Trvanie or DruhStlpca.Rok;

    /// <summary>The value sorting uses (numbers and dates), invariant.</summary>
    private static string? Poradie(object? hodnota) => hodnota switch
    {
        DateTime d => d.Ticks.ToString(CultureInfo.InvariantCulture),
        long or int or double or float or decimal => Convert.ToDouble(hodnota, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Mnozne(int pocet) =>
        pocet.ToString("#,0", FormatVystupu.Slovensky) + (pocet == 1 ? " riadok" : pocet is >= 2 and <= 4 ? " riadky" : " riadkov");

    // ------------------------------------------------------------------ Excel (Office Open XML)

    /// <summary>
    /// A workbook with one sheet: bold heading row that stays in place, filters, numbers as numbers (sizes in GB),
    /// dates as dates. Text is stored as text, so nothing in a file name can become a formula.
    /// </summary>
    public static void Xlsx(Tabulka tabulka, Stream vystup)
    {
        ArgumentNullException.ThrowIfNull(tabulka);
        using var zip = new ZipArchive(vystup, ZipArchiveMode.Create, leaveOpen: true);
        void Cast(string cesta, Action<XmlWriter> obsah)
        {
            var polozka = zip.CreateEntry(cesta, CompressionLevel.Optimal);
            polozka.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var prud = polozka.Open();
            using var xml = XmlWriter.Create(prud, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CheckCharacters = false });
            xml.WriteStartDocument(standalone: true);
            obsah(xml);
            xml.WriteEndDocument();
        }

        const string hlavny = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string vztahy = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string vztahyDok = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        Cast("[Content_Types].xml", x =>
        {
            x.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            Prvok(x, "Default", ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
            Prvok(x, "Default", ("Extension", "xml"), ("ContentType", "application/xml"));
            Prvok(x, "Override", ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
            Prvok(x, "Override", ("PartName", "/xl/worksheets/sheet1.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
            Prvok(x, "Override", ("PartName", "/xl/styles.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
            Prvok(x, "Override", ("PartName", "/docProps/core.xml"), ("ContentType", "application/vnd.openxmlformats-package.core-properties+xml"));
            x.WriteEndElement();
        });
        Cast("_rels/.rels", x =>
        {
            x.WriteStartElement("Relationships", vztahy);
            Prvok(x, "Relationship", ("Id", "rId1"), ("Type", vztahyDok + "/officeDocument"), ("Target", "xl/workbook.xml"));
            Prvok(x, "Relationship", ("Id", "rId2"), ("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"), ("Target", "docProps/core.xml"));
            x.WriteEndElement();
        });
        Cast("docProps/core.xml", x =>
        {
            x.WriteStartElement("cp", "coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
            x.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");
            x.WriteElementString("dc", "title", "http://purl.org/dc/elements/1.1/", BezRiadiacich(tabulka.Nazov));
            x.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", Jadro.Produkt.Nazov);
            x.WriteEndElement();
        });
        Cast("xl/_rels/workbook.xml.rels", x =>
        {
            x.WriteStartElement("Relationships", vztahy);
            Prvok(x, "Relationship", ("Id", "rId1"), ("Type", vztahyDok + "/worksheet"), ("Target", "worksheets/sheet1.xml"));
            Prvok(x, "Relationship", ("Id", "rId2"), ("Type", vztahyDok + "/styles"), ("Target", "styles.xml"));
            x.WriteEndElement();
        });
        Cast("xl/workbook.xml", x =>
        {
            x.WriteStartElement("workbook", hlavny);
            x.WriteAttributeString("xmlns", "r", null, vztahyDok);
            x.WriteStartElement("sheets", hlavny);
            x.WriteStartElement("sheet", hlavny);
            x.WriteAttributeString("name", NazovHarku(tabulka.Nazov));
            x.WriteAttributeString("sheetId", "1");
            x.WriteAttributeString("id", vztahyDok, "rId1");
            x.WriteEndElement();
            x.WriteEndElement();
            if (tabulka.Riadky.Count > 0)
            {
                x.WriteStartElement("definedNames", hlavny);
                x.WriteStartElement("definedName", hlavny);
                x.WriteAttributeString("name", "_xlnm._FilterDatabase");
                x.WriteAttributeString("localSheetId", "0");
                x.WriteAttributeString("hidden", "1");
                x.WriteString($"'{NazovHarku(tabulka.Nazov).Replace("'", "''", StringComparison.Ordinal)}'!$A$1:${Stlpec(tabulka.Stlpce.Count - 1)}${tabulka.Riadky.Count + 1}");
                x.WriteEndElement();
                x.WriteEndElement();
            }

            x.WriteEndElement();
        });
        Cast("xl/styles.xml", x =>
        {
            x.WriteStartElement("styleSheet", hlavny);
            x.WriteStartElement("numFmts", hlavny);
            x.WriteAttributeString("count", "4");
            Prvok(x, "numFmt", ("numFmtId", "164"), ("formatCode", "0.000"));
            Prvok(x, "numFmt", ("numFmtId", "165"), ("formatCode", "d.m.yyyy h:mm"));
            Prvok(x, "numFmt", ("numFmtId", "166"), ("formatCode", "[h]:mm:ss"));
            Prvok(x, "numFmt", ("numFmtId", "167"), ("formatCode", "0.0#"));
            x.WriteEndElement();
            x.WriteRaw("""<fonts count="2"><font><sz val="11"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><name val="Calibri"/><family val="2"/></font></fonts>""");
            x.WriteRaw("""<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>""");
            x.WriteRaw("""<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>""");
            x.WriteRaw("""<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>""");
            // 0 normal, 1 heading, 2 GB, 3 date, 4 duration, 5 whole number, 6 decimals
            x.WriteRaw("""<cellXfs count="7"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="167" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs>""");
            x.WriteRaw("""<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>""");
            x.WriteEndElement();
        });
        Cast("xl/worksheets/sheet1.xml", x => Harok(x, tabulka, hlavny));
    }

    private static void Harok(XmlWriter x, Tabulka tabulka, string hlavny)
    {
        var stlpce = tabulka.Stlpce;
        x.WriteStartElement("worksheet", hlavny);
        x.WriteStartElement("sheetViews", hlavny);
        x.WriteStartElement("sheetView", hlavny);
        x.WriteAttributeString("workbookViewId", "0");
        x.WriteRaw("""<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>""");
        x.WriteEndElement();
        x.WriteEndElement();

        x.WriteStartElement("cols", hlavny);
        for (var i = 0; i < stlpce.Count; i++)
        {
            var sirka = stlpce[i].Druh switch
            {
                DruhStlpca.Datum => 16,
                DruhStlpca.Rok => 7,
                DruhStlpca.Text => Math.Clamp(12 * stlpce[i].Sirka, 8, 80),
                _ => 12,
            };
            x.WriteStartElement("col", hlavny);
            x.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("width", sirka.ToString("0.#", CultureInfo.InvariantCulture));
            x.WriteAttributeString("customWidth", "1");
            x.WriteEndElement();
        }

        x.WriteEndElement();

        x.WriteStartElement("sheetData", hlavny);
        Riadok(x, hlavny, 1, stlpce.Select(s => (object?)FormatVystupu.NazovNaVypocet(s)).ToArray(), stlpce, hlavicka: true);
        for (var r = 0; r < tabulka.Riadky.Count; r++)
        {
            Riadok(x, hlavny, r + 2, tabulka.Riadky[r], stlpce, hlavicka: false);
        }

        x.WriteEndElement();
        if (tabulka.Riadky.Count > 0)
        {
            x.WriteStartElement("autoFilter", hlavny);
            x.WriteAttributeString("ref", $"A1:{Stlpec(stlpce.Count - 1)}{tabulka.Riadky.Count + 1}");
            x.WriteEndElement();
        }

        x.WriteEndElement();
    }

    private static void Riadok(XmlWriter x, string hlavny, int cislo, object?[] hodnoty, IReadOnlyList<StlpecTabulky> stlpce, bool hlavicka)
    {
        var r = cislo.ToString(CultureInfo.InvariantCulture);
        x.WriteStartElement("row", hlavny);
        x.WriteAttributeString("r", r);
        for (var i = 0; i < stlpce.Count; i++)
        {
            var hodnota = i < hodnoty.Length ? hodnoty[i] : null;
            if (hodnota is null)
            {
                continue;
            }

            x.WriteStartElement("c", hlavny);
            x.WriteAttributeString("r", Stlpec(i) + r);
            double? cislo2 = hodnota switch
            {
                DateTime d => (d - new DateTime(1899, 12, 30)).TotalDays,
                string => null,
                _ when stlpce[i].Druh == DruhStlpca.Velkost => Convert.ToDouble(hodnota, CultureInfo.InvariantCulture) / FormatVystupu.BajtovNaGb,
                _ when stlpce[i].Druh == DruhStlpca.Trvanie => Convert.ToDouble(hodnota, CultureInfo.InvariantCulture) / 86_400,
                long or int or double or float or decimal => Convert.ToDouble(hodnota, CultureInfo.InvariantCulture),
                _ => null,
            };
            var styl = hlavicka ? 1 : hodnota switch
            {
                DateTime => 3,
                string => 0,
                _ => stlpce[i].Druh switch
                {
                    DruhStlpca.Velkost => 2,
                    DruhStlpca.Trvanie => 4,
                    DruhStlpca.Cislo => 5,
                    DruhStlpca.Desatinne => 6,
                    _ => 0,
                },
            };
            if (styl != 0)
            {
                x.WriteAttributeString("s", styl.ToString(CultureInfo.InvariantCulture));
            }

            if (cislo2 is { } c && double.IsFinite(c))
            {
                x.WriteElementString("v", hlavny, c.ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                x.WriteAttributeString("t", "inlineStr");
                x.WriteStartElement("is", hlavny);
                x.WriteStartElement("t", hlavny);
                x.WriteAttributeString("xml", "space", null, "preserve");
                x.WriteString(BezRiadiacich(Convert.ToString(hodnota, FormatVystupu.Slovensky) ?? ""));
                x.WriteEndElement();
                x.WriteEndElement();
            }

            x.WriteEndElement();
        }

        x.WriteEndElement();
    }

    private static void Prvok(XmlWriter x, string meno, params (string Meno, string Hodnota)[] atributy)
    {
        // null namespace: the default namespace in scope (of the enclosing element)
        x.WriteStartElement(meno);
        foreach (var (m, h) in atributy)
        {
            x.WriteAttributeString(m, h);
        }

        x.WriteEndElement();
    }

    /// <summary>A, B, …, Z, AA, AB … of a zero-based column.</summary>
    internal static string Stlpec(int index)
    {
        var text = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            text = (char)('A' + ((n - 1) % 26)) + text;
        }

        return text;
    }

    /// <summary>A sheet name Excel takes: at most 31 characters, none of []:*?/\ and no apostrophe at the ends.</summary>
    internal static string NazovHarku(string nazov)
    {
        var text = new string(BezRiadiacich(nazov).Select(c => "[]:*?/\\".Contains(c, StringComparison.Ordinal) ? ' ' : c).ToArray()).Trim().Trim('\'');
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }
        if (text.Length > 31)
        {
            text = text[..31].TrimEnd();
        }

        return text.Length == 0 ? "Hárok1" : text;
    }

    /// <summary>XML 1.0 takes no control characters (a file name may have one); they become spaces.</summary>
    internal static string BezRiadiacich(string text)
    {
        if (!text.Any(c => (c < 0x20 && c is not '\t' and not '\n' and not '\r') || c is '￾' or '￿' || char.IsSurrogate(c)))
        {
            return text;
        }

        var vysledok = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                vysledok.Append(c).Append(text[++i]);
            }
            else if ((c < 0x20 && c is not '\t' and not '\n' and not '\r') || c is '￾' or '￿' || char.IsSurrogate(c))
            {
                vysledok.Append(' ');
            }
            else
            {
                vysledok.Append(c);
            }
        }

        return vysledok.ToString();
    }
}
