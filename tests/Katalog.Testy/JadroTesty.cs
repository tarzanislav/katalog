using System.Security.Cryptography;
using System.Text;
using Katalog.Jadro;
using Katalog.Jadro.Aktualizacie;
using Katalog.Jadro.Model;

namespace Katalog.Testy;

public sealed class JadroTesty
{
    [Test]
    public void KvalitaPodlaRozlisenia()
    {
        Over.Rovne(TriedaKvality.UHD4K, Kvalita.Z(3840, 2160));
        Over.Rovne(TriedaKvality.UHD4K, Kvalita.Z(3840, 1600), "orezaný širokouhlý 4K");
        Over.Rovne(TriedaKvality.FHD, Kvalita.Z(1920, 1080));
        Over.Rovne(TriedaKvality.FHD, Kvalita.Z(1920, 800), "orezaný širokouhlý FHD");
        Over.Rovne(TriedaKvality.HD, Kvalita.Z(1280, 720));
        Over.Rovne(TriedaKvality.SD, Kvalita.Z(720, 576));
        Over.Rovne(TriedaKvality.Nezistena, Kvalita.Z(0, 0));
        Over.Rovne("4K", Kvalita.Skratka(TriedaKvality.UHD4K));
    }

    [Test]
    public void KlucHladaniaSaDaRozlozit()
    {
        foreach (var druh in Enum.GetValues<DruhHladania>())
        {
            var kluc = KlucHladania.Z(druh, 123_456_789);
            Over.Rovne((druh, 123_456_789L), KlucHladania.Rozloz(kluc), druh.ToString());
        }

        Over.Pravda(Enum.GetValues<DruhHladania>().All(d => (int)d is > 0 and < KlucHladania.Nasobok), "druhy sa zmestia do násobku");
    }

    [Test]
    public void NazovProgramuJeNaJednomMieste()
    {
        Over.Rovne("KATALOG", Produkt.Nazov);
        Over.Rovne("KATALOG_Setup_1.2.3.exe", PodpisVydania.NazovInstalatora(new Version(1, 2, 3)));
        Over.Rovne("# KATALOG 1.2.3", PodpisVydania.RiadokVerzie(new Version(1, 2, 3)));
    }

    [Test]
    public void PodpisanyZoznamSuctovDaSucetInstalatora()
    {
        using var kluc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verejny = Convert.ToBase64String(kluc.ExportSubjectPublicKeyInfo());
        var verzia = new Version(0, 1, 0);
        var sucet = new string('A', 64);
        var zoznam = Encoding.UTF8.GetBytes($"# KATALOG 0.1.0\n{sucet}  KATALOG_Setup_0.1.0.exe\n");
        var podpis = kluc.SignData(zoznam, HashAlgorithmName.SHA256);

        Over.Rovne(sucet, PodpisVydania.OcakavanySucetInstalatora(zoznam, podpis, verzia, verejny));
        Over.Rovne<string?>(null, PodpisVydania.OcakavanySucetInstalatora(zoznam, podpis, new Version(0, 2, 0), verejny), "iná verzia");

        var zmeneny = (byte[])zoznam.Clone();
        zmeneny[20] ^= 1;
        Over.Rovne<string?>(null, PodpisVydania.OcakavanySucetInstalatora(zmeneny, podpis, verzia, verejny), "zmenený zoznam");

        using var cudzi = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Over.Rovne<string?>(null, PodpisVydania.OcakavanySucetInstalatora(zoznam, cudzi.SignData(zoznam, HashAlgorithmName.SHA256), verzia, verejny), "cudzí kľúč");
        Over.Rovne<string?>(null, PodpisVydania.OcakavanySucetInstalatora(zoznam, podpis, verzia, ""), "bez kľúča sa nikdy neinštaluje");
    }

    [Test]
    public void ZoznamSuctovIgnorujeKomentareAChybneRiadky()
    {
        var sucet = new string('b', 64);
        var zoznam = ZoznamSuctov.Citaj($"# hlavička\n{sucet}  a.exe\n{sucet} *b.exe\nzly riadok\n\n");
        Over.Rovne(2, zoznam.Count);
        Over.Rovne(sucet.ToUpperInvariant(), zoznam["a.exe"]);
        Over.Rovne(sucet.ToUpperInvariant(), zoznam["b.exe"]);
    }

