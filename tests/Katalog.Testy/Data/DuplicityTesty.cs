using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Media;
using Katalog.Skenovanie;
using Katalog.Ulohy;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

/// <summary>Duplicates, the best version, titles on one disk only (N1) and exact copies of files (stage E5).</summary>
public sealed class DuplicityTesty
{
    [Test]
    public void NajlepsiaVerzia()
    {
        VlastnostiVerzie V(long id, TriedaKvality t, string[] zvuk, string kodek = "h264", long velkost = 4_000_000_000, string[]? titulky = null, bool pritomna = true) =>
            new(id, t, t == TriedaKvality.UHD4K ? 2160 : t == TriedaKvality.FHD ? 1080 : 720, kodek, zvuk, titulky ?? [], velkost, false, pritomna);

        var fhdEn = V(1, TriedaKvality.FHD, ["en"]);
        var hdCz = V(2, TriedaKvality.HD, ["cs", "en"]);
        var uhdEnZmazana = V(3, TriedaKvality.UHD4K, ["en"], pritomna: false);
        var poradie = PorovnanieVerzii.Predvolene;
        Over.Rovne<long?>(1, PorovnanieVerzii.Najlepsia([fhdEn, hdCz, uhdEnZmazana], poradie), "rozlíšenie rozhoduje prvé; zmazaná sa nepočíta");
        var zvukPrvy = PorovnanieVerzii.ZTextu("2,1");
        Over.Postupnost([KriteriumVerzie.ZvukCzSk, KriteriumVerzie.Rozlisenie, KriteriumVerzie.Titulky, KriteriumVerzie.Kodek, KriteriumVerzie.Velkost], zvukPrvy,
            "vlastné poradie doplnené o ostatné");
        Over.Rovne<long?>(2, PorovnanieVerzii.Najlepsia([fhdEn, hdCz], zvukPrvy), "s CZ zvukom na prvom mieste");
        Over.Rovne("lepšie rozlíšenie", PorovnanieVerzii.Dovod(fhdEn, hdCz, poradie), "dôvod");

        var h264 = V(4, TriedaKvality.FHD, ["cs"], "h264", 8_000_000_000);
        var hevc = V(5, TriedaKvality.FHD, ["cs"], "hevc", 4_000_000_000);
        Over.Rovne<long?>(5, PorovnanieVerzii.Najlepsia([h264, hevc], poradie), "novší kodek pred veľkosťou");
        Over.Rovne<long?>(null, PorovnanieVerzii.Najlepsia([hevc, hevc with { VerziaId = 6, Velkost = 4_050_000_000 }], poradie), "rovnaké verzie – žiadna lepšia");
        Over.Rovne<long?>(null, PorovnanieVerzii.Najlepsia([hevc], poradie), "jedna verzia nie je duplicita");
        Over.Postupnost(PorovnanieVerzii.Predvolene, PorovnanieVerzii.ZTextu("x,9,,1,1"), "zlé poradie dá predvolené");
        Over.Rovne("1,2,3,4,5", PorovnanieVerzii.NaText(PorovnanieVerzii.Predvolene), "uloženie poradia");
    }

    [Test]
    public void OdtlacokKopie()
    {
        using var priecinok = new DocasnyPriecinok();
        var a = priecinok.Subor("a.mkv");
        var data = new byte[3 << 20];
        new Random(5).NextBytes(data);
        File.WriteAllBytes(a, data);
        var b = priecinok.Subor("b.mkv");
        File.WriteAllBytes(b, data);
        var odtlacok = OdtlacokSuboru.Vypocitaj(a, data.Length)!;
        Over.Rovne(16, odtlacok.Length, "16 bajtov");
        Over.Postupnost(odtlacok, OdtlacokSuboru.Vypocitaj(b, data.Length)!, "kópia má rovnaký odtlačok");
        data[^1] ^= 1;
        File.WriteAllBytes(b, data);
        Over.Pravda(!odtlacok.SequenceEqual(OdtlacokSuboru.Vypocitaj(b, data.Length)!), "zmena na konci sa prejaví");
        data[^1] ^= 1;
        data[(int)(1.5 * (1 << 20))] ^= 1;
        File.WriteAllBytes(b, data);
        Over.Postupnost(odtlacok, OdtlacokSuboru.Vypocitaj(b, data.Length)!, "stred súboru sa nečíta");
        Over.Rovne<byte[]?>(null, OdtlacokSuboru.Vypocitaj(a, data.Length + 1), "iná veľkosť – nič");
        Over.Rovne<byte[]?>(null, OdtlacokSuboru.Vypocitaj(priecinok.Subor("nie.mkv"), 1), "chýbajúci súbor – nič");
        var maly = priecinok.Subor("maly.srt");
        File.WriteAllBytes(maly, [1, 2, 3]);
        Over.Rovne(16, OdtlacokSuboru.Vypocitaj(maly, 3)!.Length, "malý súbor");
    }

