using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Vystupy;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Katalog.Okno;

/// <summary>
/// Playlists and random choice (stage E7): hand-made, smart and random playlists and the library's suggestions on the
/// wall; playing a whole playlist (a series plays its next unwatched episode), shuffled; export to M3U; "pick something
/// for me" among the titles shown.
/// </summary>
public partial class HlavneOkno
{
    private NavrhPlaylistu? _otvorenyNavrh;
    private readonly HashSet<long> _uzVybrane = [];

    /// <summary>The titles of an open playlist or suggestion, narrowed by the filter (in the playlist's own order).</summary>
    private static IReadOnlyList<PolozkaKniznice> TitulyPlaylistu(
        SqliteConnection c, ObsahKniznice obsah, (long Id, string Nazov)? otvorene, NavrhPlaylistu? navrh, Dotaz filter, KontextHladania kontext)
    {
        IReadOnlyList<PolozkaKniznice> tituly = obsah == ObsahKniznice.Navrh && navrh is not null
            ? Playlisty.Tituly(c, new Playlist(0, navrh.Nazov, DruhPlaylistu.Inteligentny, navrh.Dotaz, null, 0, 0, 0), kontext)
            : otvorene is { } o && Playlisty.Najdi(c, o.Id) is { } playlist ? Playlisty.Tituly(c, playlist, kontext) : [];
        if (filter.JePrazdny)
        {
            return tituly;
        }

        var zodpovedaju = KniznicaTitulov.Najdi(c, filter, kontext, PohladKniznice.Vsetko).Select(t => t.Id).ToHashSet();
        return tituly.Where(t => zodpovedaju.Contains(t.Id)).ToList();
    }

    /// <summary>The open playlist (null in other views and for a suggestion).</summary>
    private Playlist? OtvorenyPlaylist()
    {
        if (_obsah != ObsahKniznice.Playlist || _otvorene is not { } o || Kniznica is not { } kniznica)
        {
            return null;
        }

        using var c = kniznica.OtvorCitanie();
        return Playlisty.Najdi(c, o.Id);
    }

    private void UkazTlacidlaPlaylistov(ObsahKniznice obsah)
    {
        var playlist = obsah == ObsahKniznice.Playlist ? OtvorenyPlaylist() : null;
        var titulov = _polozkySteny.Count(p => p.Titul is not null);
        TlacidloNovyPlaylist.Visibility = obsah == ObsahKniznice.Playlisty ? Visibility.Visible : Visibility.Collapsed;
        TlacidloPrehratPlaylist.Visibility = TlacidloZamiesat.Visibility =
            obsah is ObsahKniznice.Playlist or ObsahKniznice.Navrh && titulov > 0 ? Visibility.Visible : Visibility.Collapsed;
        TlacidloUlozitNavrh.Visibility = obsah == ObsahKniznice.Navrh ? Visibility.Visible : Visibility.Collapsed;
        TlacidloPlaylist.Visibility = playlist is not null ? Visibility.Visible : Visibility.Collapsed;
        TlacidloNahodne.Visibility = obsah is ObsahKniznice.Playlisty or ObsahKniznice.Kolekcie or ObsahKniznice.ZoznamyHistra or ObsahKniznice.Stitky
            ? Visibility.Collapsed
            : Visibility.Visible;
        var rucny = playlist?.Druh == DruhPlaylistu.Rucny;
        PonukaVylosovat.Visibility = playlist?.Druh == DruhPlaylistu.Nahodny ? Visibility.Visible : Visibility.Collapsed;
        PonukaVyssie.Visibility = PonukaNizsie.Visibility = PonukaOdobrat.Visibility = rucny ? Visibility.Visible : Visibility.Collapsed;
        if (playlist is not null)
        {
            PocetKniznice.Text += playlist.Druh switch
            {
                DruhPlaylistu.Nahodny => " · náhodný",
                DruhPlaylistu.Inteligentny => " · inteligentný",
                _ => " · ručný",
            };
        }

        _uzVybrane.Clear();
    }

    // ------------------------------------------------------------------ keeping playlists

    private int PocetPreDotaz(string dotaz)
    {
        if (Kniznica is not { } kniznica)
        {
            return 0;
        }

        try
        {
            using var c = kniznica.OtvorCitanie();
            return KniznicaTitulov.Najdi(c, Dotaz.Citaj(dotaz), KontextHladania.Teraz(_stav?.Pripojene.Keys.ToList() ?? []), PohladKniznice.Vsetko).Count(t => t.Mam);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or SqliteException)
        {
            return 0;
        }
    }

