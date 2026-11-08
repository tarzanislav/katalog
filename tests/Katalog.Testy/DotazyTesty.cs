using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;

namespace Katalog.Testy;

/// <summary>The query language: words, phrases, negation, fields with their values, and editing by the filters.</summary>
public sealed class DotazyTesty
{
    private const long GB = 1L << 30;
    private const long MB = 1L << 20;

    [Test]
    public void SlovaFrazyAZapor()
    {
        var dotaz = Dotaz.Citaj("  vymyslený   \"šťastný film\" -ukážka ");
        var slova = dotaz.Podmienky.Cast<Slovo>().ToList();
        Over.Rovne(3, slova.Count, "tri slová");
        Over.Rovne(new Slovo("vymyslený", false, false), slova[0]);
        Over.Rovne(new Slovo("šťastný film", true, false), slova[1]);
        Over.Rovne(new Slovo("ukážka", false, true), slova[2]);
        Over.Rovne(0, dotaz.Upozornenia.Count, "bez upozornení");
        Over.Pravda(!dotaz.JePrazdny, "dotaz nie je prázdny");
        Over.Pravda(dotaz.HladaPriecinky && dotaz.HladaSubory, "slová hľadajú súbory aj priečinky");

        Over.Pravda(Dotaz.Citaj("").JePrazdny, "prázdny text");
        Over.Pravda(Dotaz.Citaj("  - ").JePrazdny, "samotné mínus");
        Over.Pravda(Dotaz.Citaj("zmazane:aj").JePrazdny, "len režim zmazaných nič nehľadá");
    }

    [Test]
    public void PoliaKniznice()
    {
        var dotaz = Dotaz.Citaj("rok:1990..1999 zaner:Komédia,horor videne:nie hodnotenie:>=7,5 moje:9 herec:\"Vymyslený Herec\" rezia:Režisér " +
                                "kolekcia:ano kniznica:serial mam:nie zelane:ano -rok:1995");
        Over.Rovne(0, dotaz.Upozornenia.Count, string.Join(" ", dotaz.Upozornenia));
        Over.Postupnost(
            [
                "Roky { Zaporna = False, Od = 1990, Do = 1999 }", "Zanre komedia,horor", "Videne { Zaporna = False, Hodnota = False }",
                "Hodnotenia { Zaporna = False, Od = 7.5, Do = , Moje = False }", "Hodnotenia { Zaporna = False, Od = 9, Do = , Moje = True }",
                "Osoby Herec Vymyslený Herec", "Osoby Rezia Režisér", "Kolekcie ", "VKniznici { Zaporna = False, Stav = Serial }",
                "Mam { Zaporna = False, Hodnota = False }", "Zelane { Zaporna = False, Hodnota = True }", "Roky { Zaporna = True, Od = 1995, Do = 1995 }",
            ],
            dotaz.Podmienky.Select(p => p switch
            {
                Zanre z => "Zanre " + string.Join(",", z.Nazvy),
                Osoby o => $"Osoby {o.Rola} {string.Join(",", o.Mena)}",
                Kolekcie k => "Kolekcie " + string.Join(",", k.Nazvy),
                _ => p.ToString(),
            }),
            "podmienky");
        Over.Pravda(!dotaz.HladaPriecinky && dotaz.HladaSubory, "polia knižnice hľadajú len súbory");
        Over.Rovne(new Roky(2001, null, false), Dotaz.Citaj("rok:>2000").Podmienky.Single(), "rok:>2000");
        Over.Pravda(Dotaz.Citaj("kolekcia:nie").Podmienky.Single() is Kolekcie { Zaporna: true, Nazvy.Count: 0 }, "kolekcia:nie");
        Over.Rovne(new VKniznici(StavVKniznici.NaPotvrdenie, false), Dotaz.Citaj("kniznica:potvrdit").Podmienky.Single(), "na potvrdenie");
        foreach (var zle in new[] { "rok:minuly", "herec:ab", "hodnotenie:11", "kniznica:mozno" })
        {
            Over.Rovne(1, Dotaz.Citaj(zle).Upozornenia.Count, zle);
        }

        Over.Rovne("vymyslený rok:2001", Dotaz.NastavPole("vymyslený rok:1999", PoleDotazu.Rok, "2001"), "filter roka");
    }

