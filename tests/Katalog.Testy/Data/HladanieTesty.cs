using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;
using Katalog.Ulohy;

namespace Katalog.Testy;

/// <summary>The query language run over a library of two invented disks in two catalogues; saved searches.</summary>
public sealed class HladanieTesty
{
    private static readonly IdentitaZvazku Identita1 = new(DruhDisku.Usb, "1111-1111", "{vymysleny-1}", null, "Videotéka", "NTFS", 1_000_000, null);
    private static readonly IdentitaZvazku Identita2 = new(DruhDisku.Usb, "2222-2222", "{vymysleny-2}", null, "Zálohy", "NTFS", 1_000_000, null);

    [Test]
    public async Task SlovaAPolia()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var (disk1, disk2) = await PripravAsync(kniznica, priecinok.Cesta);
        using var c = kniznica.OtvorCitanie();
        var nikto = new KontextHladania([], DateTime.Now);

        var stastny = Najdi(c, "šťastný film", nikto);
        Over.Postupnost(
            ["Vymyslený Šťastný film (2024)", "Vymyslený Šťastný film (2024).cs.srt", "Vymyslený Šťastný film (2024).mkv", "Vymyslený Šťastný film (2024).mkv"],
            stastny.Polozky.Select(p => p.Nazov), "priečinky prvé, potom súbory podľa názvu");
        Over.Rovne(DruhHladania.Priecinok, stastny.Polozky[0].Druh, "prvý je priečinok");
        Over.Pravda(!stastny.Viac, "nič viac");

        Over.Rovne(2, Najdi(c, "stastny FILM typ:video", nikto).Polozky.Count, "bez diakritiky, len videá");
        Over.Rovne(3, Najdi(c, "stastny -srt", nikto).Polozky.Count, "záporné slovo");
        Over.Postupnost(["Vymyslený seriál S01E01.mp4", "Vymyslený Šťastný film (2024).cs.srt"],
            Nazvy(c, "pripona:srt,mp4", nikto), "prípony");
        Over.Postupnost(["Vymyslený seriál S01E01.mp4", "Vymyslený Šťastný film (2024).mkv"],
            Nazvy(c, "velkost:>2kB", nikto), "veľkosť");
        Over.Rovne(3, Najdi(c, "-pripona:mkv typ:subor", nikto).Polozky.Count, "záporné pole");
        Over.Postupnost(["Vymyslený Šťastný film (2024).cs.srt", "Vymyslený Šťastný film (2024).mkv"],
            Nazvy(c, "zmenene:2024", nikto), "rok zmeny");
        Over.Rovne(6, Najdi(c, "pridane:1d", nikto).Polozky.Count, "pridané dnes");
        Over.Postupnost(["Vymyslený seriál S01E01.mp4"], Nazvy(c, "s0", nikto), "krátke slovo bez indexu");
        Over.Rovne(1, Najdi(c, "S01E01", nikto).Polozky.Count, "písmená a čísla");

        Over.Postupnost(["Zálohy", "Vymyslený Šťastný film (2024).mkv"], Nazvy(c, "disk:2", nikto), "číslo disku");
        Over.Rovne(2, Najdi(c, "katalog:zálohy", nikto).Polozky.Count, "katalóg");
        Over.Rovne(9, Najdi(c, "katalog:\"Vymyslená videotéka\" typ:priecinok", nikto).Polozky.Count, "všetky priečinky katalógu okrem koreňa");
        Over.Rovne(1, Najdi(c, "disk:\"Vymyslený disk 1\" typ:priecinok bdmv", nikto).Polozky.Count, "disk podľa názvu");

        var druhyPripojeny = new KontextHladania([disk2], DateTime.Now);
        Over.Postupnost([disk2], Najdi(c, "pripojene:ano vymyslený", druhyPripojeny).Polozky.Select(p => p.DiskId), "len na pripojenom disku");
        Over.Rovne(5, Najdi(c, "pripojene:nie typ:subor", druhyPripojeny).Polozky.Where(p => p.DiskId == disk1).Count(), "len na nepripojených");

        var obmedzene = Hladanie.Najdi(c, Dotaz.Citaj("vymyslený"), nikto, najviac: 2);
        Over.Rovne(2, obmedzene.Polozky.Count, "najviac dve");
        Over.Pravda(obmedzene.Viac, "a vie, že je ich viac");

        var epizoda = Najdi(c, "s01e01", nikto).Polozky.Single();
        Over.Rovne("Seriály/Vymyslený seriál/Season 01", Hladanie.Cesty(c, [epizoda.PriecinokId])[epizoda.PriecinokId], "cesta výsledku");