    private async void NovyPlaylist_Click(object sender, RoutedEventArgs e) => await NovyPlaylistAsync(DruhPlaylistu.Inteligentny, "", null);

    /// <summary>Asks for a new playlist; returns its id (null when cancelled).</summary>
    private async Task<long?> NovyPlaylistAsync(DruhPlaylistu druh, string dotaz, string? nazov)
    {
        if (Kniznica is not { } kniznica ||
            PlaylistOkno.Opytaj(this, nazov ?? "Nový playlist", druh, dotaz, null, novy: true, PocetPreDotaz) is not { } odpoved)
        {
            return null;
        }

        var id = await kniznica.Zapis.ZapisAsync(s => Playlisty.Vytvor(s, odpoved.Nazov, odpoved.Druh, odpoved.Dotaz, odpoved.Pocet, Cas.Teraz()));
        _obsah = ObsahKniznice.Playlist;
        _otvorene = (id, odpoved.Nazov);
        await ZobrazKnizniciuAsync();
        return id;
    }

    private async void UlozitNavrh_Click(object sender, RoutedEventArgs e)
    {
        if (_otvorenyNavrh is { } navrh)
        {
            await NovyPlaylistAsync(DruhPlaylistu.Inteligentny, navrh.Dotaz, navrh.Nazov);
        }
    }

