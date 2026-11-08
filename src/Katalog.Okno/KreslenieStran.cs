using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Katalog.Vystupy;

namespace Katalog.Okno;

/// <summary>
/// Draws the pages the outputs laid out (labels, tables) with WPF: as vectors for the printer and the preview, as
/// pixels for the PDF. One drawing for all three, so what the preview shows is what comes out.
/// </summary>
internal static class KreslenieStran
{
    /// <summary>Device-independent pixels (1/96 inch) of a millimetre.</summary>
    public const double DipNaMm = 96 / 25.4;

    /// <summary>The resolution of PDF pages: sharp in print, still small.</summary>
    public const int DpiPdf = 300;

    private static readonly Typeface Pismo = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface PismoTucne = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static DrawingVisual Nakresli(StranaNakresu strana)
    {
        var vizual = new DrawingVisual();
        using (var k = vizual.RenderOpen())
        {
            // white paper (the preview in the dark theme too)
            k.DrawRectangle(Brushes.White, null, new Rect(0, 0, strana.Sirka * DipNaMm, strana.Vyska * DipNaMm));
            foreach (var prvok in strana.Prvky)
            {
                Kresli(k, prvok);
            }
        }

        return vizual;
    }

    private static void Kresli(DrawingContext k, PrvokNakresu prvok)
    {
        switch (prvok)
        {
            case ObdlznikNakresu o:
                k.DrawRectangle(o.Vypln is { } v ? Stetec(v) : null, o.Hrubka > 0 ? new Pen(Stetec(o.Farba), o.Hrubka * DipNaMm) : null,
                    Obdlznik(o.X, o.Y, o.Sirka, o.Vyska));
                break;
            case CiaraNakresu c:
                k.DrawLine(new Pen(Stetec(c.Farba), c.Hrubka * DipNaMm), new Point(c.X1 * DipNaMm, c.Y1 * DipNaMm), new Point(c.X2 * DipNaMm, c.Y2 * DipNaMm));
                break;
            case QrNakresu q:
                k.DrawGeometry(Brushes.Black, null, Moduly(q));
                break;
            case ObrazokNakresu o when File.Exists(o.Cesta):
                if (Obrazok(o.Cesta) is { } obrazok)
                {
                    var mierka = Math.Min(o.Sirka / obrazok.Width, o.Vyska / obrazok.Height);
                    var w = obrazok.Width * mierka;
                    var h = obrazok.Height * mierka;
                    k.DrawImage(obrazok, Obdlznik(o.X + ((o.Sirka - w) / 2), o.Y + ((o.Vyska - h) / 2), w, h));
                }

                break;
            case TextNakresu t when t.Text.Length > 0:
                var text = new FormattedText(t.Text, CultureInfo.GetCultureInfo("sk-SK"), FlowDirection.LeftToRight,
                    t.Tucne ? PismoTucne : Pismo, t.Velkost * 96 / 72, Stetec(t.Farba), 1.0)
                {
                    // the number of lines bounds the height (a height bound would drop a line a hair taller than the box)
                    MaxTextWidth = Math.Max(1, t.Sirka * DipNaMm),
                    MaxLineCount = Math.Max(1, t.Riadkov),
                    Trimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = t.Zarovnanie switch
                    {
                        ZarovnanieTextu.Stred => TextAlignment.Center,
                        ZarovnanieTextu.Vpravo => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    },
                };
                k.DrawText(text, new Point(t.X * DipNaMm, t.Y * DipNaMm));
                break;
        }
    }

