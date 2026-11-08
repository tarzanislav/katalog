using System.Globalization;
using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;
using Katalog.Ulohy;
using Microsoft.Data.Sqlite;

namespace Katalog.Testy;

/// <summary>Scanning of invented folders into a library: first scan, difference update, deletion and return, cancellation.</summary>
public sealed class SkenovanieTesty
{
    private static readonly IdentitaZvazku Identita = new(DruhDisku.Usb, "1A2B-3C4D", "{vymysleny}", "VYMYSLENY-1", "Záloha", "NTFS", 1_000_000, null);

    [Test]
    public async Task PrvySkenARozdielovaAktualizacia()
    {
        using var priecinok = new DocasnyPriecinok();
        var disk = Path.Combine(priecinok.Cesta, "disk");
        Subor(disk, "Filmy/Vymyslený film A (2001).mkv", 10);
        Subor(disk, "Filmy/Vymyslený film B (2002).mkv", 20);
        Subor(disk, "Seriály/Season 01/Vymyslený seriál S01E01.mkv", 30);
        Directory.CreateDirectory(Path.Combine(disk, "Prázdny"));

        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PridajDiskAsync(kniznica);
        var ulozisko = new LokalneUlozisko(disk, DruhDisku.Usb, new VymyslenyZvazok());

        var prvy = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        Over.Rovne(StavUlohy.Hotova, prvy.Stav, "prvý sken");
        Over.Rovne(new PocetZmien(3, 0, 0), prvy.Zmeny, "prvý sken");
        using (var c = kniznica.OtvorCitanie())
        {
            var koren = KatalogyADisky.Koren(c, diskId)!.Value;
            Over.Postupnost(["Filmy", "Prázdny", "Seriály"], KatalogyADisky.Podpriecinky(c, koren).Select(p => p.Nazov), "priečinky v koreni");
            var serialy = KatalogyADisky.Podpriecinky(c, koren).Single(p => p.Nazov == "Seriály");
            Over.Pravda(serialy.MaPodpriecinky, "Seriály majú podpriečinok");
            var sezona = KatalogyADisky.Podpriecinky(c, serialy.Id).Single();
            Over.Rovne("Seriály/Season 01", KatalogyADisky.Cesta(c, sezona.Id), "cesta priečinka");
            Over.Rovne(new Suhrn(3, 4, 60), KatalogyADisky.SuhrnDisku(c, diskId), "súhrn disku");
            Over.Pravda(KatalogyADisky.NajdiDisk(c, diskId)!.PoslednySken is not null, "čas posledného skenu");
            Over.Rovne(1, Fulltext.Najdi(c, "seriál s01e01").Count(v => v.Druh == DruhHladania.Subor), "súbor v hľadaní");
        }

        // nothing changed: nothing is written
        var bezZmeny = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        Over.Rovne(new PocetZmien(0, 0, 0), bezZmeny.Zmeny, "sken bez zmien");

        long idEpizody;
        using (var c = kniznica.OtvorCitanie())
        {
            idEpizody = IdSuboru(c, "Vymyslený seriál S01E01.mkv");
        }

        // a file grows, one is deleted, a whole folder goes away, new ones come
        Subor(disk, "Filmy/Vymyslený film B (2002).mkv", 25);
        File.Delete(Path.Combine(disk, "Filmy", "Vymyslený film A (2001).mkv"));
        var serialyCesta = Path.Combine(disk, "Seriály");
        var zaloha = Path.Combine(priecinok.Cesta, "odlozene");
        Directory.Move(serialyCesta, zaloha);
        Subor(disk, "Filmy/Vymyslený film C (2003).mkv", 5);
        Subor(disk, "Nové/Vymyslený film C (2003).cs.srt", 1);

        var druhy = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        Over.Rovne(new PocetZmien(2, 1, 2), druhy.Zmeny, "druhý sken (2 nové, 1 zmenený, 2 zmazané)");
        using (var c = kniznica.OtvorCitanie())
        {
            var koren = KatalogyADisky.Koren(c, diskId)!.Value;
            Over.Postupnost(["Filmy", "Nové", "Prázdny"], KatalogyADisky.Podpriecinky(c, koren).Select(p => p.Nazov), "zmazaný priečinok sa nezobrazuje");
            Over.Pravda(KatalogyADisky.Podpriecinky(c, koren, ajZmazane: true).Any(p => p.Nazov == "Seriály" && p.Zmazany), "zmazaný priečinok ostáva v histórii");
            Over.Rovne(new Suhrn(3, 3, 31), KatalogyADisky.SuhrnDisku(c, diskId), "súhrn po zmenách");
        }

        // the folder comes back unchanged: the same records come back (their ids, so their history, stay)
        Directory.Move(zaloha, serialyCesta);
        var treti = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
        Over.Rovne(new PocetZmien(1, 0, 0), treti.Zmeny, "tretí sken (vrátený súbor)");
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne(idEpizody, IdSuboru(c, "Vymyslený seriál S01E01.mkv"), "vrátený súbor má to isté id");
            Over.Rovne(new Suhrn(4, 5, 61), KatalogyADisky.SuhrnDisku(c, diskId), "súhrn po vrátení");
            Over.Rovne(4L, Cislo(c, string.Create(CultureInfo.InvariantCulture, $"SELECT count(*) FROM sken WHERE disk_id = {diskId} AND stav = {(int)StavUlohy.Hotova}")), "záznamy skenov");
        }
    }

    [Test]
    public async Task ZrusenySkenNicNepokazi()
    {
        using var priecinok = new DocasnyPriecinok();
        var disk = Path.Combine(priecinok.Cesta, "disk");
        for (var i = 0; i < 50; i++)
        {
            Subor(disk, string.Create(CultureInfo.InvariantCulture, $"P{i}/Vymyslený {i}.mkv"), i + 1);
        }

        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PridajDiskAsync(kniznica);
        using var zrusenie = new CancellationTokenSource();
        await zrusenie.CancelAsync();
        var vysledok = await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new VymyslenyZvazok()), null, zrusenie.Token);
        Over.Rovne(StavUlohy.Zrusena, vysledok.Stav, "zrušený sken");
        using var c = kniznica.OtvorCitanie();
        Over.Pravda(KatalogyADisky.NajdiDisk(c, diskId)!.PoslednySken is null, "zrušený sken nie je posledným skenom");
        Over.Pravda(KontrolaVazieb(c), "väzby ostali v poriadku");

        // the next scan simply goes on
        var dalsi = await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new VymyslenyZvazok()), null, CancellationToken.None);
        Over.Rovne(StavUlohy.Hotova, dalsi.Stav);
        Over.Rovne(50L, KatalogyADisky.SuhrnDisku(c, diskId).Subory, "po ďalšom skene");
    }

    [Test]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task NeprecitatelnyPriecinokOstaneAkoBol()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            Over.Preskoc("prístupové práva sa dajú odobrať len na Linuxe a nie správcovi");
        }

        using var priecinok = new DocasnyPriecinok();
        var disk = Path.Combine(priecinok.Cesta, "disk");
        Subor(disk, "Tajné/Vymyslený.mkv", 3);
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var diskId = await PridajDiskAsync(kniznica);
        var ulozisko = new LokalneUlozisko(disk, DruhDisku.Usb, new VymyslenyZvazok());
        await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);

        var tajne = Path.Combine(disk, "Tajné");
        File.SetUnixFileMode(tajne, UnixFileMode.None);
        try
        {
            var vysledok = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, null, CancellationToken.None);
            Over.Rovne(1, vysledok.Chyby, "jedna chyba");
            Over.Rovne(new PocetZmien(0, 0, 0), vysledok.Zmeny, "obsah nečitateľného priečinka sa neoznačí ako zmazaný");
        }
        finally
        {
            File.SetUnixFileMode(tajne, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    public async Task KatalogyADiskySaDajuUpravovat()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var videoteka = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, " Videotéka "));
        var zalohy = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Zálohy"));
        var idKorena = kniznica.NovyIdPriecinka();
        var disk = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(videoteka, "Vymyslený disk", Identita, "", 500), idKorena));
        var idKorena2 = kniznica.NovyIdPriecinka();
        var disk2 = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(videoteka, "Priečinok", Identita, "Filmy/Nové", 500), idKorena2));
        await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.UpravDisk(s, disk, "Premenovaný", zalohy, 7, " polica 2 ", null));
        await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PremenujKatalog(s, zalohy, "Archív"));

        using var c = kniznica.OtvorCitanie();
        Over.Postupnost(["Videotéka", "Archív"], KatalogyADisky.Katalogy(c).Select(k => k.Nazov), "katalógy v poradí vytvorenia");
        var d = KatalogyADisky.NajdiDisk(c, disk)!;
        Over.Rovne<(string, long, int, string?, string)>(("Premenovaný", zalohy, 7, "polica 2", "007"), (d.Nazov, d.KatalogId, d.Cislo, d.Umiestnenie, d.CisloText), "upravený disk");
        Over.Rovne(Identita, d.Identita, "identita zväzku");
        Over.Rovne(2, KatalogyADisky.NajdiDisk(c, disk2)!.Cislo, "druhý disk dostal číslo 2");
        Over.Rovne(8, KatalogyADisky.DalsieCislo(c), "ďalšie číslo je nad najvyšším");
        Over.Rovne("Filmy/Nové", KatalogyADisky.NajdiDisk(c, disk2)!.Koren, "koreň priečinkového disku");
        Over.Rovne(idKorena, KatalogyADisky.Koren(c, disk), "koreňový priečinok disku");

        await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.ZmazKatalog(s, videoteka));
        Over.Rovne(1, KatalogyADisky.Disky(c).Count, "disky zmazaného katalógu odišli s ním");
    }

    [Test]
    public async Task KniznicaVerzie1SaPrevedieSoZalohou()
    {
        using var priecinok = new DocasnyPriecinok();
        var cesta = priecinok.Subor("Kniznica.katalog");
        using (var spojenie = new SqliteConnection("Data Source=" + cesta + ";Pooling=False;Foreign Keys=True"))
        {
            spojenie.Open();
            Migracie.Vykonaj(spojenie, 0, 1);
            KniznicaTesty.Vykonaj(spojenie, """
                INSERT INTO katalog(id, nazov, vytvoreny) VALUES (1, 'Vymyslený', 0);
                INSERT INTO disk(id, katalog_id, cislo, nazov, pridany) VALUES (1, 1, 1, 'Vymyslený disk', 0);
                """);
        }

        await using var kniznica = await Kniznica.OtvorAsync(cesta);
        Over.Rovne(Migracie.Posledna, kniznica.VerziaSchemy, "prevedená na poslednú verziu");
        Over.Pravda(kniznica.ZalohaPredMigraciou is { } z && File.Exists(z) && z.Contains("-v1-", StringComparison.Ordinal), "záloha verzie 1");
        using var c = kniznica.OtvorCitanie();
        Over.Rovne("", KatalogyADisky.NajdiDisk(c, 1)!.Koren, "starý disk je celý zväzok");
    }

    [Test]
    public void PriecinkovyDiskJePripojenyLenAkPriecinokExistuje()
    {
        var celyZvazok = NovyDiskZaznam(1, "");
        var priecinkovy = NovyDiskZaznam(2, "Filmy");
        var zvazok = new PripojenyZvazok("E:" + Path.DirectorySeparatorChar, Identita with { Menovka = "Iná menovka" }, 100);

        var najdene = RozpoznanieDiskov.Najdi([celyZvazok, priecinkovy], [zvazok], _ => true);
        Over.Postupnost([1L, 2L], najdene.Select(n => n.Disk.Id), "oba disky na zväzku");
        Over.Pravda(najdene.All(n => n.Verdikt == VerdiktZhody.Isto), "istá zhoda podľa zväzku");
        Over.Pravda(najdene.Single(n => n.Disk.Id == 2).Cesta.EndsWith("Filmy", StringComparison.Ordinal), "cesta priečinkového disku");

        Over.Postupnost([1L], RozpoznanieDiskov.Najdi([celyZvazok, priecinkovy], [zvazok], _ => false).Select(n => n.Disk.Id), "chýbajúci priečinok");
        Over.Rovne("Filmy/Nové", RozpoznanieDiskov.RelativnyKoren(Path.Combine(Path.GetTempPath(), "x"), Path.Combine(Path.GetTempPath(), "x", "Filmy", "Nové")));
    }

    private static Disk NovyDiskZaznam(long id, string koren) => new(id, 1, (int)id, "Vymyslený", Identita, koren, null, null, null, 0, null, null);

    private static async Task<long> PridajDiskAsync(Kniznica kniznica)
    {
        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslený katalóg"));
        var koren = kniznica.NovyIdPriecinka();
        return await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, "Vymyslený disk", Identita, "", null), koren));
    }

    private static void Subor(string disk, string relativna, int velkost)
    {
        var cesta = Path.Combine(disk, relativna.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
        File.WriteAllBytes(cesta, new byte[velkost]);
        // a fixed time, so "unchanged" is exact and a rewrite with another size is a change
        File.SetLastWriteTimeUtc(cesta, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddSeconds(velkost));
    }

    private static long IdSuboru(SqliteConnection c, string nazov)
    {
        using var prikaz = c.CreateCommand();
        prikaz.CommandText = "SELECT id FROM subor WHERE nazov = $n";
        prikaz.Parameters.AddWithValue("$n", nazov);
        return Convert.ToInt64(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long Cislo(SqliteConnection c, string sql) => KniznicaTesty.Cislo(c, sql);

    private static bool KontrolaVazieb(SqliteConnection c)
    {
        using var prikaz = c.CreateCommand();
        prikaz.CommandText = "PRAGMA foreign_key_check";
        using var citac = prikaz.ExecuteReader();
        return !citac.Read();
    }

    private sealed class VymyslenyZvazok : IZvazky
    {
        public IReadOnlyList<PripojenyZvazok> Pripojene() => [];

        public IdentitaZvazku? Identita(string cesta) => Directory.Exists(cesta) ? SkenovanieTesty.Identita : null;

        public (long? Velkost, long? Volne) Miesto(string cesta) => (null, null);
    }
}
