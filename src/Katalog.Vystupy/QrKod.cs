using System.Text;

namespace Katalog.Vystupy;

/// <summary>How much of a QR code may be damaged and still be read (L 7 %, M 15 %, Q 25 %, H 30 %).</summary>
public enum UrovenOpravy
{
    L = 0,
    M = 1,
    Q = 2,
    H = 3,
}

/// <summary>
/// QR codes (ISO/IEC 18004) written here, without any library (owner, 8. 11. 2026): the text as UTF-8 in byte mode,
/// the smallest version 1–40 it fits, Reed–Solomon error correction in interleaved blocks, and of the eight masks the
/// one with the lowest penalty. The result is the matrix of modules (true = dark), without the quiet zone.
/// </summary>
public static class QrKod
{
    /// <summary>The longest text a label code takes (a label holds a number and a name, never a document).</summary>
    public const int NajdlhsiText = 1000;

    // error correction codewords per block and number of blocks, by level (L, M, Q, H) and version (index 0 unused)
    private static readonly sbyte[][] EccNaBlok =
    [
        [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    private static readonly sbyte[][] PocetBlokov =
    [
        [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];

    /// <summary>The two format bits of each level as the standard numbers them (L 01, M 00, Q 11, H 10).</summary>
    private static readonly int[] BityUrovne = [1, 0, 3, 2];

    /// <summary>The QR code of a text (UTF-8); <paramref name="maska"/> forces a mask 0–7 (tests), otherwise the best is chosen.</summary>
    public static bool[,] Vytvor(string text, UrovenOpravy uroven = UrovenOpravy.M, int? maska = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > NajdlhsiText)
        {
            throw new ArgumentException("Text QR kódu je príliš dlhý.", nameof(text));
        }

        var data = Encoding.UTF8.GetBytes(text);
        var verzia = 1;
        while (true)
        {
            var dlzkaPocitadla = verzia <= 9 ? 8 : 16;
            if (4 + dlzkaPocitadla + (data.Length * 8) <= PocetDatovychSlov(verzia, uroven) * 8)
            {
                break;
            }

            if (++verzia > 40)
            {
                throw new ArgumentException("Text sa do QR kódu nezmestí.", nameof(text));
            }
        }

        var slova = Kodove(data, verzia, uroven);
        return new Matica(verzia, uroven, slova, maska).Moduly;
    }

    // ------------------------------------------------------------------ codewords

    private static int PocetSurovychModulov(int verzia)
    {
        var vysledok = ((16 * verzia) + 128) * verzia + 64;
        if (verzia >= 2)
        {
            var zarovnania = (verzia / 7) + 2;
            vysledok -= ((25 * zarovnania) - 10) * zarovnania - 55;
            if (verzia >= 7)
            {
                vysledok -= 36;
            }
        }

        return vysledok;
    }

    private static int PocetDatovychSlov(int verzia, UrovenOpravy uroven) =>
        (PocetSurovychModulov(verzia) / 8) - (EccNaBlok[(int)uroven][verzia] * PocetBlokov[(int)uroven][verzia]);

    /// <summary>Mode, count, data, terminator and padding, then the error correction of each block, interleaved.</summary>
    private static byte[] Kodove(byte[] data, int verzia, UrovenOpravy uroven)
    {
        var kapacita = PocetDatovychSlov(verzia, uroven) * 8;
        var bity = new List<bool>(kapacita);
        void Pridaj(int hodnota, int dlzka)
        {
            for (var i = dlzka - 1; i >= 0; i--)
            {
                bity.Add(((hodnota >> i) & 1) != 0);
            }
        }

        Pridaj(0b0100, 4);
        Pridaj(data.Length, verzia <= 9 ? 8 : 16);
        foreach (var b in data)
        {
            Pridaj(b, 8);
        }

        Pridaj(0, Math.Min(4, kapacita - bity.Count));
        Pridaj(0, (8 - (bity.Count % 8)) % 8);
        for (var vypln = 0xEC; bity.Count < kapacita; vypln ^= 0xEC ^ 0x11)
        {
            Pridaj(vypln, 8);
        }

        var datove = new byte[bity.Count / 8];
        for (var i = 0; i < bity.Count; i++)
        {
            if (bity[i])
            {
                datove[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            }
        }

        // blocks: the short ones first; each gets its error correction
        var blokov = PocetBlokov[(int)uroven][verzia];
        var eccDlzka = EccNaBlok[(int)uroven][verzia];
        var surovych = PocetSurovychModulov(verzia) / 8;
        var kratkych = blokov - (surovych % blokov);
        var dlzkaKratkeho = surovych / blokov;
        var delitel = Delitel(eccDlzka);
        var bloky = new List<byte[]>(blokov);
        for (int i = 0, k = 0; i < blokov; i++)
        {
            var dlzka = dlzkaKratkeho - eccDlzka + (i < kratkych ? 0 : 1);
            var dat = datove.AsSpan(k, dlzka).ToArray();
            k += dlzka;
            var ecc = Zvysok(dat, delitel);
            var blok = new byte[dlzkaKratkeho + 1];
            dat.CopyTo(blok, 0);
            ecc.CopyTo(blok, dlzkaKratkeho + 1 - eccDlzka);
            bloky.Add(blok);
        }

        var vysledok = new List<byte>(surovych);
        for (var i = 0; i < bloky[0].Length; i++)
        {
            for (var j = 0; j < bloky.Count; j++)
            {
                // the short blocks have a hole where the long ones have their last data codeword
                if (i != dlzkaKratkeho - eccDlzka || j >= kratkych)
                {
                    vysledok.Add(bloky[j][i]);
                }
            }
        }

        return vysledok.ToArray();
    }

    private static byte[] Delitel(int stupen)
    {
        var vysledok = new byte[stupen];
        vysledok[stupen - 1] = 1;
        var koren = 1;
        for (var i = 0; i < stupen; i++)
        {
            for (var j = 0; j < stupen; j++)
            {
                vysledok[j] = Nasob(vysledok[j], koren);
                if (j + 1 < stupen)
                {
                    vysledok[j] ^= vysledok[j + 1];
                }
            }

            koren = Nasob(koren, 0x02);
        }

        return vysledok;
    }

    private static byte[] Zvysok(byte[] data, byte[] delitel)
    {
        var vysledok = new byte[delitel.Length];
        foreach (var b in data)
        {
            var faktor = b ^ vysledok[0];
            Array.Copy(vysledok, 1, vysledok, 0, vysledok.Length - 1);
            vysledok[^1] = 0;
            for (var i = 0; i < vysledok.Length; i++)
            {
                vysledok[i] ^= Nasob(delitel[i], faktor);
            }
        }

        return vysledok;
    }

    /// <summary>Multiplication in GF(2^8) with the polynomial 0x11D.</summary>
    private static byte Nasob(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    // ------------------------------------------------------------------ the matrix

    private sealed class Matica
    {
        private readonly int _velkost;
        private readonly bool[,] _moduly;
        private readonly bool[,] _funkcne;

        public Matica(int verzia, UrovenOpravy uroven, byte[] slova, int? maska)
        {
            _velkost = (verzia * 4) + 17;
            _moduly = new bool[_velkost, _velkost];
            _funkcne = new bool[_velkost, _velkost];
            KresliFunkcne(verzia, uroven);
            KresliSlova(slova);

            var najlepsia = maska ?? -1;
            if (najlepsia < 0)
            {
                var najmensia = int.MaxValue;
                for (var m = 0; m < 8; m++)
                {
                    Maskuj(m);
                    KresliFormat(uroven, m);
                    var penalta = Penalta();
                    if (penalta < najmensia)
                    {
                        najlepsia = m;
                        najmensia = penalta;
                    }

                    Maskuj(m);
                }
            }

            Maskuj(najlepsia);
            KresliFormat(uroven, najlepsia);
        }

        /// <summary>[y, x]: row, column.</summary>
        public bool[,] Moduly => _moduly;

        private void Nastav(int x, int y, bool tmavy)
        {
            _moduly[y, x] = tmavy;
            _funkcne[y, x] = true;
        }

        private void KresliFunkcne(int verzia, UrovenOpravy uroven)
        {
            for (var i = 0; i < _velkost; i++)
            {
                Nastav(6, i, i % 2 == 0);
                Nastav(i, 6, i % 2 == 0);
            }

            Hladacik(3, 3);
            Hladacik(_velkost - 4, 3);
            Hladacik(3, _velkost - 4);

            var pozicie = Zarovnania(verzia);
            for (var i = 0; i < pozicie.Length; i++)
            {
                for (var j = 0; j < pozicie.Length; j++)
                {
                    // not over the three finder patterns
                    if (!((i == 0 && j == 0) || (i == 0 && j == pozicie.Length - 1) || (i == pozicie.Length - 1 && j == 0)))
                    {
                        Zarovnanie(pozicie[i], pozicie[j]);
                    }
                }
            }

            KresliFormat(uroven, 0);
            KresliVerziu(verzia);
        }

        private void Hladacik(int x, int y)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var vzdialenost = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < _velkost && yy >= 0 && yy < _velkost)
                    {
                        Nastav(xx, yy, vzdialenost != 2 && vzdialenost != 4);
                    }
                }
            }
        }

        private void Zarovnanie(int x, int y)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    Nastav(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private int[] Zarovnania(int verzia)
        {
            if (verzia == 1)
            {
                return [];
            }

            var pocet = (verzia / 7) + 2;
            var krok = verzia == 32 ? 26 : (int)Math.Ceiling(((verzia * 4) + 4) / (double)((pocet * 2) - 2)) * 2;
            var vysledok = new int[pocet];
            vysledok[0] = 6;
            for (int i = pocet - 1, pozicia = _velkost - 7; i >= 1; i--, pozicia -= krok)
            {
                vysledok[i] = pozicia;
            }

            return vysledok;
        }

        private void KresliFormat(UrovenOpravy uroven, int maska)
        {
            var data = (BityUrovne[(int)uroven] << 3) | maska;
            var zvysok = data;
            for (var i = 0; i < 10; i++)
            {
                zvysok = (zvysok << 1) ^ ((zvysok >> 9) * 0x537);
            }

            var bity = ((data << 10) | zvysok) ^ 0x5412;
            bool Bit(int i) => ((bity >> i) & 1) != 0;

            for (var i = 0; i <= 5; i++)
            {
                Nastav(8, i, Bit(i));
            }

            Nastav(8, 7, Bit(6));
            Nastav(8, 8, Bit(7));
            Nastav(7, 8, Bit(8));
            for (var i = 9; i < 15; i++)
            {
                Nastav(14 - i, 8, Bit(i));
            }

            for (var i = 0; i < 8; i++)
            {
                Nastav(_velkost - 1 - i, 8, Bit(i));
            }

            for (var i = 8; i < 15; i++)
            {
                Nastav(8, _velkost - 15 + i, Bit(i));
            }

            Nastav(8, _velkost - 8, true);
        }

        private void KresliVerziu(int verzia)
        {
            if (verzia < 7)
            {
                return;
            }

            var zvysok = verzia;
            for (var i = 0; i < 12; i++)
            {
                zvysok = (zvysok << 1) ^ ((zvysok >> 11) * 0x1F25);
            }

            var bity = (verzia << 12) | zvysok;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bity >> i) & 1) != 0;
                int a = _velkost - 11 + (i % 3), b = i / 3;
                Nastav(a, b, bit);
                Nastav(b, a, bit);
            }
        }

        private void KresliSlova(byte[] slova)
        {
            var i = 0;
            for (var vpravo = _velkost - 1; vpravo >= 1; vpravo -= 2)
            {
                if (vpravo == 6)
                {
                    vpravo = 5;
                }

                for (var riadok = 0; riadok < _velkost; riadok++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = vpravo - j;
                        var hore = ((vpravo + 1) & 2) == 0;
                        var y = hore ? _velkost - 1 - riadok : riadok;
                        if (!_funkcne[y, x] && i < slova.Length * 8)
                        {
                            _moduly[y, x] = ((slova[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
                }
            }
        }

        private void Maskuj(int maska)
        {
            for (var y = 0; y < _velkost; y++)
            {
                for (var x = 0; x < _velkost; x++)
                {
                    var prepnut = maska switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
                        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
                    };
                    if (prepnut && !_funkcne[y, x])
                    {
                        _moduly[y, x] = !_moduly[y, x];
                    }
                }
            }
        }

        private int Penalta()
        {
            const int n1 = 3, n2 = 3, n3 = 40, n4 = 10;
            var vysledok = 0;
            Span<int> historia = stackalloc int[7];
            for (var smer = 0; smer < 2; smer++)
            {
                for (var a = 0; a < _velkost; a++)
                {
                    var farba = false;
                    var beh = 0;
                    historia.Clear();
                    for (var b = 0; b < _velkost; b++)
                    {
                        var modul = smer == 0 ? _moduly[a, b] : _moduly[b, a];
                        if (modul == farba)
                        {
                            beh++;
                            if (beh == 5)
                            {
                                vysledok += n1;
                            }
                            else if (beh > 5)
                            {
                                vysledok++;
                            }
                        }
                        else
                        {
                            PridajDoHistorie(beh, historia);
                            if (!farba)
                            {
                                vysledok += VzoryHladacika(historia) * n3;
                            }

                            farba = modul;
                            beh = 1;
                        }
                    }

                    if (farba)
                    {
                        PridajDoHistorie(beh, historia);
                        beh = 0;
                    }

                    beh += _velkost;
                    PridajDoHistorie(beh, historia);
                    vysledok += VzoryHladacika(historia) * n3;
                }
            }

            var tmavych = 0;
            for (var y = 0; y < _velkost; y++)
            {
                for (var x = 0; x < _velkost; x++)
                {
                    tmavych += _moduly[y, x] ? 1 : 0;
                    if (y < _velkost - 1 && x < _velkost - 1)
                    {
                        var c = _moduly[y, x];
                        if (c == _moduly[y, x + 1] && c == _moduly[y + 1, x] && c == _moduly[y + 1, x + 1])
                        {
                            vysledok += n2;
                        }
                    }
                }
            }

            var spolu = _velkost * _velkost;
            var k = (int)Math.Ceiling(Math.Abs((tmavych * 20) - (spolu * 10)) / (double)spolu) - 1;
            return vysledok + (k * n4);
        }

        private void PridajDoHistorie(int beh, Span<int> historia)
        {
            if (historia[0] == 0)
            {
                // the light border before the first run
                beh += _velkost;
            }

            for (var i = historia.Length - 1; i > 0; i--)
            {
                historia[i] = historia[i - 1];
            }

            historia[0] = beh;
        }

        private static int VzoryHladacika(ReadOnlySpan<int> h)
        {
            var n = h[1];
            var jadro = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (jadro && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (jadro && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }
    }
}