        // what the user writes never becomes SQL
        Over.Rovne(0, Najdi(c, "\"'; DROP TABLE subor; --\" disk:\"x' OR 1=1\" katalog:%", nikto).Polozky.Count, "znaky SQL sú len text");
        Over.Rovne(0, Najdi(c, "0%", nikto).Polozky.Count, "percento v krátkom slove nie je zástupný znak");
        Over.Rovne(6L, KatalogyADisky.SuhrnDisku(c, disk1).Subory + KatalogyADisky.SuhrnDisku(c, disk2).Subory, "knižnica ostala celá");
        Over.Rovne(0, Hladanie.Najdi(c, Dotaz.Citaj("zmazane:aj"), nikto).Polozky.Count, "prázdny dotaz nič nehľadá");
    }

    [Test]
    public async Task ZmazanePolozky()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var (disk1, _) = await PripravAsync(kniznica, priecinok.Cesta);
        File.Delete(Path.Combine(priecinok.Cesta, "d1", "Filmy", "Vymyslená ukážka (2023)", "Vymyslená ukážka sample.mkv"));
        await SkenovanieDisku.SpustiAsync(kniznica, disk1, new LokalneUlozisko(Path.Combine(priecinok.Cesta, "d1"), DruhDisku.Usb, new Zvazok(Identita1)),
            null, CancellationToken.None);

        using var c = kniznica.OtvorCitanie();
        var nikto = new KontextHladania([], DateTime.Now);
        Over.Postupnost(["Vymyslená ukážka (2023)"], Nazvy(c, "ukážka", nikto), "zmazaný súbor sa predvolene nehľadá");
        Over.Rovne(2, Najdi(c, "ukazka zmazane:aj", nikto).Polozky.Count, "aj zmazané");
        var len = Najdi(c, "ukazka zmazane:len", nikto).Polozky.Single();
        Over.Pravda(len.Zmazana && len.Druh == DruhHladania.Subor, "len zmazané");
    }

    [Test]
    public async Task UlozeneHladaniaPodlaNazvu()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var id = await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, " Titulky ", "typ:titulky "));
        var znova = await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, "titulky", "typ:titulky -forced"));
        Over.Rovne(id, znova, "rovnaký názov v inej veľkosti písmen je to isté hľadanie");
        await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, "Veľké filmy", "typ:video velkost:>20GB"));
        await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Premenuj(s, id, "České titulky"));

        using (var c = kniznica.OtvorCitanie())
        {
            var zoznam = UlozeneHladania.Zoznam(c);
            Over.Postupnost(["České titulky", "Veľké filmy"], zoznam.Select(u => u.Nazov), "v poradí uloženia");
            Over.Rovne("typ:titulky -forced", zoznam[0].Dotaz, "nový dotaz");
        }

        await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Zmaz(s, id));
        using var citanie = kniznica.OtvorCitanie();
        Over.Rovne(1, UlozeneHladania.Zoznam(citanie).Count, "po zmazaní");
        await Over.VyhodiAsync<ArgumentException>(() => kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, "  ", "x")), "prázdny názov");
    }

    private static VysledkyHladania Najdi(Microsoft.Data.Sqlite.SqliteConnection c, string text, KontextHladania kontext)
    {
        var dotaz = Dotaz.Citaj(text);
        Over.Rovne(0, dotaz.Upozornenia.Count, text + ": " + string.Join(" ", dotaz.Upozornenia));
        return Hladanie.Najdi(c, dotaz, kontext);
    }

    private static IEnumerable<string> Nazvy(Microsoft.Data.Sqlite.SqliteConnection c, string text, KontextHladania kontext) =>
        Najdi(c, text, kontext).Polozky.Select(p => p.Nazov);

    /// <summary>Two invented disks in two catalogues, scanned; returns their ids.</summary>
    private static async Task<(long Disk1, long Disk2)> PripravAsync(Kniznica kniznica, string priecinok)
    {
        var d1 = Path.Combine(priecinok, "d1");
        Subor(d1, "Filmy/Vymyslený Šťastný film (2024)/Vymyslený Šťastný film (2024).mkv", 3000, new DateTime(2024, 3, 15, 12, 0, 0, DateTimeKind.Utc));
        Subor(d1, "Filmy/Vymyslený Šťastný film (2024)/Vymyslený Šťastný film (2024).cs.srt", 20, new DateTime(2024, 3, 15, 12, 0, 0, DateTimeKind.Utc));
        Subor(d1, "Filmy/Vymyslená ukážka (2023)/Vymyslená ukážka sample.mkv", 500, new DateTime(2023, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        Subor(d1, "Seriály/Vymyslený seriál/Season 01/Vymyslený seriál S01E01.mp4", 2500, new DateTime(2025, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        Subor(d1, "Blu-ray/Vymyslený Blu-ray/BDMV/index.bdmv", 10, new DateTime(2022, 2, 2, 12, 0, 0, DateTimeKind.Utc));
        var d2 = Path.Combine(priecinok, "d2");
        Subor(d2, "Zálohy/Vymyslený Šťastný film (2024).mkv", 1000, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        var videoteka = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var zalohy = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslené zálohy"));
        var koren1 = kniznica.NovyIdPriecinka();
        var disk1 = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(videoteka, "Vymyslený disk 1", Identita1, "", null), koren1));
        var koren2 = kniznica.NovyIdPriecinka();
        var disk2 = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(zalohy, "Vymyslený disk 2", Identita2, "", null), koren2));
        foreach (var (id, cesta, identita) in new[] { (disk1, d1, Identita1), (disk2, d2, Identita2) })
        {
            var sken = await SkenovanieDisku.SpustiAsync(kniznica, id, new LokalneUlozisko(cesta, DruhDisku.Usb, new Zvazok(identita)), null, CancellationToken.None);
            Over.Rovne(StavUlohy.Hotova, sken.Stav, "sken " + cesta);
        }

        return (disk1, disk2);
    }

    private static void Subor(string disk, string relativna, int velkost, DateTime zmeneny)
    {
        var cesta = Path.Combine(disk, relativna.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
        File.WriteAllBytes(cesta, new byte[velkost]);
        File.SetLastWriteTimeUtc(cesta, zmeneny);
    }

    private sealed class Zvazok(IdentitaZvazku identita) : IZvazky
    {
        public IReadOnlyList<PripojenyZvazok> Pripojene() => [];

        public IdentitaZvazku? Identita(string cesta) => Directory.Exists(cesta) ? identita : null;

        public (long? Velkost, long? Volne) Miesto(string cesta) => (null, null);
    }
}