    /// <summary>The dark modules as one geometry (runs of a row joined), so the printer gets clean squares.</summary>
    private static StreamGeometry Moduly(QrNakresu q)
    {
        var n = q.Moduly.GetLength(0);
        var modul = q.Strana / n * DipNaMm;
        var geometria = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var g = geometria.Open())
        {
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    if (!q.Moduly[y, x])
                    {
                        continue;
                    }

                    var zaciatok = x;
                    while (x + 1 < n && q.Moduly[y, x + 1])
                    {
                        x++;
                    }

                    var lx = (q.X * DipNaMm) + (zaciatok * modul);
                    var ly = (q.Y * DipNaMm) + (y * modul);
                    var px = (q.X * DipNaMm) + ((x + 1) * modul);
                    // a hair taller, so neighbouring rows meet without a light seam
                    var py = ly + modul + 0.05;
                    g.BeginFigure(new Point(lx, ly), isFilled: true, isClosed: true);
                    g.LineTo(new Point(px, ly), isStroked: false, isSmoothJoin: false);
                    g.LineTo(new Point(px, py), isStroked: false, isSmoothJoin: false);
                    g.LineTo(new Point(lx, py), isStroked: false, isSmoothJoin: false);
                }
            }
        }

        geometria.Freeze();
        return geometria;
    }

    /// <summary>A page as pixels for the PDF (white paper, RGB rows from the top).</summary>
    public static RastrovaStrana Rastruj(StranaNakresu strana, int dpi = DpiPdf)
    {
        var sirka = (int)Math.Round(strana.Sirka / 25.4 * dpi);
        var vyska = (int)Math.Round(strana.Vyska / 25.4 * dpi);
        var obraz = new RenderTargetBitmap(sirka, vyska, dpi, dpi, PixelFormats.Pbgra32);
        obraz.Render(Nakresli(strana));
        var bgra = new byte[sirka * vyska * 4];
        obraz.CopyPixels(bgra, sirka * 4, 0);
        var rgb = new byte[sirka * vyska * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            // on a white page alpha is full; blend the rest onto white anyway
            var a = bgra[i + 3];
            rgb[j] = (byte)(bgra[i + 2] + (255 - a));
            rgb[j + 1] = (byte)(bgra[i + 1] + (255 - a));
            rgb[j + 2] = (byte)(bgra[i] + (255 - a));
        }

        return new RastrovaStrana(strana.Sirka, strana.Vyska, sirka, vyska, rgb);
    }

    /// <summary>A small picture of a page for the preview.</summary>
    public static BitmapSource Nahlad(StranaNakresu strana, double dpi = 96)
    {
        var sirka = Math.Max(1, (int)Math.Round(strana.Sirka / 25.4 * dpi));
        var vyska = Math.Max(1, (int)Math.Round(strana.Vyska / 25.4 * dpi));
        var obraz = new RenderTargetBitmap(sirka, vyska, dpi, dpi, PixelFormats.Pbgra32);
        obraz.Render(Nakresli(strana));
        obraz.Freeze();
        return obraz;
    }

    /// <summary>Writes the pages into a PDF (one page drawn at a time, so memory stays small).</summary>
    public static void UlozPdf(IReadOnlyList<StranaNakresu> strany, string cesta, string nazov)
    {
        var docasny = cesta + ".tmp";
        using (var subor = File.Create(docasny))
        {
            PdfSubor.Zapis(subor, strany.Select(s => Rastruj(s)), nazov);
        }

        File.Move(docasny, cesta, overwrite: true);
    }

    private static Rect Obdlznik(double x, double y, double sirka, double vyska) =>
        new(x * DipNaMm, y * DipNaMm, Math.Max(0, sirka * DipNaMm), Math.Max(0, vyska * DipNaMm));

    private static SolidColorBrush Stetec(string farba)
    {
        var stetec = new SolidColorBrush((Color)ColorConverter.ConvertFromString(farba));
        stetec.Freeze();
        return stetec;
    }

    private static BitmapImage? Obrazok(string cesta)
    {
        try
        {
            var obrazok = new BitmapImage();
            obrazok.BeginInit();
            obrazok.CacheOption = BitmapCacheOption.OnLoad;
            obrazok.UriSource = new Uri(cesta);
            obrazok.EndInit();
            obrazok.Freeze();
            return obrazok;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Pages for the printer: each page its own size (an A4 sheet, or one label of a label printer).</summary>
internal sealed class StrankovacNakresu(IReadOnlyList<StranaNakresu> strany) : DocumentPaginator
{
    public override bool IsPageCountValid => true;

    public override int PageCount => strany.Count;

    public override Size PageSize
    {
        get => strany.Count > 0 ? new Size(strany[0].Sirka * KreslenieStran.DipNaMm, strany[0].Vyska * KreslenieStran.DipNaMm) : new Size(793.7, 1122.5);
        set { }
    }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        var strana = strany[pageNumber];
        var velkost = new Size(strana.Sirka * KreslenieStran.DipNaMm, strana.Vyska * KreslenieStran.DipNaMm);
        return new DocumentPage(KreslenieStran.Nakresli(strana), velkost, new Rect(velkost), new Rect(velkost));
    }
}