    [Test]
    public void VydanieZOdpovedeGitHubu()
    {
        const string json = """
            {
              "tag_name": "v0.1.0", "draft": false, "prerelease": false, "body": " Novinky ",
              "html_url": "https://github.com/tarzanislav/katalog/releases/tag/v0.1.0",
              "assets": [
                {"name": "KATALOG_Setup_0.1.0.exe", "browser_download_url": "https://github.com/tarzanislav/katalog/releases/download/v0.1.0/KATALOG_Setup_0.1.0.exe"},
                {"name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/tarzanislav/katalog/releases/download/v0.1.0/SHA256SUMS.txt"},
                {"name": "SHA256SUMS.txt.sig", "browser_download_url": "https://github.com/tarzanislav/katalog/releases/download/v0.1.0/SHA256SUMS.txt.sig"},
                {"name": "cudzi.exe", "browser_download_url": "https://example.com/cudzi.exe"}
              ]
            }
            """;
        var vydanie = Vydanie.Citaj(json)!;
        Over.Rovne(new Version(0, 1, 0), vydanie.Verzia);
        Over.Rovne("Novinky", vydanie.Poznamky);
        Over.Pravda(vydanie.Instalator is not null && vydanie.ZoznamSuctov is not null && vydanie.Podpis is not null, "všetky súbory vydania");
        Over.Pravda(vydanie.JeNovsieAko(new Version(0, 0, 0)), "novšie ako vývojová verzia");
        Over.Pravda(!vydanie.JeNovsieAko(new Version(0, 1, 0)), "nie novšie ako to isté");
        Over.Pravda(!vydanie.DaSaNainstalovat, "bez verejného kľúča sa samo neinštaluje");

        Over.Rovne<Vydanie?>(null, Vydanie.Citaj(json.Replace("\"prerelease\": false", "\"prerelease\": true", StringComparison.Ordinal)), "predbežné vydanie");
        Over.Rovne<Vydanie?>(null, Vydanie.Citaj(json.Replace("v0.1.0\"", "v0.1\"", StringComparison.Ordinal)), "verzia nie je 1.2.3");
        Over.Rovne<Vydanie?>(null, Vydanie.Citaj(json.Replace("https://github.com/tarzanislav/katalog/releases/tag", "https://example.com/x", StringComparison.Ordinal)), "cudzia stránka");
    }

    [Test]
    public void OficialneAdresyVydani()
    {
        Over.Pravda(Vydanie.JeOficialnaAdresa(new Uri("https://github.com/tarzanislav/katalog/releases")), "github.com");
        Over.Pravda(Vydanie.JeOficialnaAdresa(new Uri("https://release-assets.githubusercontent.com/x")), "úložisko vydaní");
        Over.Pravda(!Vydanie.JeOficialnaAdresa(new Uri("http://github.com/x")), "bez HTTPS nie");
        Over.Pravda(!Vydanie.JeOficialnaAdresa(new Uri("https://github.com.example.com/x")), "podvrhnutá doména nie");
    }

    [Test]
    public void KontrolaAktualizaciiRazZaDen()
    {
        var teraz = new DateTime(2026, 11, 8, 8, 0, 0, DateTimeKind.Utc);
        Over.Pravda(PravidloAktualizacii.MaKontrolovat(true, null, teraz), "prvý raz");
        Over.Pravda(!PravidloAktualizacii.MaKontrolovat(true, teraz.AddHours(-3), teraz), "pred tromi hodinami");
        Over.Pravda(PravidloAktualizacii.MaKontrolovat(true, teraz.AddHours(-25), teraz), "pred dňom");
        Over.Pravda(PravidloAktualizacii.MaKontrolovat(true, teraz.AddHours(5), teraz), "hodiny posunuté späť");
        Over.Pravda(!PravidloAktualizacii.MaKontrolovat(false, null, teraz), "vypnuté");
    }
}
