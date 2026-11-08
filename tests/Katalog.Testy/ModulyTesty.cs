using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Media;
using Katalog.Skenovanie;
using Katalog.Ulohy;

namespace Katalog.Testy;

public sealed class ModulyTesty
{
    private static readonly IdentitaZvazku UsbDisk = new(DruhDisku.Usb, "1A2B-3C4D", "{0aaa}", "WD-XYZ123", "Záloha", "NTFS", 4_000_787_030_016, null);

    [Test]
    public void TenIstyDiskJeIstyAjSInymPismenom()
    {
        // the drive letter is not part of the identity at all; a new label alone changes nothing
        var najdeny = UsbDisk with { Menovka = "Filmy" };
        Over.Rovne(VerdiktZhody.Isto, ZhodaZvazku.Porovnaj(UsbDisk, najdeny).Verdikt);
    }

    [Test]
    public void PreformatovanyDiskJeIny()
    {
        var najdeny = UsbDisk with { SerioveCisloZvazku = "9999-0000", GuidZvazku = "{0bbb}" };
        Over.Rovne(VerdiktZhody.Iny, ZhodaZvazku.Porovnaj(UsbDisk, najdeny).Verdikt);
    }

    [Test]
    public void LenRovnakaMenovkaNestaci()
    {
        var najdeny = new IdentitaZvazku(DruhDisku.Usb, null, null, null, "Záloha", null, null, null);
        Over.Rovne(VerdiktZhody.Iny, ZhodaZvazku.Porovnaj(UsbDisk, najdeny).Verdikt);
    }

    [Test]
    public void LenSerioveCisloZvazkuSaOpyta()
    {
        var ulozeny = new IdentitaZvazku(DruhDisku.Usb, "1A2B-3C4D", null, null, null, null, null, null);
        Over.Rovne(VerdiktZhody.Opytat, ZhodaZvazku.Porovnaj(ulozeny, ulozeny).Verdikt);
    }

    [Test]
    public void SietoveZdielaniePodlaAdresy()
    {
        var nas = new IdentitaZvazku(DruhDisku.Sietovy, null, null, null, null, null, null, @"\\nas\filmy");
        Over.Rovne(VerdiktZhody.Isto, ZhodaZvazku.Porovnaj(nas, nas with { Adresa = @"\\NAS\Filmy" }).Verdikt);
        Over.Rovne(VerdiktZhody.Iny, ZhodaZvazku.Porovnaj(nas, nas with { Adresa = @"\\nas\hudba" }).Verdikt);
    }

    [Test]
    public void DvaRovnakoDobreDiskySaOpytaju()
    {
        var disky = new[] { ("A", UsbDisk), ("B", UsbDisk with { Menovka = "Iná" }) };
        var najdeny = UsbDisk with { Menovka = null };
        var vysledok = ZhodaZvazku.Najlepsia(disky, d => d.Item2, najdeny);
        Over.Rovne(VerdiktZhody.Opytat, vysledok!.Value.Verdikt);
        Over.Pravda(ZhodaZvazku.Najlepsia(Array.Empty<(string, IdentitaZvazku)>(), d => d.Item2, najdeny) is null, "žiadny disk");
    }

    [Test]
    public void TypySuborovPodlaPripony()
    {
        Over.Rovne("mkv", TypySuborov.Pripona("Vymyslený film (2001).MKV"));
        Over.Rovne("", TypySuborov.Pripona(".skryty"));
        Over.Rovne("", TypySuborov.Pripona("bez_pripony"));
        Over.Pravda(TypySuborov.JeVideo("m2ts") && !TypySuborov.JeVideo("txt"), "video");
        Over.Pravda(TypySuborov.SuTitulky("srt"), "titulky");
        Over.Pravda(TypySuborov.JeStrukturaDisku("video_ts") && TypySuborov.JeStrukturaDisku("BDMV"), "štruktúry DVD a BD");
    }

    [Test]
    public void PruhyUloh()
    {
        Over.Rovne("disk:12", Pruhy.Disk(12));
        Over.Rovne("net:tmdb", Pruhy.Siet("TMDB"));
    }

    [Test]
    public void ZamknutePoleZdrojNeprepise()
    {
        var poradie = PoradieZdrojov.Predvolene;
        Over.Pravda(PoradieZdrojov.MozePrepisat(ZdrojUdajov.Histra, false, ZdrojUdajov.Tmdb, poradie), "TMDB je pred HISTRA");
        Over.Pravda(!PoradieZdrojov.MozePrepisat(ZdrojUdajov.Tmdb, false, ZdrojUdajov.Histra, poradie), "HISTRA neprepíše TMDB");
        Over.Pravda(!PoradieZdrojov.MozePrepisat(ZdrojUdajov.Histra, true, ZdrojUdajov.Tmdb, poradie), "zamknuté pole");
        Over.Pravda(!PoradieZdrojov.MozePrepisat(ZdrojUdajov.Pouzivatel, false, ZdrojUdajov.NazovSuboru, poradie), "údaj používateľa");
    }
}
