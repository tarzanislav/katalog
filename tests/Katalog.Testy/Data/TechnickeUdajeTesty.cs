using Katalog.Data;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Media;
using Katalog.Skenovanie;
using Katalog.Ulohy;

namespace Katalog.Testy;

/// <summary>Reading technical data of a scanned disk into the library, searching by them, and the persistent job queue.</summary>
public sealed class TechnickeUdajeTesty
{
    private static readonly IdentitaZvazku Identita = new(DruhDisku.Usb, "3333-3333", "{vymysleny-3}", null, "Videá", "NTFS", 1_000_000, null);

    [Test]
    public async Task ZapisACitanieBezFfmpeg()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var (diskId, _) = await PripravAsync(kniznica, priecinok.Cesta, videom: false);

        IReadOnlyList<VideoNaCitanie> naCitanie;
        using (var c = kniznica.OtvorCitanie())
        {
            naCitanie = TechnickeUdaje.NaCitanie(c, diskId, 10);
            Over.Postupnost(["Vymyslený film (2024).mkv"], naCitanie.Select(v => v.Nazov), "len video čaká na údaje");
            Over.Rovne((1, 1), TechnickeUdaje.Pocty(c, diskId), "jedno video, jedno čaká");
        }

        var (udaje, stopy) = CitanieFfprobe.Citaj(MediaTesty.Film4k);
        var obrazky = UlozisteObrazkov.PreKniznicu(kniznica.Cesta);
        var obrazok = obrazky.Uloz([0xFF, 0xD8, 0xFF, 0xD9], DruhObrazka.Nahlad, 320, 133);
        var video = naCitanie[0];
        var zapisane = await kniznica.Zapis.ZapisAsync(s =>
            TechnickeUdaje.Zapis(s, video, udaje, stopy, [(UlozisteObrazkov.Zaregistruj(s, obrazok, 1), 2_465_041)], 1));
        Over.Pravda(zapisane, "zapísané");
        Over.Pravda(File.Exists(obrazky.Cesta(obrazok.Meno)) && Path.GetFileName(obrazky.Priecinok) == "Kniznica.obrazky", "obrázok vedľa knižnice");

        using (var c = kniznica.OtvorCitanie())
        {
            var precitane = TechnickeUdaje.Citaj(c, video.SuborId)!;
            Over.Rovne(udaje, precitane.Udaje, "údaje sa vrátia");
            Over.Rovne(stopy.Count, precitane.Stopy.Count, "stopy sa vrátia");
            Over.Rovne(stopy[0], precitane.Stopy[0], "stopa sa vráti celá");
            Over.Rovne(new NahladVidea(obrazok.Meno, 2_465_041, 320, 133), precitane.Nahlady.Single(), "náhľad");
            Over.Rovne((1, 1), TechnickeUdaje.Pocty(c, diskId), "čaká ešte odtlačok");
            Over.Pravda(TechnickeUdaje.NaCitanie(c, diskId, 10).Single().MaUdaje, "len odtlačok");
        }

        Over.Pravda(await kniznica.Zapis.ZapisAsync(s => TechnickeUdaje.ZapisOdtlacok(s, video, new byte[16])), "odtlačok zapísaný");
        Over.Pravda(!await kniznica.Zapis.ZapisAsync(s => TechnickeUdaje.ZapisOdtlacok(s, video with { Velkost = 1 }, new byte[16])), "iný súbor sa nezapíše");
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne((1, 0), TechnickeUdaje.Pocty(c, diskId), "nič nečaká");

            var nikto = new KontextHladania([], DateTime.Now);
            foreach (var text in new[] { "kvalita:4k", "kodek:h265", "zvuk:cs titulky:sk", "hdr:dv", "dlzka:>2h", "poskodene:nie", "-zvuk:de typ:video" })
            {
                Over.Rovne(1, Hladanie.Najdi(c, Dotaz.Citaj(text), nikto).Polozky.Count, text);
            }