    [Test]
    public void PoliaAIchHodnoty()
    {
        var dotaz = Dotaz.Citaj("Pripona:.MKV,mp4 typ:video,titulky disk:D003,\"Záloha 2\" katalog:Videotéka Pripojené:áno Zmazané:aj -pripona:srt");
        Over.Rovne(0, dotaz.Upozornenia.Count, string.Join(" ", dotaz.Upozornenia));

        var pripony = dotaz.Podmienky.OfType<Pripony>().ToList();
        Over.Postupnost(["mkv", "mp4"], pripony[0].Zoznam, "prípony bez bodky a malými");
        Over.Pravda(!pripony[0].Zaporna && pripony[1].Zaporna, "druhá prípona je záporná");

        var typy = dotaz.Podmienky.OfType<Typy>().Single();
        Over.Postupnost([DruhSuboru.Video, DruhSuboru.Titulky], typy.Druhy, "typy");
        Over.Pravda(!typy.Priecinky && !typy.VsetkySubory, "bez priečinkov");

        var disky = dotaz.Podmienky.OfType<Disky>().Single();
        Over.Postupnost([3], disky.Cisla, "číslo disku aj s D a nulami");
        Over.Postupnost(["Záloha 2"], disky.Nazvy, "názov disku v úvodzovkách");

        Over.Postupnost(["Videotéka"], dotaz.Podmienky.OfType<Katalogy>().Single().Nazvy, "katalóg");
        Over.Rovne(new Pripojene(true), dotaz.Podmienky.OfType<Pripojene>().Single(), "pripojené");
        Over.Rovne(RezimZmazanych.Aj, dotaz.Zmazane, "aj zmazané");
        Over.Pravda(!dotaz.HladaPriecinky && dotaz.HladaSubory, "prípona hľadá len súbory");

        Over.Rovne(new Pripojene(false), Dotaz.Citaj("-pripojene:ano").Podmienky.Single(), "zápor pripojených");
        Over.Rovne(RezimZmazanych.Len, Dotaz.Citaj("zmazane:len").Zmazane, "len zmazané");
        Over.Rovne(RezimZmazanych.Nie, Dotaz.Citaj("vymyslený").Zmazane, "predvolene bez zmazaných");
    }

    [Test]
    public void TypyPriecinkovASuborov()
    {
        var priecinky = Dotaz.Citaj("typ:priecinok sezóna");
        Over.Pravda(priecinky.HladaPriecinky && !priecinky.HladaSubory, "typ:priecinok");
        var subory = Dotaz.Citaj("typ:subor sezóna");
        Over.Pravda(!subory.HladaPriecinky && subory.HladaSubory, "typ:subor");
        var bezPriecinkov = Dotaz.Citaj("-typ:priecinok sezóna");
        Over.Pravda(!bezPriecinkov.HladaPriecinky && bezPriecinkov.HladaSubory, "-typ:priecinok");
        var zmenene = Dotaz.Citaj("zmenene:2024");
        Over.Pravda(zmenene.HladaPriecinky && zmenene.HladaSubory, "dátum zmeny majú aj priečinky");
        Over.Pravda(!Dotaz.Citaj("pridane:7d").HladaPriecinky, "dátum pridania majú len súbory");
        Over.Rovne(1, Dotaz.Citaj("typ:kniha").Upozornenia.Count, "neznámy typ sa ohlási");
    }

    [Test]
    public void Velkosti()
    {
        Over.Rovne(new Velkost(4 * GB + 1, null, false), Velkost("velkost:>4GB"), "viac ako");
        Over.Rovne(new Velkost(4 * GB, null, false), Velkost("velkost:>=4 GB".Replace(" ", "", StringComparison.Ordinal)), "aspoň");
        Over.Rovne(new Velkost(null, 700 * MB, false), Velkost("velkost:<=700MB"), "najviac");
        Over.Rovne(new Velkost(null, 700 * MB - 1, false), Velkost("velkost:<700m"), "menej ako");
        Over.Rovne(new Velkost(3 * GB / 2, 2 * GB, false), Velkost("velkost:1,5GB..2GB"), "rozsah s desatinnou čiarkou");
        Over.Rovne(new Velkost(4 * GB, null, false), Velkost("velkost:4"), "holé číslo sú GB a znamená aspoň");
        Over.Rovne(new Velkost(null, 1L << 40, false), Velkost("velkost:..1TB"), "otvorený rozsah");
        Over.Rovne(new Velkost(512, 512, false), Velkost("velkost:=512B"), "presne");
        Over.Rovne(new Velkost(GB, null, true), Velkost("-velkost:1gb"), "zápor");

        var zle = Dotaz.Citaj("velkost:veľa velkost:4XB velkost:..");
        Over.Rovne(0, zle.Podmienky.Count, "nezrozumiteľné veľkosti sa vynechajú");
        Over.Rovne(3, zle.Upozornenia.Count, "a ohlásia");
        Over.Pravda(zle.Upozornenia[0].Contains("velkost:>4GB", StringComparison.Ordinal), "s príkladom");
    }

