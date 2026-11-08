using Katalog.Jadro.Prehravanie;

namespace Katalog.Testy;

/// <summary>How players are started: quoting, files, playlists, Blu-ray and DVD folders. Paths are invented.</summary>
public sealed class PrehravanieTesty
{
    private const string Film = @"E:\Filmy\Vymyslený film (2024)\Vymyslený film (2024).mkv";
    private const string Epizoda = @"E:\Seriály\Vymyslený seriál S01E01.mkv";
    private const string BluRay = @"E:\Disky\Vymyslený Blu-ray";
    private const string Playlist = @"C:\Temp\KATALOG\playlist.m3u8";

    [Test]
    public void ArgumentyAkoIchCitaWindows()
    {
        Over.Rovne(@"E:\Filmy\film.mkv", PrikazyPrehravania.Argument(@"E:\Filmy\film.mkv"), "bez medzier bez úvodzoviek");
        Over.Rovne("\"E:\\Moje filmy\\a b.mkv\"", PrikazyPrehravania.Argument(@"E:\Moje filmy\a b.mkv"), "s medzerou");
        Over.Rovne("\"E:\\Moje filmy\\\\\"", PrikazyPrehravania.Argument(@"E:\Moje filmy\"), "lomka pred koncom sa zdvojí");
        Over.Rovne("\"a\\\"b c\"", PrikazyPrehravania.Argument("a\"b c"), "úvodzovka vnútri");
        Over.Rovne("\"\"", PrikazyPrehravania.Argument(""), "prázdny argument");
    }

    [Test]
    public void PrehravacPodlaProgramu()
    {
        Over.Rovne(DruhPrehravaca.Vlc, Prehravac.ZProgramu(@"C:\Program Files\VideoLAN\VLC\vlc.exe").Druh, "VLC");
        Over.Rovne(DruhPrehravaca.MpcHc, Prehravac.ZProgramu(@"C:\Program Files\MPC-HC\mpc-hc64.exe").Druh, "MPC-HC");
        Over.Rovne(DruhPrehravaca.MpcBe, Prehravac.ZProgramu(@"C:\Program Files\MPC-BE x64\mpc-be64.exe").Druh, "MPC-BE");
        Over.Rovne(DruhPrehravaca.PotPlayer, Prehravac.ZProgramu(@"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe").Druh, "PotPlayer");
        Over.Rovne(DruhPrehravaca.Mpv, Prehravac.ZProgramu(@"D:\Nástroje\mpv\mpv.exe").Druh, "mpv");
        var iny = Prehravac.ZProgramu(@"D:\Nástroje\Vymyslený prehrávač.exe");
        Over.Rovne(DruhPrehravaca.Iny, iny.Druh, "iný");
        Over.Rovne("Vymyslený prehrávač", iny.Nazov, "názov iného prehrávača");
        Over.Pravda(!Prehravac.Predvoleny.PrehraStrukturuDisku && Prehravac.ZProgramu("vlc.exe").PrehraStrukturuDisku, "kto vie Blu-ray");
    }

    [Test]
    public void PredvolenyPrehravacWindows()
    {
        var jeden = PrikazyPrehravania.Priprav(Prehravac.Predvoleny, [new PolozkaPrehravania(Film, DruhPolozkyPrehravania.Subor)], Playlist);
        Over.Rovne(new Spustenie(Film, "", true, null), jeden, "jeden súbor otvorí Windows");

        var dva = PrikazyPrehravania.Priprav(Prehravac.Predvoleny,
            [new PolozkaPrehravania(Film, DruhPolozkyPrehravania.Subor), new PolozkaPrehravania(Epizoda, DruhPolozkyPrehravania.Subor)], Playlist);
        Over.Rovne(Playlist, dva.Program, "viac súborov ide cez playlist");
        Over.Pravda(dva.CezShell, "playlist otvorí Windows");
        Over.Rovne("#EXTM3U\r\n" + Film + "\r\n" + Epizoda + "\r\n", dva.Playlist, "obsah playlistu");

        var disk = PrikazyPrehravania.Priprav(Prehravac.Predvoleny, [new PolozkaPrehravania(BluRay, DruhPolozkyPrehravania.BluRay)], Playlist);
        Over.Rovne(Path.Combine(BluRay, "BDMV", "index.bdmv"), disk.Program, "Blu-ray cez index.bdmv");
    }

    [Test]
    public void PrehravaceDostanuSuboryAjDisky()
    {
        var vlc = Prehravac.ZProgramu(@"C:\Program Files\VideoLAN\VLC\vlc.exe");
        var subory = PrikazyPrehravania.Priprav(vlc,
            [new PolozkaPrehravania(Film, DruhPolozkyPrehravania.Subor), new PolozkaPrehravania(Epizoda, DruhPolozkyPrehravania.Subor)], Playlist);
        Over.Rovne(new Spustenie(vlc.Program!, $"\"{Film}\" \"{Epizoda}\"", false, null), subory, "VLC dostane oba súbory");

        var bluRay = new PolozkaPrehravania(BluRay, DruhPolozkyPrehravania.BluRay);
        Over.Rovne("\"bluray:///E:/Disky/Vymyslený Blu-ray/\"", PrikazyPrehravania.Priprav(vlc, [bluRay], Playlist).Argumenty, "VLC Blu-ray");
        Over.Rovne("dvd:///E:/DVD/", PrikazyPrehravania.Priprav(vlc, [new PolozkaPrehravania(@"E:\DVD", DruhPolozkyPrehravania.Dvd)], Playlist).Argumenty, "VLC DVD");

        var mpc = Prehravac.ZProgramu(@"C:\Program Files\MPC-HC\mpc-hc64.exe");
        Over.Rovne(PrikazyPrehravania.Argument(Path.Combine(BluRay, "BDMV", "index.bdmv")), PrikazyPrehravania.Priprav(mpc, [bluRay], Playlist).Argumenty, "MPC-HC Blu-ray");

        var mpv = Prehravac.ZProgramu(@"D:\Nástroje\mpv\mpv.exe");
        Over.Rovne($"bd:// --bluray-device=\"{BluRay}\"", PrikazyPrehravania.Priprav(mpv, [bluRay], Playlist).Argumenty, "mpv Blu-ray");
    }

    [Test]
    public void StrukturaDiskuPodlaPodpriecinkov()
    {
        Over.Rovne(DruhPolozkyPrehravania.BluRay, PrikazyPrehravania.StrukturaDisku(["CERTIFICATE", "bdmv"]), "Blu-ray");
        Over.Rovne(DruhPolozkyPrehravania.Dvd, PrikazyPrehravania.StrukturaDisku(["AUDIO_TS", "VIDEO_TS"]), "DVD");
        Over.Rovne<DruhPolozkyPrehravania?>(null, PrikazyPrehravania.StrukturaDisku(["Season 01", "Extras"]), "obyčajný priečinok");
    }
}