            foreach (var text in new[] { "kvalita:fhd", "zvuk:de", "titulky:nie", "hdr:nie", "dlzka:<1h", "poskodene:ano" })
            {
                Over.Rovne(0, Hladanie.Najdi(c, Dotaz.Citaj(text), nikto).Polozky.Count, text);
            }
        }

        // a file that changed since it was read is not written over with old data
        var zmeneny = video with { Velkost = video.Velkost + 1 };
        Over.Pravda(!await kniznica.Zapis.ZapisAsync(s => TechnickeUdaje.Zapis(s, zmeneny, udaje, [], [], 2)), "zmenený súbor sa nezapíše");
    }

    [Test]
    public async Task TrvalaFrontaUloh()
    {
        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var (diskId, _) = await PripravAsync(kniznica, priecinok.Cesta, videom: false);
        var pruh = Pruhy.Disk(diskId);

        var id = await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Naplanuj(s, DruhUlohy.Ffprobe, pruh, diskId, 10));
        var znova = await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Naplanuj(s, DruhUlohy.Ffprobe, pruh, diskId, 11));
        Over.Rovne(id, znova, "tá istá úloha sa nenaplánuje dvakrát");
        await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Zacni(s, id, 12));
        await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Priebeh(s, id, 0.4));

        // the program ended in the middle: next time the job waits again
        Over.Rovne(1, await kniznica.Zapis.ZapisAsync(UlohyKniznice.ObnovPrerusene), "prerušená úloha");
        using (var c = kniznica.OtvorCitanie())
        {
            var uloha = UlohyKniznice.Cakajuce(c).Single();
            Over.Pravda(uloha is { Druh: DruhUlohy.Ffprobe, Stav: StavUlohy.Caka, Pokusy: 1, Priebeh: 0.4 } && uloha.DiskId == diskId, "čaká znova");
        }

        await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Skonci(s, id, StavUlohy.Hotova, null, 20));
        using (var c = kniznica.OtvorCitanie())
        {
            Over.Rovne(0, UlohyKniznice.Cakajuce(c).Count, "hotová nečaká");
        }

        Over.Rovne(1, await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Vycisti(s, 21)), "staré hotové úlohy sa vyčistia");
    }

    /// <summary>The whole reading with the real ffprobe and ffmpeg (GitHub Actions installs them on Linux).</summary>
    [Test]
    public async Task CitanieDiskuSoSkutocnymFfmpeg()
    {
        var nastroje = HladanieFfmpeg.Najdi(Environment.GetEnvironmentVariable("KATALOG_FFMPEG_DIR"), AppContext.BaseDirectory);
        if (nastroje?.Ffmpeg is null)
        {
            Console.WriteLine("ffprobe a ffmpeg tu nie sú – test vynechaný");
            return;
        }

        using var priecinok = new DocasnyPriecinok();
        await using var kniznica = await Kniznica.OtvorAsync(priecinok.Subor("Kniznica.katalog"));
        var (diskId, disk) = await PripravAsync(kniznica, priecinok.Cesta, videom: true, nastroje.Ffmpeg);
        var obrazky = UlozisteObrazkov.PreKniznicu(kniznica.Cesta);
        var priebehy = new List<PriebehCitania>();
        var vysledok = await CitanieMedii.SpustiAsync(kniznica, diskId, disk, new Ffmpeg(nastroje), obrazky,
            new SynchronnyPriebeh<PriebehCitania>(priebehy.Add), CancellationToken.None);
        Over.Rovne(new VysledokCitania(StavUlohy.Hotova, 2, 1, null), vysledok, "prečítané dve videá, jedno pokazené");
        Over.Rovne(new PriebehCitania(2, 2), priebehy[^1], "priebeh na konci");

        using (var c = kniznica.OtvorCitanie())
        {
            var nikto = new KontextHladania([], DateTime.Now);
            var film = Hladanie.Najdi(c, Dotaz.Citaj("kvalita:sd zvuk:sk titulky:cs"), nikto).Polozky.Single();
            var udaje = TechnickeUdaje.Citaj(c, film.Id)!;
            Over.Pravda(udaje.Udaje is { Sirka: 320, Vyska: 180, VideoKodek: "mjpeg", Chyby: null }, "údaje filmu");
            Over.Pravda(udaje.Stopy.Any(s => s is { Druh: DruhStopy.Titulky, Jazyk: "cs", Vynutena: true, ExternySubor: not null }), "externé vynútené titulky");
            Over.Pravda(udaje.Nahlady.Single() is { Sirka: 320, Vyska: 180 } n && File.Exists(obrazky.Cesta(n.Meno)), "náhľad");
            Over.Rovne(1, Hladanie.Najdi(c, Dotaz.Citaj("poskodene:ano"), nikto).Polozky.Count, "pokazený súbor sa dá nájsť");
        }

        var znova = await CitanieMedii.SpustiAsync(kniznica, diskId, disk, new Ffmpeg(nastroje), obrazky, null, CancellationToken.None);
        Over.Rovne(0, znova.Precitane, "druhý raz nie je čo čítať");

        using var zrusenie = new CancellationTokenSource();
        await zrusenie.CancelAsync();
        File.Copy(Path.Combine(disk, "Filmy", "Vymyslený film (2024).mkv"), Path.Combine(disk, "Filmy", "Vymyslená kópia.mkv"));
        await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new Zvazok(Identita)), null, CancellationToken.None);
        var zrusene = await CitanieMedii.SpustiAsync(kniznica, diskId, disk, new Ffmpeg(nastroje), obrazky, null, zrusenie.Token);
        Over.Rovne(StavUlohy.Zrusena, zrusene.Stav, "zrušené čítanie");
        Directory.Move(disk, disk + "-odpojeny");
        var odpojeny = await CitanieMedii.SpustiAsync(kniznica, diskId, disk, new Ffmpeg(nastroje), obrazky, null, CancellationToken.None);
        Over.Rovne(StavUlohy.CakaNaDisk, odpojeny.Stav, "odpojený disk počká");
    }

    private static async Task<(long DiskId, string Disk)> PripravAsync(Kniznica kniznica, string priecinok, bool videom, string? ffmpeg = null)
    {
        var disk = Path.Combine(priecinok, "disk");
        var filmy = Path.Combine(disk, "Filmy");
        Directory.CreateDirectory(filmy);
        var video = Path.Combine(filmy, "Vymyslený film (2024).mkv");
        if (videom)
        {
            await VymysleneVideo.VytvorAsync(ffmpeg!, video);
            await File.WriteAllBytesAsync(Path.Combine(filmy, "Vymyslený pokazený.mp4"), new byte[2048]);
        }
        else
        {
            await File.WriteAllBytesAsync(video, new byte[100]);
        }

        await File.WriteAllTextAsync(Path.Combine(filmy, "Vymyslený film (2024).cze.forced.srt"), "1\n00:00:00,000 --> 00:00:01,000\nVymyslený text\n");
        await File.WriteAllTextAsync(Path.Combine(filmy, "Poznámka.txt"), "vymyslené");

        var katalog = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, "Vymyslená videotéka"));
        var koren = kniznica.NovyIdPriecinka();
        var diskId = await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, "Vymyslený disk", Identita, "", null), koren));
        var sken = await SkenovanieDisku.SpustiAsync(kniznica, diskId, new LokalneUlozisko(disk, DruhDisku.Usb, new Zvazok(Identita)), null, CancellationToken.None);
        Over.Rovne(StavUlohy.Hotova, sken.Stav, "sken");
        return (diskId, disk);
    }

    /// <summary>Progress reported on the calling thread (Progress&lt;T&gt; would post it later).</summary>
    private sealed class SynchronnyPriebeh<T>(Action<T> akcia) : IProgress<T>
    {
        public void Report(T value) => akcia(value);
    }

    private sealed class Zvazok(IdentitaZvazku identita) : IZvazky
    {
        public IReadOnlyList<PripojenyZvazok> Pripojene() => [];

        public IdentitaZvazku? Identita(string cesta) => Directory.Exists(cesta) ? identita : null;

        public (long? Velkost, long? Volne) Miesto(string cesta) => (null, null);
    }
}