    private async void UpravitPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || OtvorenyPlaylist() is not { } p ||
            PlaylistOkno.Opytaj(this, p.Nazov, p.Druh, p.Dotaz, p.Pocet, novy: false, PocetPreDotaz) is not { } odpoved)
        {
            return;
        }

        await kniznica.Zapis.ZapisAsync(s => Playlisty.Uprav(s, p.Id, odpoved.Nazov, odpoved.Dotaz, odpoved.Pocet));
        _otvorene = (p.Id, odpoved.Nazov);
        await ZobrazKnizniciuAsync();
    }

    private async void Vylosovat_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is { } kniznica && OtvorenyPlaylist() is { Druh: DruhPlaylistu.Nahodny } p)
        {
            await kniznica.Zapis.ZapisAsync(s => Playlisty.Vylosuj(s, p.Id));
            await ZobrazKnizniciuAsync();
        }
    }

    private async void PosunutVyssie_Click(object sender, RoutedEventArgs e) => await PosunAsync(-1);

    private async void PosunutNizsie_Click(object sender, RoutedEventArgs e) => await PosunAsync(1);

    private async Task PosunAsync(int smer)
    {
        if (Kniznica is { } kniznica && OtvorenyPlaylist() is { Druh: DruhPlaylistu.Rucny } p && _vybranyTitul is { } titul)
        {
            await kniznica.Zapis.ZapisAsync(s => Playlisty.Posun(s, p.Id, titul, smer));
            await ZobrazKnizniciuAsync(zachovatVyber: true);
        }
    }

    private async void OdobratZPlaylistu_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is { } kniznica && OtvorenyPlaylist() is { Druh: DruhPlaylistu.Rucny } p && _vybranyTitul is { } titul)
        {
            await kniznica.Zapis.ZapisAsync(s => Playlisty.Odober(s, p.Id, [titul]));
            await ZobrazKnizniciuAsync();
        }
    }

    private async void ZmazatPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || OtvorenyPlaylist() is not { } p ||
            MessageBox.Show(this, $"Zmazať playlist „{p.Nazov}“? Tituly v knižnici ostanú.", Produkt.Nazov,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await kniznica.Zapis.ZapisAsync(s => Playlisty.Zmaz(s, p.Id));
        _obsah = ObsahKniznice.Playlisty;
        _otvorene = null;
        await ZobrazKnizniciuAsync();
    }

    /// <summary>The title's detail: into a hand-made playlist, or a new one.</summary>
    private void DoPlaylistu_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _vybranyTitul is not { } titul || sender is not Button tlacidlo)
        {
            return;
        }

        IReadOnlyList<Playlist> rucne;
        using (var c = kniznica.OtvorCitanie())
        {
            rucne = Playlisty.Zoznam(c).Where(p => p.Druh == DruhPlaylistu.Rucny).ToList();
        }

        var ponuka = new ContextMenu { PlacementTarget = tlacidlo, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var playlist in rucne)
        {
            var polozka = new MenuItem { Header = playlist.Nazov };
            polozka.Click += async (_, _) =>
            {
                var pridane = await kniznica.Zapis.ZapisAsync(s => Playlisty.Pridaj(s, playlist.Id, [titul]));
                StavUloh.Text = pridane > 0 ? $"Pridané do playlistu „{playlist.Nazov}“." : $"Titul už v playliste „{playlist.Nazov}“ je.";
            };
            ponuka.Items.Add(polozka);
        }

        if (rucne.Count > 0)
        {
            ponuka.Items.Add(new Separator());
        }

        var novy = new MenuItem { Header = "Nový playlist…" };
        novy.Click += async (_, _) =>
        {
            if (PlaylistOkno.Opytaj(this, "Nový playlist", DruhPlaylistu.Rucny, null, null, novy: true, PocetPreDotaz) is { } odpoved)
            {
                await kniznica.Zapis.ZapisAsync(s =>
                {
                    var id = Playlisty.Vytvor(s, odpoved.Nazov, odpoved.Druh, odpoved.Dotaz, odpoved.Pocet, Cas.Teraz());
                    if (odpoved.Druh == DruhPlaylistu.Rucny)
                    {
                        Playlisty.Pridaj(s, id, [titul]);
                    }
                });
                StavUloh.Text = $"Playlist „{odpoved.Nazov}“ je vytvorený.";
            }
        };
        ponuka.Items.Add(novy);
        ponuka.IsOpen = true;
    }

    // ------------------------------------------------------------------ playing

    private async void PrehratPlaylist_Click(object sender, RoutedEventArgs e) => await PrehrajPlaylistAsync(zamiesat: false);

    private async void ZamiesatPlaylist_Click(object sender, RoutedEventArgs e) => await PrehrajPlaylistAsync(zamiesat: true);

    /// <summary>
    /// What a list of titles plays: a film its best version (on a connected disk when there is one), a series its next
    /// unwatched episode.
    /// </summary>
    private List<(PolozkaKniznice Titul, IReadOnlyList<CielPrehrania> Ciele)> CielePlaylistu(SqliteConnection c, IReadOnlyList<PolozkaKniznice> tituly)
    {
        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var poradie = PorovnanieVerzii.ZTextu(_nastavenia.PoradieKvality);
        var vysledok = new List<(PolozkaKniznice, IReadOnlyList<CielPrehrania>)>();
        foreach (var titul in tituly.Where(t => t.Mam))
        {
            var epizoda = titul.Druh == DruhTitulu.Serial ? Playlisty.DalsiaEpizoda(c, titul.Id) : null;
            if (titul.Druh == DruhTitulu.Serial && epizoda is null)
            {
                continue;
            }

            if (Playlisty.VerziaNaPrehratie(c, titul.Id, epizoda, pripojene, poradie) is { } verzia)
            {
                vysledok.Add((titul, KniznicaTitulov.CielVerzie(c, verzia).Select(x => new CielPrehrania(x.DiskId, x.PriecinokId, x.Subor, x.Pripona)).ToList()));
            }
        }

        return vysledok;
    }

    /// <summary>
    /// Plays the titles of the open playlist or suggestion (shuffled, if asked). Titles on disks not connected are left out
    /// and named; when nothing is connected, the first title's disk is waited for.
    /// </summary>
    internal async Task<bool> PrehrajPlaylistAsync(bool zamiesat)
    {
        if (Kniznica is not { } kniznica)
        {
            return false;
        }

        var tituly = _polozkySteny.Where(p => p.Titul is not null).Select(p => p.Titul!).ToList();
        if (zamiesat)
        {
            tituly = Playlisty.Losuj(tituly, Random.Shared.NextInt64(), tituly.Count).ToList();
        }

        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var (ciele, vynechanych, disky) = await Task.Run(() =>
        {
            using var c = kniznica.OtvorCitanie();
            var vsetky = CielePlaylistu(c, tituly);
            var dostupne = vsetky.Where(x => x.Ciele.All(ci => pripojene.Contains(ci.DiskId))).ToList();
            var chybajuce = vsetky.Except(dostupne).SelectMany(x => x.Ciele.Select(ci => ci.DiskId)).Distinct()
                .Select(d => KatalogyADisky.NajdiDisk(c, d)).OfType<Disk>().Select(d => "D" + d.CisloText).Order(StringComparer.Ordinal).ToList();
            // nothing connected: the first title waits for its disk (the window says which)
            List<CielPrehrania> hraj = dostupne.Count > 0 ? dostupne.SelectMany(x => x.Ciele).ToList() : vsetky.Take(1).SelectMany(x => x.Ciele).ToList();
            return (hraj, dostupne.Count > 0 ? vsetky.Count - dostupne.Count : 0, chybajuce);
        });

        if (ciele.Count == 0)
        {
            MessageBox.Show(this, "V playliste nie je nič na prehratie.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var hra = await PrehrajAsync(ciele);
        if (hra && vynechanych > 0)
        {
            StavUloh.Text += $" · vynechané {Format.Mnozne(vynechanych, "titul", "tituly", "titulov")} na nepripojených diskoch ({string.Join(", ", disky)})";
        }

        return hra;
    }

    private void ExportovatM3u_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || OtvorenyPlaylist() is not { } playlist)
        {
            return;
        }

        var tituly = _polozkySteny.Where(p => p.Titul is not null).Select(p => p.Titul!).ToList();
        List<PolozkaM3u> polozky;
        int vynechane;
        using (var c = kniznica.OtvorCitanie())
        {
            (polozky, vynechane) = PolozkyM3u(c, tituly);
        }

        if (polozky.Count == 0)
        {
            MessageBox.Show(this, "Žiadny titul playlistu nie je na pripojenom disku – M3U obsahuje cesty, preto treba disky pripojiť.",
                Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportovať playlist",
            Filter = "Playlist M3U (*.m3u8)|*.m3u8",
            FileName = BezpecnyNazov(playlist.Nazov) + ".m3u8",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, M3u.Text(polozky, playlist.Nazov), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StavUloh.Text = $"Playlist uložený: {dialog.FileName}" + (vynechane > 0 ? $" (vynechané {Format.Mnozne(vynechane, "titul", "tituly", "titulov")} na nepripojených diskoch)" : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Playlist sa nepodarilo uložiť: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>The entries of an M3U: full paths on the disks connected now (the self-test uses it).</summary>
    internal (List<PolozkaM3u> Polozky, int Vynechane) PolozkyM3u(SqliteConnection c, IReadOnlyList<PolozkaKniznice> tituly)
    {
        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var polozky = new List<PolozkaM3u>();
        var vynechane = 0;
        foreach (var (titul, ciele) in CielePlaylistu(c, tituly))
        {
            if (!ciele.All(ci => pripojene.Contains(ci.DiskId)))
            {
                vynechane++;
                continue;
            }

            var nazov = titul.Nazov + (titul.Rok is { } r ? $" ({r})" : "");
            foreach (var p in PolozkyPrehravania(c, ciele))
            {
                polozky.Add(new PolozkaM3u(p.Cesta, titul.Druh == DruhTitulu.Serial ? $"{nazov} – {Path.GetFileNameWithoutExtension(p.Cesta)}" : nazov));
            }
        }

        return (polozky, vynechane);
    }

    // ------------------------------------------------------------------ pick something for me

    private async void NahodnyVyber_Click(object sender, RoutedEventArgs e) => await VyberNahodneAsync();

    /// <summary>
    /// Selects a random title among those shown that the user has (the filter applies: videne:nie, zaner:…); a title is not
    /// picked twice until all have been (the self-test calls it).
    /// </summary>
    internal async Task<PolozkaSteny?> VyberNahodneAsync()
    {
        var kandidati = _polozkySteny.Where(p => p.Titul is { Mam: true }).ToList();
        if (kandidati.Count == 0)
        {
            StavUloh.Text = "Nie je z čoho vyberať – v zobrazení nie je žiadny titul, ktorý máš.";
            return null;
        }

        var nove = kandidati.Where(p => !_uzVybrane.Contains(p.Titul!.Id)).ToList();
        if (nove.Count == 0)
        {
            _uzVybrane.Clear();
            nove = kandidati;
        }

        var vybrana = nove[Random.Shared.Next(nove.Count)];
        _uzVybrane.Add(vybrana.Titul!.Id);
        await VyberPolozkuAsync(vybrana);
        if (ZoznamTitulov.Visibility == Visibility.Visible)
        {
            ZoznamTitulov.SelectedItem = vybrana;
            ZoznamTitulov.ScrollIntoView(vybrana);
        }
        else if (StenaPlagatov.ItemsSource is IEnumerable<RiadokSteny> riadky && riadky.FirstOrDefault(r => r.Polozky.Contains(vybrana)) is { } riadok)
        {
            StenaPlagatov.ScrollIntoView(riadok);
        }

        StavUloh.Text = $"Náhodný výber: {vybrana.Nazov}" + (vybrana.Titul.Rok is { } rok ? $" ({rok})" : "") +
                        $" · ďalší klik vyberie iný ({_uzVybrane.Count} z {kandidati.Count})";
        return vybrana;
    }
}