    [Test]
    public async Task DuplicityAZalohy()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var prvy = await DiskAsync(kniznica, priecinok.Cesta, katalog, "prvy", "1111-1111", new[]
        {
            @"MOVIES\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv",
            @"MOVIES\Vymyslený jediný (2002)\Vymyslený jediný (2002) - EN FHD H264 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E01 - CZ HD H264 SMALL.mkv",
            @"TVSHOWS\Vymyslený seriál (2020)\Season 01\Vymyslený seriál S01E02 - CZ HD H264 SMALL.mkv",
        });
        var druhy = await DiskAsync(kniznica, priecinok.Cesta, katalog, "druhy", "2222-2222", new[]
        {
            @"Zálohy\Vymyslený film (2001)\Vymyslený film (2001) - EN FHD H264 SMALL.mkv",
            @"Zálohy\Vymyslený seriál S01E01.mkv",
        });
        foreach (var disk in new[] { prvy, druhy })
        {
            await RozpoznanieDisku.SpustiAsync(kniznica, disk, "", CancellationToken.None);
        }

        // the two copies of the film are the same file
        await kniznica.Zapis.ZapisAsync(s => Vykonaj(s, "UPDATE subor SET odtlacok = zeroblob(16) WHERE nazov = 'Vymyslený film (2001) - EN FHD H264 SMALL.mkv'"));
        var nikto = new KontextHladania([], DateTime.Now);
        using var c = kniznica.OtvorCitanie();
        IEnumerable<string> Tituly(string text) => KniznicaTitulov.Najdi(c, Dotaz.Citaj(text), nikto, PohladKniznice.Vsetko).Select(t => t.Nazov);

        Over.Postupnost(["Vymyslený film", "Vymyslený seriál"], Tituly("verzie:>1"), "duplicity: film dvakrát, epizóda dvakrát");
        Over.Postupnost(["Vymyslený film"], Tituly("verzie:2 kniznica:film"), "presne dve verzie");
        Over.Postupnost(["Vymyslený jediný", "Vymyslený seriál"], Tituly("zalohovane:nie"), "len na jednom disku (seriál: S01E02)");
        Over.Postupnost(["Vymyslený film"], Tituly("zalohovane:ano"), "zálohovaný film");
        Over.Rovne(2, Hladanie.Najdi(c, Dotaz.Citaj("duplikat:ano"), nikto).Polozky.Count, "presné kópie súborov");
        Over.Rovne(0, Hladanie.Najdi(c, Dotaz.Citaj("duplikat:nie"), nikto).Polozky.Count, "bez odtlačku sa nepočíta");
        Over.Pravda(!Dotaz.Citaj("duplikat:ano").HladaPriecinky, "duplikát je len súbor");

        var film = KniznicaTitulov.Najdi(c, Dotaz.Citaj("vymysleny film"), nikto, PohladKniznice.Filmy).Single();
        var detail = KniznicaTitulov.Detail(c, film.Id)!;
        Over.Rovne(2, detail.Vlastnosti.Count, "vlastnosti dvoch verzií");
        Over.Rovne(0, detail.NajlepsieVerzie.Count, "rovnaké verzie (bez technických údajov) – žiadna lepšia");
    }

    private static async Task<long> DiskAsync(Kniznica kniznica, string priecinok, long katalog, string meno, string seriove, IEnumerable<string> subory)
    {
        var disk = Path.Combine(priecinok, meno);
        foreach (var subor in subory)
        {
            var cesta = Path.Combine(disk, subor.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
            await File.WriteAllBytesAsync(cesta, new byte[10]);
        }

        var identita = new IdentitaZvazku(DruhDisku.Usb, seriove, "{" + meno + "}", null, meno, "NTFS", 1_000_000, null);
        var koren = kniznica.NovyIdPriecinka();
        var diskId = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, meno, identita, "", null), koren));
        var sken = await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new Zvazok(identita)), null, CancellationToken.None);
        Over.Rovne(StavUlohy.Hotova, sken.Stav, "sken " + meno);
        return diskId;
    }

    private static void Vykonaj(SqliteConnection c, string sql)
    {
        using var p = c.CreateCommand();
        p.CommandText = sql;
        p.ExecuteNonQuery();
    }

    private sealed class Zvazok(IdentitaZvazku identita) : IZvazky
    {
        public IReadOnlyList<PripojenyZvazok> Pripojene() => [];

        public IdentitaZvazku? Identita(string cesta) => Directory.Exists(cesta) ? identita : null;

        public (long? Velkost, long? Volne) Miesto(string cesta) => (null, null);
    }
}
