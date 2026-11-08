namespace Katalog.Vystupy;

/// <summary>
/// A label format: a sheet of self-adhesive labels on A4 (columns × rows) or one label per page for a label printer.
/// Millimetres; a step is the width (height) of a label plus the gap. The formats are the ones of EviTA.
/// </summary>
public sealed record SablonaStitkov(
    string Kluc,
    string Nazov,
    bool Harok,
    double Sirka,
    double Vyska,
    int Stlpce,
    int Riadky,
    double OkrajHore,
    double OkrajVlavo,
    double KrokVodorovne,
    double KrokZvisle)
{
    public int NaStranu => Harok ? Stlpce * Riadky : 1;

    public double SirkaStrany => Harok ? Nakres.SirkaA4 : Sirka;

    public double VyskaStrany => Harok ? Nakres.VyskaA4 : Vyska;
}

/// <summary>What one label shows: a heading (the disk number), a name, a few smaller lines and the text of its QR code.</summary>
public sealed record UdajeStitku(string Nadpis, string Nazov, IReadOnlyList<string> Riadky, string? Kod);

/// <summary>How the labels are printed; kept in the settings and used again next time.</summary>
public sealed record MoznostiStitkov
{
    public string Sablona { get; init; } = "a4-3x8";

    /// <summary>The first free label on a sheet already partly used (1 = top left).</summary>
    public int ZacatOd { get; init; } = 1;

    public int Kopie { get; init; } = 1;

    public bool Qr { get; init; } = true;

    /// <summary>A thin frame around each label (for cutting plain paper).</summary>
    public bool Ramcek { get; init; }

    /// <summary>Shift of the whole print when a printer does not hit the labels exactly (mm).</summary>
    public double PosunX { get; init; }

    public double PosunY { get; init; }
}

/// <summary>Labels laid out on pages (sheets or single labels).</summary>
public static class Stitky
{
    public const string Predvolena = "a4-3x8";

    public static IReadOnlyList<SablonaStitkov> Sablony { get; } =
    [
        new("a4-3x8", "Hárok A4 – 24 štítkov 70 × 37 mm", true, 70, 37, 3, 8, 0.5, 0, 70, 37),
        new("a4-3x7", "Hárok A4 – 21 štítkov 70 × 42,3 mm", true, 70, 42.3, 3, 7, 0.4, 0, 70, 42.3),
        new("a4-4x10", "Hárok A4 – 40 štítkov 48,5 × 25,4 mm", true, 48.5, 25.4, 4, 10, 21.5, 8, 48.5, 25.4),
        new("a4-5x13", "Hárok A4 – 65 štítkov 38,1 × 21,2 mm (L7651)", true, 38.1, 21.2, 5, 13, 10.7, 4.75, 40.6, 21.2),
        new("a4-3x7-63", "Hárok A4 – 21 štítkov 63,5 × 38,1 mm (L7160)", true, 63.5, 38.1, 3, 7, 15.1, 7.2, 66.0, 38.1),
        new("a4-2x7", "Hárok A4 – 14 štítkov 99,1 × 38,1 mm (L7163)", true, 99.1, 38.1, 2, 7, 15.1, 4.65, 101.6, 38.1),
        new("rola-62x29", "Tlačiareň štítkov – 62 × 29 mm", false, 62, 29, 1, 1, 0, 0, 62, 29),
        new("rola-50x30", "Tlačiareň štítkov – 50 × 30 mm", false, 50, 30, 1, 1, 0, 0, 50, 30),
        new("rola-50x25", "Tlačiareň štítkov – 50 × 25 mm", false, 50, 25, 1, 1, 0, 0, 50, 25),
        new("rola-38x25", "Tlačiareň štítkov – 38 × 25 mm", false, 38, 25, 1, 1, 0, 0, 38, 25),
    ];

    public static SablonaStitkov Sablona(string? kluc) => Sablony.FirstOrDefault(s => s.Kluc == kluc) ?? Sablony[0];

    /// <summary>The pages with all labels: each item <see cref="MoznostiStitkov.Kopie"/> times, starting at the first free label.</summary>
    public static IReadOnlyList<StranaNakresu> Rozloz(IReadOnlyList<UdajeStitku> polozky, MoznostiStitkov moznosti)
    {
        ArgumentNullException.ThrowIfNull(polozky);
        ArgumentNullException.ThrowIfNull(moznosti);
        var sablona = Sablona(moznosti.Sablona);
        var naStranu = sablona.NaStranu;
        var kopie = Math.Clamp(moznosti.Kopie, 1, 100);
        var pozicia = sablona.Harok ? Math.Clamp(moznosti.ZacatOd, 1, naStranu) - 1 : 0;

        var strany = new List<StranaNakresu>();
        var prvky = new List<PrvokNakresu>();
        foreach (var polozka in polozky)
        {
            for (var k = 0; k < kopie; k++)
            {
                if (pozicia == naStranu)
                {
                    strany.Add(new StranaNakresu(sablona.SirkaStrany, sablona.VyskaStrany, prvky));
                    prvky = [];
                    pozicia = 0;
                }

                var x = sablona.OkrajVlavo + (pozicia % sablona.Stlpce * sablona.KrokVodorovne) + moznosti.PosunX;
                var y = sablona.OkrajHore + (pozicia / sablona.Stlpce * sablona.KrokZvisle) + moznosti.PosunY;
                Stitok(prvky, polozka, x, y, sablona.Sirka, sablona.Vyska, moznosti);
                pozicia++;
            }
        }

        if (prvky.Count > 0)
        {
            strany.Add(new StranaNakresu(sablona.SirkaStrany, sablona.VyskaStrany, prvky));
        }

        return strany;
    }

