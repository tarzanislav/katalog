namespace Katalog.Vystupy;

/// <summary>A table laid out on A4 pages for print and PDF: title, heading repeated on every page, page numbers.</summary>
public static class StranyTabulky
{
    private const double Okraj = 12;
    private const double VelkostTextu = 7.5;
    private const double VyskaRiadku = 4.6;
    private const double VyskaHlavicky = 5.6;

    /// <param name="naSirku">Landscape, for tables with many columns.</param>
    /// <param name="vytlacene">The date and time on the footer (local).</param>
    public static IReadOnlyList<StranaNakresu> Rozloz(Tabulka tabulka, bool naSirku, DateTime vytlacene)
    {
        ArgumentNullException.ThrowIfNull(tabulka);
        var sirka = naSirku ? Nakres.VyskaA4 : Nakres.SirkaA4;
        var vyska = naSirku ? Nakres.SirkaA4 : Nakres.VyskaA4;
        var plocha = sirka - (2 * Okraj);

        // column widths by their weights; numbers and dates need less than names
        var vahy = tabulka.Stlpce.Select(s => Math.Max(0.2, s.Sirka) * s.Druh switch
        {
            DruhStlpca.Text => 1,
            DruhStlpca.Datum => 1.05,
            DruhStlpca.Rok => 0.4,
            _ => 0.6,
        }).ToArray();
        var sucet = Math.Max(0.001, vahy.Sum());
        var sirky = vahy.Select(v => plocha * v / sucet).ToArray();

        var strany = new List<List<PrvokNakresu>>();
        List<PrvokNakresu> prvky = [];
        double y = 0;

        void NovaStrana(bool prva)
        {
            prvky = [];
            strany.Add(prvky);
            y = Okraj;
            if (prva)
            {
                prvky.Add(new TextNakresu(Okraj, y, plocha, 7, tabulka.Nazov, 14, Tucne: true));
                y += 7.5;
                var popis = string.Join(" · ", new[] { tabulka.Popis, Pocet(tabulka.Riadky.Count) }.Where(t => !string.IsNullOrEmpty(t)));
                prvky.Add(new TextNakresu(Okraj, y, plocha, 4, popis, 8, Farba: "#555555"));
                y += 6;
            }

            var x = Okraj;
            for (var i = 0; i < tabulka.Stlpce.Count; i++)
            {
                prvky.Add(new TextNakresu(x + 0.8, y + 0.8, sirky[i] - 1.6, VyskaHlavicky - 1.2, tabulka.Stlpce[i].Nazov, VelkostTextu, Tucne: true,
                    Zarovnanie: JeCislo(tabulka.Stlpce[i].Druh) ? ZarovnanieTextu.Vpravo : ZarovnanieTextu.Vlavo));
                x += sirky[i];
            }

            y += VyskaHlavicky;
            prvky.Add(new CiaraNakresu(Okraj, y, Okraj + plocha, y, 0.3));
        }

        NovaStrana(prva: true);
        var spodok = vyska - Okraj - 6;
        for (var r = 0; r < tabulka.Riadky.Count; r++)
        {
            if (y + VyskaRiadku > spodok)
            {
                NovaStrana(prva: false);
            }

            if (r % 2 == 1)
            {
                prvky.Add(new ObdlznikNakresu(Okraj, y, plocha, VyskaRiadku, 0, "#F2F2F2"));
            }

            var riadok = tabulka.Riadky[r];
            var x = Okraj;
            for (var i = 0; i < tabulka.Stlpce.Count; i++)
            {
                var hodnota = i < riadok.Length ? riadok[i] : null;
                var text = FormatVystupu.NaCitanie(hodnota, tabulka.Stlpce[i].Druh);
                if (text.Length > 0)
                {
                    prvky.Add(new TextNakresu(x + 0.8, y + 0.6, sirky[i] - 1.6, VyskaRiadku - 1, text, VelkostTextu,
                        Zarovnanie: JeCislo(tabulka.Stlpce[i].Druh) ? ZarovnanieTextu.Vpravo : ZarovnanieTextu.Vlavo));
                }

                x += sirky[i];
            }

            y += VyskaRiadku;
        }

        var spolu = strany.Count;
        var vysledok = new List<StranaNakresu>(spolu);
        for (var s = 0; s < spolu; s++)
        {
            var paticka = $"{tabulka.Nazov} · {FormatVystupu.Datum(vytlacene)} · {Jadro.Produkt.Nazov}";
            strany[s].Add(new CiaraNakresu(Okraj, vyska - Okraj - 4.5, Okraj + plocha, vyska - Okraj - 4.5, 0.2, "#999999"));
            strany[s].Add(new TextNakresu(Okraj, vyska - Okraj - 3.5, plocha * 0.8, 3.5, paticka, 7, Farba: "#666666"));
            strany[s].Add(new TextNakresu(Okraj + (plocha * 0.8), vyska - Okraj - 3.5, plocha * 0.2, 3.5, $"Strana {s + 1} z {spolu}", 7,
                Zarovnanie: ZarovnanieTextu.Vpravo, Farba: "#666666"));
            vysledok.Add(new StranaNakresu(sirka, vyska, strany[s]));
        }

        return vysledok;
    }

    private static bool JeCislo(DruhStlpca druh) => druh is DruhStlpca.Cislo or DruhStlpca.Desatinne or DruhStlpca.Velkost or DruhStlpca.Trvanie or DruhStlpca.Rok;

    private static string Pocet(int pocet) =>
        pocet.ToString("#,0", FormatVystupu.Slovensky) + (pocet == 1 ? " riadok" : pocet is >= 2 and <= 4 ? " riadky" : " riadkov");
}