    [Test]
    public void Obdobia()
    {
        var teraz = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Local);
        Over.Rovne((Den(2024, 1, 1), Den(2025, 1, 1)), Interval("zmenene:2024", teraz), "rok");
        Over.Rovne((Den(2024, 5, 1), (DateTime?)null), Interval("zmenene:>=2024-05", teraz), "od mesiaca");
        Over.Rovne((Den(2024, 6, 1), (DateTime?)null), Interval("zmenene:>2024-05", teraz), "po mesiaci");
        Over.Rovne(((DateTime?)null, Den(2025, 3, 1)), Interval("zmenene:<1.3.2025", teraz), "pred dňom");
        Over.Rovne(((DateTime?)null, Den(2025, 3, 2)), Interval("zmenene:<=1.3.2025", teraz), "do dňa vrátane");
        Over.Rovne((Den(2023, 1, 1), Den(2025, 1, 1)), Interval("zmenene:2023..2024", teraz), "rozsah rokov");
        Over.Rovne((teraz.AddDays(-7), (DateTime?)null), Interval("pridane:7d", teraz), "posledných 7 dní");
        Over.Rovne(((DateTime?)null, teraz.AddDays(-14)), Interval("pridane:<2t", teraz), "staršie ako 2 týždne");
        Over.Rovne((teraz.AddMonths(-3), (DateTime?)null), Interval("pridane:3m", teraz), "mesiace");
        Over.Rovne((teraz.AddDays(-30), teraz.AddDays(-7)), Interval("pridane:30d..7d", teraz), "relatívny rozsah");
        Over.Rovne((DateTime.Today, DateTime.Today.AddDays(1)), Interval("zmenene:dnes", teraz), "dnes");

        var obdobie = (Obdobie)Dotaz.Citaj("pridane:7d").Podmienky.Single();
        Over.Rovne(PoleDotazu.Pridane, obdobie.Pole, "pole pridania");
        Over.Rovne(1, Dotaz.Citaj("zmenene:včera-ráno").Upozornenia.Count, "nezrozumiteľný dátum");
    }

    [Test]
    public void NeznamePoleSaHladaAkoText()
    {
        var dotaz = Dotaz.Citaj("re:zero stitok:vymyslený C:\\Filmy");
        Over.Postupnost(["re:zero", "stitok:vymyslený", "C:\\Filmy"], dotaz.Podmienky.Cast<Slovo>().Select(s => s.Text), "ako text");
        Over.Rovne(2, dotaz.Upozornenia.Count, "neznáme polia sa ohlásia, cesta nie");
    }

    [Test]
    public void FiltreUpravujuText()
    {
        Over.Rovne("vymyslený -ukážka typ:titulky", Dotaz.NastavPole("vymyslený typ:video -ukážka", PoleDotazu.Typ, "titulky"), "výmena typu");
        Over.Rovne("vymyslený", Dotaz.NastavPole("vymyslený velkost:>4GB -velkost:>40GB", PoleDotazu.Velkost, null), "odstránenie aj záporného");
        Over.Rovne("\"šťastný film\" disk:\"Záloha 2\"", Dotaz.NastavPole("\"šťastný film\"", PoleDotazu.Disk, "Záloha 2"), "hodnota s medzerou v úvodzovkách");
        Over.Rovne("pripojene:ano", Dotaz.NastavPole("", PoleDotazu.Pripojene, "ano"), "do prázdneho");
        Over.Rovne("vymyslený zmenene:2024", Dotaz.NastavPole("vymyslený Zmenené:2023", PoleDotazu.Zmenene, "2024"), "pole s diakritikou");

        Over.Rovne(">4GB", Dotaz.HodnotaPola("vymyslený velkost:>4GB", PoleDotazu.Velkost), "hodnota pola");
        Over.Rovne("Záloha 2", Dotaz.HodnotaPola("disk:\"Záloha 2\"", PoleDotazu.Disk), "hodnota v úvodzovkách");
        Over.Rovne<string?>(null, Dotaz.HodnotaPola("-typ:video", PoleDotazu.Typ), "záporné pole nie je hodnota filtra");
    }

    [Test]
    public void DruhySuborovPodlaPripony()
    {
        Over.Rovne(DruhSuboru.Video, DruhySuborov.Urci("MKV"), "mkv");
        Over.Rovne(DruhSuboru.Titulky, DruhySuborov.Urci("srt"), "srt");
        Over.Rovne<DruhSuboru?>(null, DruhySuborov.Urci("katalog"), "neznáma prípona");
        var vsetky = Enum.GetValues<DruhSuboru>().SelectMany(DruhySuborov.Pripony).ToList();
        Over.Rovne(vsetky.Count, vsetky.Distinct().Count(), "každá prípona patrí jednému druhu");
    }

    private static Velkost? Velkost(string text) => Dotaz.Citaj(text).Podmienky.OfType<Velkost>().SingleOrDefault();

    private static DateTime Den(int rok, int mesiac, int den) => new(rok, mesiac, den, 0, 0, 0, DateTimeKind.Local);

    private static (DateTime? Od, DateTime? Do) Interval(string text, DateTime teraz)
    {
        var dotaz = Dotaz.Citaj(text);
        Over.Rovne(0, dotaz.Upozornenia.Count, text + ": " + string.Join(" ", dotaz.Upozornenia));
        var obdobie = dotaz.Podmienky.OfType<Obdobie>().Single();
        return (obdobie.Od?.Urci(teraz), obdobie.Do?.Urci(teraz));
    }
}