    /// <summary>How many pages the labels take (for the preview before drawing them).</summary>
    public static int PocetStran(int poloziek, MoznostiStitkov moznosti)
    {
        var sablona = Sablona(moznosti.Sablona);
        var vsetkych = poloziek * Math.Clamp(moznosti.Kopie, 1, 100);
        if (vsetkych == 0)
        {
            return 0;
        }

        var prva = sablona.Harok ? Math.Clamp(moznosti.ZacatOd, 1, sablona.NaStranu) - 1 : 0;
        return (prva + vsetkych + sablona.NaStranu - 1) / sablona.NaStranu;
    }

    /// <summary>
    /// One label: the QR code on the left as high as the label allows (with its quiet zone), the texts on the right;
    /// everything scales with the label's height (37 mm is the measure), nothing gets smaller than a printer can print.
    /// </summary>
    private static void Stitok(List<PrvokNakresu> prvky, UdajeStitku udaje, double x, double y, double sirka, double vyska, MoznostiStitkov moznosti)
    {
        if (moznosti.Ramcek)
        {
            prvky.Add(new ObdlznikNakresu(x, y, sirka, vyska, 0.2, Farba: "#808080"));
        }

        var okraj = Math.Clamp(vyska * 0.07, 1.5, 3);
        var mierka = Math.Clamp(vyska / 37, 0.55, 1.2);
        var textX = x + okraj;
        var textSirka = sirka - (2 * okraj);

        if (moznosti.Qr && !string.IsNullOrEmpty(udaje.Kod))
        {
            var moduly = QrKod.Vytvor(udaje.Kod, UrovenOpravy.M);
            var pocet = moduly.GetLength(0);
            // the code with two modules of quiet zone on each side (ISO asks for four, the label's own margin adds the rest)
            var strana = Math.Min(vyska - (2 * okraj), sirka * 0.45);
            var modul = strana / (pocet + 4);
            var kod = modul * pocet;
            prvky.Add(new QrNakresu(x + okraj + (2 * modul), y + ((vyska - kod) / 2), kod, moduly));
            textX = x + okraj + strana + (okraj / 2);
            textSirka = x + sirka - okraj - textX;
        }

        var velkostNadpisu = 15 * mierka;
        var velkostNazvu = 9.5 * mierka;
        var velkostRiadku = Math.Max(6, 7.5 * mierka);
        var vyskaNadpisu = velkostNadpisu * Nakres.MmNaBod * Nakres.Riadkovanie;
        var vyskaNazvu = velkostNazvu * Nakres.MmNaBod * Nakres.Riadkovanie;
        var vyskaRiadku = velkostRiadku * Nakres.MmNaBod * Nakres.Riadkovanie;

        var dole = y + vyska - okraj;
        var hore = y + okraj;
        prvky.Add(new TextNakresu(textX, hore, textSirka, vyskaNadpisu, udaje.Nadpis, velkostNadpisu, Tucne: true));
        hore += vyskaNadpisu;

        // the name takes two lines when the label has room for them and the lines below still fit
        var volne = dole - hore;
        var riadkov = Math.Min(udaje.Riadky.Count, Math.Max(0, (int)((volne - vyskaNazvu) / vyskaRiadku)));
        var riadkovNazvu = volne - (riadkov * vyskaRiadku) >= 2 * vyskaNazvu ? 2 : 1;
        if (!string.IsNullOrWhiteSpace(udaje.Nazov) && volne >= vyskaNazvu)
        {
            prvky.Add(new TextNakresu(textX, hore, textSirka, vyskaNazvu * riadkovNazvu, udaje.Nazov, velkostNazvu, Tucne: true, Riadkov: riadkovNazvu));
            hore += vyskaNazvu * riadkovNazvu;
        }

        for (var i = 0; i < udaje.Riadky.Count && hore + vyskaRiadku <= dole + 0.01; i++)
        {
            prvky.Add(new TextNakresu(textX, hore, textSirka, vyskaRiadku, udaje.Riadky[i], velkostRiadku, Farba: "#333333"));
            hore += vyskaRiadku;
        }
    }
}
