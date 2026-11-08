using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Katalog.Vystupy;

/// <summary>One page as pixels: its size on paper (mm) and the RGB bytes, row by row from the top (3 bytes a pixel).</summary>
public sealed record RastrovaStrana(double SirkaMm, double VyskaMm, int SirkaPx, int VyskaPx, byte[] Rgb);

/// <summary>
/// A PDF of drawn pages (PDF 1.4, written here without a library): each page one picture, compressed without loss
/// (Flate); pages with only greys are stored as greys. Text is then not searchable, but every letter looks exactly
/// as on the screen and on the printer, Slovak ones included, with no font to embed.
/// </summary>
public static class PdfSubor
{
    public static void Zapis(Stream vystup, IEnumerable<RastrovaStrana> strany, string nazov)
    {
        ArgumentNullException.ThrowIfNull(vystup);
        ArgumentNullException.ThrowIfNull(strany);
        var pdf = new Zapisovac(vystup);
        pdf.Text("%PDF-1.4\n%âãÏÓ\n");

        // 1 catalog, 2 pages, 3 info; each page takes three objects: page, content, picture
        var stranyId = new List<int>();
        var dalsi = 4;
        foreach (var strana in strany)
        {
            if (strana.SirkaPx <= 0 || strana.VyskaPx <= 0 || strana.Rgb.Length != strana.SirkaPx * strana.VyskaPx * 3)
            {
                throw new ArgumentException("Strana PDF nemá správne rozmery.", nameof(strany));
            }

            int stranaId = dalsi, obsahId = dalsi + 1, obrazokId = dalsi + 2;
            dalsi += 3;
            stranyId.Add(stranaId);
            var w = Bod(strana.SirkaMm);
            var h = Bod(strana.VyskaMm);

            pdf.Objekt(stranaId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 {obrazokId} 0 R >> >> /Contents {obsahId} 0 R >>");
            var obsah = Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Im0 Do Q\n");
            pdf.Prud(obsahId, "", obsah);

            var sivy = JeSiva(strana.Rgb);
            var pixely = sivy ? Siva(strana.Rgb) : strana.Rgb;
            pdf.Prud(obrazokId,
                $"/Type /XObject /Subtype /Image /Width {strana.SirkaPx} /Height {strana.VyskaPx} /ColorSpace /{(sivy ? "DeviceGray" : "DeviceRGB")} /BitsPerComponent 8 /Filter /FlateDecode ",
                Zbal(pixely));
        }

        if (stranyId.Count == 0)
        {
            throw new ArgumentException("PDF potrebuje aspoň jednu stranu.", nameof(strany));
        }

        pdf.Objekt(1, "<< /Type /Catalog /Pages 2 0 R >>");
        pdf.Objekt(2, $"<< /Type /Pages /Kids [{string.Join(' ', stranyId.Select(i => $"{i} 0 R"))}] /Count {stranyId.Count} >>");
        pdf.Objekt(3, $"<< /Title {TextPdf(nazov)} /Producer {TextPdf(Jadro.Produkt.Nazov + " " + Jadro.Produkt.VerziaText)} >>");
        pdf.Koniec(dalsi, korenId: 1, infoId: 3);
    }

    /// <summary>Points (1/72 inch) of millimetres, as PDF counts.</summary>
    private static string Bod(double mm) => (mm * 72 / 25.4).ToString("0.###", CultureInfo.InvariantCulture);

    private static bool JeSiva(byte[] rgb)
    {
        for (var i = 0; i < rgb.Length; i += 3)
        {
            if (rgb[i] != rgb[i + 1] || rgb[i] != rgb[i + 2])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] Siva(byte[] rgb)
    {
        var vysledok = new byte[rgb.Length / 3];
        for (var i = 0; i < vysledok.Length; i++)
        {
            vysledok[i] = rgb[i * 3];
        }

        return vysledok;
    }

    private static byte[] Zbal(byte[] data)
    {
        using var pamat = new MemoryStream();
        using (var zlib = new ZLibStream(pamat, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return pamat.ToArray();
    }

    /// <summary>A text string as UTF-16 with a byte order mark (hex), so any name fits.</summary>
    private static string TextPdf(string text)
    {
        var bajty = Encoding.BigEndianUnicode.GetBytes(text);
        return "<FEFF" + Convert.ToHexString(bajty) + ">";
    }

    private sealed class Zapisovac(Stream vystup)
    {
        private readonly Dictionary<int, long> _pozicie = [];
        private long _pozicia;

        public void Text(string text) => Bajty(Encoding.Latin1.GetBytes(text));

        public void Objekt(int id, string telo)
        {
            _pozicie[id] = _pozicia;
            Text($"{id} 0 obj\n{telo}\nendobj\n");
        }

        public void Prud(int id, string slovnik, byte[] data)
        {
            _pozicie[id] = _pozicia;
            Text($"{id} 0 obj\n<< {slovnik}/Length {data.Length} >>\nstream\n");
            Bajty(data);
            Text("\nendstream\nendobj\n");
        }

        public void Koniec(int pocet, int korenId, int infoId)
        {
            var xref = _pozicia;
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"xref\n0 {pocet}\n0000000000 65535 f \n");
            for (var id = 1; id < pocet; id++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{_pozicie[id]:0000000000} 00000 n \n");
            }

            text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {pocet} /Root {korenId} 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Text(text.ToString());
        }

        private void Bajty(byte[] data)
        {
            vystup.Write(data);
            _pozicia += data.Length;
        }
    }
}
