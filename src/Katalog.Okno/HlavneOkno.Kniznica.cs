using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Okno;

/// <summary>What the library panel shows.</summary>
internal enum ObsahKniznice
{
    Filmy,
    Serialy,
    NaPotvrdenie,
    Zelane,
    Kolekcie,
    Kolekcia,
    ZoznamyHistra,
    ZoznamHistra,
    Duplicity,
    Nezalohovane,
    Stitky,
    Stitok,
    Playlisty,
    Playlist,
    Navrh,
}

/// <summary>
/// The library of titles in the window (stage E4): the poster wall or list of films, series, titles waiting for
/// confirmation, wishes, collections and HISTRA lists, filtered by the one query language; the detail of a title with its
/// versions on disks, episodes, collections and the candidates of the sources to choose from.
/// </summary>
public partial class HlavneOkno
{
    private readonly DispatcherTimer _odkladFiltra = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SpravcaKniznice? _kniznicaTitulov;
    private Tajomstva? _tajomstva;
    private StavKnizniceTitulov? _stavKnizniceTitulov;
    private ObsahKniznice _obsah = ObsahKniznice.Filmy;
    private (long Id, string Nazov)? _otvorene;
    private List<PolozkaSteny> _polozkySteny = [];
    private int _stlpceSteny;
    private long? _vybranyTitul;
    private DetailTitulu? _detail;
    private Vypozicka? _detailVypozicka;
    private IReadOnlyList<string> _detailStitky = [];
    private int _cisloKniznice;
    private bool _plnimDetail;

    /// <summary>The library's background work (the self-test gives it stand-in sources).</summary>
    internal SpravcaKniznice? SpravcaTitulov => _kniznicaTitulov;

    /// <summary>The tiles shown now (the self-test reads them).</summary>
    internal IReadOnlyList<PolozkaSteny> PolozkySteny => _polozkySteny;

    /// <summary>The detail shown now (the self-test reads it).</summary>
    internal DetailTitulu? DetailZobrazeny => _detail;

    private Tajomstva TajomstvaProgramu => _tajomstva ??= Tajomstva.Nacitaj(Path.GetDirectoryName(_nastavenia.Subor) is { Length: > 0 } p ? p : Nastavenia.PredvolenyPriecinok);

    private void InicializujKnizniciu(Kniznica kniznica)
    {
        _kniznicaTitulov = new SpravcaKniznice(kniznica, _nastavenia, TajomstvaProgramu, _obrazky ?? UlozisteObrazkov.PreKniznicu(kniznica.Cesta));
        _kniznicaTitulov.Zmena += stav =>
        {
            _stavKnizniceTitulov = stav;
            ZobrazPriebehSkenov();
            if (stav is null && _kniznicaTitulov.PoslednaChyba is { } chyba)
            {
                StavUloh.Text = "Zdroje údajov: " + chyba;
            }
        };
        _kniznicaTitulov.Zmenene += () =>
        {
            if (PanelKniznice.Visibility == Visibility.Visible)
            {
                _ = ZobrazKnizniciuAsync(zachovatVyber: true);
            }

            _ = ObnovPoctyKnizniceAsync();
        };
        _odkladFiltra.Tick += async (_, _) =>
        {
            _odkladFiltra.Stop();
            await ZobrazKnizniciuAsync();
        };
        ZobrazeniePlagaty.Checked += (_, _) => PrepniZobrazenie();
        ZobrazenieZoznam.Checked += (_, _) => PrepniZobrazenie();
        DetailMojeHodnotenie.ItemsSource = new[] { "–" }.Concat(Enumerable.Range(1, 10).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
    }

    /// <summary>Recognises new videos and lets the sources fill in (after opening, scans and Settings; the self-test awaits it).</summary>
    internal Task DoplnKnizniciuAsync() => _kniznicaTitulov?.SpustiAsync() ?? Task.CompletedTask;

    private async Task ObnovPoctyKnizniceAsync()
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        try
        {
            var (filmy, serialy, naPotvrdenie, zelane) = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return KniznicaTitulov.Pocty(c);
            });
            NavigaciaFilmy.Content = filmy > 0 ? $"Filmy ({Format.Pocet(filmy)})" : "Filmy";
            NavigaciaSerialy.Content = serialy > 0 ? $"Seriály ({Format.Pocet(serialy)})" : "Seriály";
            NavigaciaNaPotvrdenie.Content = naPotvrdenie > 0 ? $"Na potvrdenie ({Format.Pocet(naPotvrdenie)})" : "Na potvrdenie";
            NavigaciaZelane.Content = zelane > 0 ? $"Zoznam želaní ({Format.Pocet(zelane)})" : "Zoznam želaní";
            var (duplicity, nezalohovane) = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return KniznicaTitulov.PoctyZbierok(c);
            });
            NavigaciaDuplicity.Content = duplicity > 0 ? $"Duplicity ({Format.Pocet(duplicity)})" : "Duplicity";
            NavigaciaNezalohovane.Content = nezalohovane > 0 ? $"Len na jednom disku ({Format.Pocet(nezalohovane)})" : "Len na jednom disku";
            var (pozicane, stitky) = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return (Vypozicky.Otvorene(c).Count, StitkyTitulov.Zoznam(c).Count);
            });
            NavigaciaPozicane.Content = pozicane > 0 ? $"Požičané ({Format.Pocet(pozicane)})" : "Požičané";
            NavigaciaStitky.Content = stitky > 0 ? $"Štítky ({Format.Pocet(stitky)})" : "Štítky";
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
        }
    }

    /// <summary>A view of the library chosen in the navigation.</summary>
    private bool ZobrazObsahNavigacie(ListBoxItem polozka)
    {
        if (ReferenceEquals(polozka, NavigaciaPrehlad))
        {
            _ = ZobrazPrehladAsync();
            return true;
        }

        if (ReferenceEquals(polozka, NavigaciaStatistiky))
        {
            _ = ZobrazStatistikyAsync();
            return true;
        }

        if (ReferenceEquals(polozka, NavigaciaPozicane))
        {
            _ = ZobrazVypozickyAsync();
            return true;
        }

        ObsahKniznice? obsah = polozka switch
        {
            _ when ReferenceEquals(polozka, NavigaciaFilmy) => ObsahKniznice.Filmy,
            _ when ReferenceEquals(polozka, NavigaciaSerialy) => ObsahKniznice.Serialy,
            _ when ReferenceEquals(polozka, NavigaciaNaPotvrdenie) => ObsahKniznice.NaPotvrdenie,
            _ when ReferenceEquals(polozka, NavigaciaZelane) => ObsahKniznice.Zelane,
            _ when ReferenceEquals(polozka, NavigaciaKolekcie) => ObsahKniznice.Kolekcie,
            _ when ReferenceEquals(polozka, NavigaciaHistra) => ObsahKniznice.ZoznamyHistra,
            _ when ReferenceEquals(polozka, NavigaciaDuplicity) => ObsahKniznice.Duplicity,
            _ when ReferenceEquals(polozka, NavigaciaNezalohovane) => ObsahKniznice.Nezalohovane,
            _ when ReferenceEquals(polozka, NavigaciaStitky) => ObsahKniznice.Stitky,
            _ when ReferenceEquals(polozka, NavigaciaPlaylisty) => ObsahKniznice.Playlisty,
            _ => null,
        };
        if (obsah is not { } o)
        {
            return false;
        }

        _obsah = o;
        _otvorene = null;
        _ = ZobrazKnizniciuAsync();
        return true;
    }

    /// <summary>Shows the library panel with the current view (the self-test awaits it).</summary>
    internal async Task ZobrazKnizniciuAsync(bool zachovatVyber = false)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        if (PanelKniznice.Visibility != Visibility.Visible)
        {
            UkazStred(PanelKniznice);
            _zobrazenyDisk = null;
            _zobrazenyPriecinok = null;
        }

        var cislo = ++_cisloKniznice;
        var obsah = _obsah;
        var otvorene = _otvorene;
        var dotaz = Dotaz.Citaj(FilterKniznice.Text);
        var kontext = KontextHladania.Teraz(_stav?.Pripojene.Keys.ToList() ?? []);
        var obrazky = _obrazky;
        var navrh = _otvorenyNavrh;
        List<PolozkaSteny> polozky;
        try
        {
            polozky = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return obsah switch
                {
                    ObsahKniznice.Kolekcie => KniznicaTitulov.ZoznamKolekcii(c)
                        .Where(k => dotaz.JePrazdny || dotaz.Podmienky.OfType<Slovo>().All(s => Normalizacia.Nazov(k.Nazov).Contains(Normalizacia.Nazov(s.Text), StringComparison.Ordinal) != s.Zaporna))
                        .Select(k => new PolozkaSteny(k, obrazky)).ToList(),
                    ObsahKniznice.ZoznamyHistra => KniznicaTitulov.ZoznamyHistra(c).Select(z => new PolozkaSteny(z)).ToList(),
                    ObsahKniznice.Playlisty => Playlisty.Zoznam(c)
                        .Where(k => dotaz.JePrazdny || dotaz.Podmienky.OfType<Slovo>().All(s => Normalizacia.Nazov(k.Nazov).Contains(Normalizacia.Nazov(s.Text), StringComparison.Ordinal) != s.Zaporna))
                        .Select(k => new PolozkaSteny(k))
                        .Concat(dotaz.JePrazdny ? Playlisty.Navrhy(c, kontext).Select(n => new PolozkaSteny(n)) : [])
                        .ToList(),
                    ObsahKniznice.Playlist or ObsahKniznice.Navrh => TitulyPlaylistu(c, obsah, otvorene, navrh, dotaz, kontext)
                        .Select(t => new PolozkaSteny(t, obrazky)).ToList(),
                    ObsahKniznice.Stitky => StitkyTitulov.Zoznam(c)
                        .Where(k => dotaz.JePrazdny || dotaz.Podmienky.OfType<Slovo>().All(s => Normalizacia.Nazov(k.Nazov).Contains(Normalizacia.Nazov(s.Text), StringComparison.Ordinal) != s.Zaporna))
                        .Select(k => new PolozkaSteny(k)).ToList(),
                    _ => KniznicaTitulov.Najdi(c, dotaz, kontext, obsah switch
                        {
                            ObsahKniznice.Filmy => PohladKniznice.Filmy,
                            ObsahKniznice.Serialy => PohladKniznice.Serialy,
                            ObsahKniznice.NaPotvrdenie => PohladKniznice.NaPotvrdenie,
                            ObsahKniznice.Zelane => PohladKniznice.Zelane,
                            ObsahKniznice.Duplicity => PohladKniznice.Duplicity,
                            ObsahKniznice.Nezalohovane => PohladKniznice.Nezalohovane,
                            _ => PohladKniznice.Vsetko,
                        },
                        kolekciaId: obsah == ObsahKniznice.Kolekcia ? otvorene?.Id : null,
                        zoznamHistra: obsah == ObsahKniznice.ZoznamHistra ? otvorene?.Id : null,
                        stitokId: obsah == ObsahKniznice.Stitok ? otvorene?.Id : null)
                        .Select(t => new PolozkaSteny(t, obrazky)).ToList(),
                };
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        if (cislo != _cisloKniznice || Kniznica is null)
        {
            return;
        }

        _polozkySteny = polozky;
        NadpisKniznice.Text = obsah switch
        {
            ObsahKniznice.Filmy => "Filmy",
            ObsahKniznice.Serialy => "Seriály",
            ObsahKniznice.NaPotvrdenie => "Na potvrdenie",
            ObsahKniznice.Zelane => "Zoznam želaní",
            ObsahKniznice.Kolekcie => "Kolekcie",
            ObsahKniznice.ZoznamyHistra => "Zoznamy HISTRA",
            ObsahKniznice.Duplicity => "Duplicity",
            ObsahKniznice.Nezalohovane => "Len na jednom disku",
            ObsahKniznice.Stitky => "Štítky",
            ObsahKniznice.Playlisty => "Playlisty",
            ObsahKniznice.Navrh => navrh?.Nazov ?? "",
            _ => otvorene?.Nazov ?? "",
        };
        PocetKniznice.Text = Format.Mnozne(polozky.Count, obsah switch
        {
            ObsahKniznice.Kolekcie => "kolekcia",
            ObsahKniznice.ZoznamyHistra => "zoznam",
            ObsahKniznice.Stitky => "štítok",
            ObsahKniznice.Playlisty => "playlist",
            _ => "titul",
        }, obsah switch
        {
            ObsahKniznice.Kolekcie => "kolekcie",
            ObsahKniznice.ZoznamyHistra => "zoznamy",
            ObsahKniznice.Stitky => "štítky",
            ObsahKniznice.Playlisty => "playlisty",
            _ => "tituly",
        }, obsah switch
        {
            ObsahKniznice.Kolekcie => "kolekcií",
            ObsahKniznice.ZoznamyHistra => "zoznamov",
            ObsahKniznice.Stitky => "štítkov",
            ObsahKniznice.Playlisty => "playlistov",
            _ => "titulov",
        });
        TlacidloSpatKniznica.Visibility = otvorene is null && obsah != ObsahKniznice.Navrh ? Visibility.Collapsed : Visibility.Visible;
        UkazTlacidlaPlaylistov(obsah);
        TlacidloNacitatHistra.Visibility = obsah == ObsahKniznice.ZoznamyHistra ? Visibility.Visible : Visibility.Collapsed;
        UpozornenieKniznice.Text = string.Join(" ", dotaz.Upozornenia);
        UpozornenieKniznice.Visibility = dotaz.Upozornenia.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PrazdnaKniznica.Text = TextPrazdnej(obsah, dotaz);
        PrazdnaKniznica.Visibility = polozky.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ZoznamTitulov.ItemsSource = polozky;
        _stlpceSteny = 0;
        PostavStenu();
        PrepniZobrazenie();

        var vybrany = zachovatVyber ? polozky.FirstOrDefault(p => p.Titul?.Id == _vybranyTitul) : null;
        if (vybrany is not null)
        {
            vybrany.Vybrana = true;
            await ZobrazDetailTituluAsync(vybrany.Titul!.Id);
        }
        else
        {
            _vybranyTitul = null;
            TlacidloPrehratTitul.IsEnabled = false;
            ZobrazDetail(null);
        }
    }

    private string TextPrazdnej(ObsahKniznice obsah, Dotaz dotaz)
    {
        if (!dotaz.JePrazdny)
        {
            return "Nič nezodpovedá filtru. Skús menej slov alebo iné pole.";
        }

        var bezZdrojov = _kniznicaTitulov?.Zdroje().Count == 0
            ? " Názvy, dej a plagáty doplní TMDB alebo HISTRA – zapni ich v Nastaveniach → Zdroje údajov."
            : "";
        return obsah switch
        {
            ObsahKniznice.Filmy or ObsahKniznice.Serialy =>
                "Zatiaľ tu nič nie je. Pridaj disk s filmami alebo seriálmi (vľavo Pridať disk) – po skene sa tu ukážu samy." + bezZdrojov,
            ObsahKniznice.NaPotvrdenie => "Nič nečaká na potvrdenie. Tituly, ktoré sa nedali určiť isto, sa ukážu tu.",
            ObsahKniznice.Zelane => "Zoznam želaní je prázdny. Titul doň pridáš v jeho detaile tlačidlom Želané.",
            ObsahKniznice.Kolekcie => "Zatiaľ žiadne kolekcie. Doplnia sa z TMDB (série filmov) a z Jellyfin.",
            ObsahKniznice.ZoznamyHistra => "Zatiaľ žiadne zoznamy. Načítaj ich tlačidlom „Načítať zoznamy z HISTRA“ (potrebuje token HISTRA).",
            ObsahKniznice.Duplicity => "Žiadny titul nemáš viackrát. Keď bude, ukáže sa tu aj s návrhom, ktorá verzia je lepšia.",
            ObsahKniznice.Nezalohovane => "Všetky tituly máš aspoň na dvoch diskoch. Výborne – strata jedného disku ťa nepripraví o žiadny film.",
            ObsahKniznice.Playlisty => "Zatiaľ žiadny playlist. Vytvor ho tlačidlom „Nový playlist…“ – ručný, inteligentný (podľa dotazu) alebo náhodný.",
            ObsahKniznice.Playlist => "Playlist je prázdny. Tituly doň pridáš v ich detaile tlačidlom „Do playlistu…“.",
            ObsahKniznice.Stitky => "Zatiaľ žiadne štítky. Titulu ich pridáš v jeho detaile tlačidlom „Štítky…“ (napr. Vianoce, Pre deti).",
            _ => "Prázdne.",
        };
    }

    // ------------------------------------------------------------------ the wall and the list

    private void StenaPlagatov_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            PostavStenu();
        }
    }

    /// <summary>Puts the tiles into rows by the width of the wall (only rebuilt when the number of columns changes).</summary>
    private void PostavStenu()
    {
        var stlpce = Math.Max(1, (int)((StenaPlagatov.ActualWidth - 24) / 162));
        if (stlpce == _stlpceSteny && StenaPlagatov.ItemsSource is not null)
        {
            return;
        }

        _stlpceSteny = stlpce;
        StenaPlagatov.ItemsSource = _polozkySteny.Chunk(stlpce).Select(r => new RiadokSteny(r)).ToList();
    }

    private void PrepniZobrazenie()
    {
        if (StenaPlagatov is null || ZoznamTitulov is null)
        {
            return;
        }

        var zoznam = ZobrazenieZoznam.IsChecked == true;
        var prazdne = _polozkySteny.Count == 0;
        StenaPlagatov.Visibility = !zoznam && !prazdne ? Visibility.Visible : Visibility.Collapsed;
        ZoznamTitulov.Visibility = zoznam && !prazdne ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Dlazdica_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PolozkaSteny polozka })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            await OtvorPolozkuAsync(polozka);
            return;
        }

        await VyberPolozkuAsync(polozka);
    }

    private async void ZoznamTitulov_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoznamTitulov.SelectedItem is PolozkaSteny polozka)
        {
            await VyberPolozkuAsync(polozka);
        }
    }

    private async void ZoznamTitulov_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ZoznamTitulov.SelectedItem is PolozkaSteny polozka)
        {
            await OtvorPolozkuAsync(polozka);
        }
    }

    /// <summary>Selects a tile and shows its detail (the self-test calls it).</summary>
    internal async Task VyberPolozkuAsync(PolozkaSteny polozka)
    {
        foreach (var p in _polozkySteny)
        {
            p.Vybrana = ReferenceEquals(p, polozka);
        }

        if (polozka.Titul is { } titul)
        {
            _vybranyTitul = titul.Id;
            TlacidloPrehratTitul.IsEnabled = titul.Mam;
            await ZobrazDetailTituluAsync(titul.Id);
        }
        else
        {
            _vybranyTitul = null;
            TlacidloPrehratTitul.IsEnabled = false;
            ZobrazDetailSkupiny(polozka);
        }
    }

    /// <summary>A double click: a collection or list opens, a title plays.</summary>
    internal async Task OtvorPolozkuAsync(PolozkaSteny polozka)
    {
        switch (polozka)
        {
            case { Kolekcia: { } k }:
                _obsah = ObsahKniznice.Kolekcia;
                _otvorene = (k.Id, k.Nazov);
                await ZobrazKnizniciuAsync();
                break;
            case { Zoznam: { } z }:
                _obsah = ObsahKniznice.ZoznamHistra;
                _otvorene = (z.Id, z.Nazov);
                await ZobrazKnizniciuAsync();
                break;
            case { Znacka: { } st }:
                _obsah = ObsahKniznice.Stitok;
                _otvorene = (st.Id, st.Nazov);
                await ZobrazKnizniciuAsync();
                break;
            case { Playlist: { } pl }:
                _obsah = ObsahKniznice.Playlist;
                _otvorene = (pl.Id, pl.Nazov);
                await ZobrazKnizniciuAsync();
                break;
            case { Navrh: { } n }:
                _obsah = ObsahKniznice.Navrh;
                _otvorene = null;
                _otvorenyNavrh = n;
                await ZobrazKnizniciuAsync();
                break;
            case { Titul: { Mam: true } t }:
                await PrehrajTitulAsync(t.Id);
                break;
        }
    }

    private async void SpatKniznica_Click(object sender, RoutedEventArgs e)
    {
        _obsah = _obsah switch
        {
            ObsahKniznice.ZoznamHistra => ObsahKniznice.ZoznamyHistra,
            ObsahKniznice.Stitok => ObsahKniznice.Stitky,
            ObsahKniznice.Playlist or ObsahKniznice.Navrh => ObsahKniznice.Playlisty,
            _ => ObsahKniznice.Kolekcie,
        };
        _otvorene = null;
        await ZobrazKnizniciuAsync();
    }

    private void FilterKniznice_TextChanged(object sender, TextChangedEventArgs e)
    {
        NapovedaFiltraKniznice.Visibility = FilterKniznice.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _odkladFiltra.Stop();
        _odkladFiltra.Start();
    }

    private async void Doplnit_Click(object sender, RoutedEventArgs e)
    {
        if (_kniznicaTitulov is not { } spravca)
        {
            return;
        }

        if (spravca.Zdroje().Count == 0 &&
            MessageBox.Show(this, "Žiadny zdroj údajov nie je zapnutý (TMDB, HISTRA). Videá sa rozpoznajú len z názvov.\n\nOtvoriť Nastavenia → Zdroje údajov?",
                Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            await OtvorNastaveniaAsync(zdroje: true);
            return;
        }

        await spravca.SpustiAsync();
    }

    private async void NacitatHistra_Click(object sender, RoutedEventArgs e)
    {
        if (_kniznicaTitulov is not { } spravca)
        {
            return;
        }

        TlacidloNacitatHistra.IsEnabled = false;
        try
        {
            var pocet = await spravca.NacitajZoznamyHistraAsync(new Progress<string>(t => StavUloh.Text = t));
            StavUloh.Text = $"Zoznamy HISTRA načítané: {Format.Mnozne(pocet, "zoznam", "zoznamy", "zoznamov")}.";
            await ZobrazKnizniciuAsync();
        }
        catch (ChybaZdroja ex)
        {
            MessageBox.Show(this, ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            TlacidloNacitatHistra.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ detail of a title

    private void ZobrazDetailSkupiny(PolozkaSteny polozka)
    {
        ZobrazDetail(null);
        DetailNadpis.Text = polozka.Nazov;
        DetailPrazdny.Visibility = Visibility.Collapsed;
        var riadky = new List<RiadokDetailu> { new("Obsah", polozka.Podnadpis) };
        if (polozka.Kolekcia?.Dej is { } dej)
        {
            riadky.Add(new RiadokDetailu("O kolekcii", dej));
        }

        if (polozka.Playlist is { } playlist)
        {
            riadky.Add(new RiadokDetailu("Druh", playlist.Druh switch
            {
                DruhPlaylistu.Rucny => "ručný – tituly v tvojom poradí",
                DruhPlaylistu.Nahodny => $"náhodný – {playlist.Pocet} titulov vylosovaných z dotazu (Vylosovať znova dá iné)",
                _ => "inteligentný – všetko, čo zodpovedá dotazu, chronologicky; dopĺňa sa sám",
            }));
            if (!string.IsNullOrEmpty(playlist.Dotaz))
            {
                riadky.Add(new RiadokDetailu("Dotaz", playlist.Dotaz));
            }
        }
        else if (polozka.Navrh is { } navrhPlaylistu)
        {
            riadky.Add(new RiadokDetailu("Dotaz", navrhPlaylistu.Dotaz));
            riadky.Add(new RiadokDetailu("Návrh", "Dvojklik ukáže jeho tituly; tlačidlom „Uložiť ako playlist“ si ho necháš."));
        }

        riadky.Add(new RiadokDetailu("Otvoriť", polozka.Playlist is not null || polozka.Navrh is not null
            ? "Dvojklik ukáže jeho tituly; seriál v ňom hrá ďalšiu nevidenú epizódu."
            : polozka.Znacka is not null
            ? "Dvojklik ukáže jeho tituly. Hľadanie: stitok:\"" + polozka.Nazov + "\""
            : "Dvojklik ukáže jej tituly – aj tie, ktoré nemáš (bledé)."));
        DetailRiadky.ItemsSource = riadky;
    }

    /// <summary>Shows everything about a title in the detail panel.</summary>
    internal async Task ZobrazDetailTituluAsync(long titulId)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        DetailTitulu? detail;
        Vypozicka? vypozicka;
        IReadOnlyList<string> stitky;
        try
        {
            (detail, vypozicka, stitky) = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return (KniznicaTitulov.Detail(c, titulId, PorovnanieVerzii.ZTextu(_nastavenia.PoradieKvality)),
                        Vypozicky.Otvorena(c, null, titulId), StitkyTitulov.Titulu(c, titulId));
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        if (_vybranyTitul != titulId)
        {
            return;
        }

        if (detail is null)
        {
            ZobrazDetail(null);
            return;
        }

        _detail = detail;
        _detailVypozicka = vypozicka;
        _detailStitky = stitky;
        _plnimDetail = true;
        try
        {
            VyplnDetail(detail);
        }
        finally
        {
            _plnimDetail = false;
        }
    }

    private void VyplnDetail(DetailTitulu d)
    {
        var t = d.Polozka;
        DetailNahlad.Visibility = Visibility.Collapsed;
        DetailPrazdny.Visibility = Visibility.Collapsed;
        PanelDetailuTitulu.Visibility = Visibility.Visible;
        DetailNadpis.Text = t.Nazov;
        DetailPlagat.Source = t.Plagat is { } p && _obrazky is { } o && File.Exists(o.Cesta(p)) ? NacitajObrazok(o.Cesta(p)) : null;
        DetailPlagat.Visibility = DetailPlagat.Source is null ? Visibility.Collapsed : Visibility.Visible;
        DetailPodnadpis.Text = string.Join(" · ", new[]
        {
            t.Original is { } orig && orig != t.Nazov ? orig : null,
            t.Rok?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            t.Druh == DruhTitulu.Serial ? "seriál" : "film",
            d.Zanre.Count > 0 ? string.Join(", ", d.Zanre) : null,
            d.DlzkaMinut is { } m ? $"{m} min" : null,
            t.Hodnotenie is { } h ? $"hodnotenie {h.ToString("0.0", Format.Slovensky)}" : null,
        }.Where(x => x is not null));
        DetailPrehrat.IsEnabled = t.Mam;
        DetailVidene.IsChecked = t.Videne;
        DetailVidene.Visibility = t.Druh == DruhTitulu.Film ? Visibility.Visible : Visibility.Collapsed;
        DetailZelane.IsChecked = t.Zelany;
        DetailMojeHodnotenie.SelectedIndex = t.MojeHodnotenie ?? 0;
        DetailPozicat.Content = _detailVypozicka is null ? "Požičať…" : "Vrátené";
        DetailPozicat.ToolTip = _detailVypozicka is { } vp ? $"Požičané: {vp.Komu} od {Format.Datum(vp.Od)}" : "Zapísať, komu titul požičiavaš";

        // choosing: waiting for confirmation, or no source knows the title yet but there are candidates
        var tmdb = d.Identifikatory.Any(i => i.Zdroj == ZdrojUdajov.Tmdb);
        var vyberat = t.NaPotvrdenie || (!tmdb && d.Kandidati.Count > 0);
        DetailVyber.Visibility = vyberat ? Visibility.Visible : Visibility.Collapsed;
        DetailPotvrdit.Visibility = t.NaPotvrdenie ? Visibility.Visible : Visibility.Collapsed;
        DetailVyberNadpis.Text = t.NaPotvrdenie ? "Čo je to za titul?" : "Spresniť titul";
        DetailVyberText.Text = d.Kandidati.Count > 0
            ? "Návrhy zdrojov. Vyber správny – doplnia sa názov, dej a plagát. Nie je medzi nimi? Hľadaj inak: názov a rok, rezia:Meno alebo tmdb:ID."
            : _kniznicaTitulov?.Zdroje().Count > 0
                ? "Zdroje zatiaľ nič nenašli. Hľadaj inak: iný názov a rok, rezia:Meno alebo tmdb:ID (TMDB ID titul hneď potvrdí)."
                : "Zapni TMDB alebo HISTRA v Nastaveniach → Zdroje údajov, alebo titul potvrď tak, ako je.";
        DetailKandidati.ItemsSource = d.Kandidati.Take(12).Select(k => new KartaKandidata(k)).ToList();
        if (DetailHladanie.Tag is not long tag || tag != t.Id)
        {
            DetailHladanie.Tag = t.Id;
            DetailHladanie.Text = t.Rok is { } rokTitulu ? $"{t.Nazov} ({rokTitulu})" : t.Nazov;
        }

        var riadky = new List<RiadokDetailu>();
        if (_detailVypozicka is { } pozicane)
        {
            riadky.Add(new RiadokDetailu("Požičané", $"{pozicane.Komu} · od {Format.Datum(pozicane.Od)}" + (pozicane.Poznamka is { } pp ? $" · {pp}" : "")));
        }

        if (_detailStitky.Count > 0)
        {
            riadky.Add(new RiadokDetailu("Štítky", string.Join(", ", _detailStitky)));
        }

        if (d.Dej is { } dej)
        {
            riadky.Add(new RiadokDetailu("Dej", dej));
        }

        var rezia = d.Osoby.Where(o => o.Rola == RolaOsoby.Rezia).Select(o => o.Meno).ToList();
        if (rezia.Count > 0)
        {
            riadky.Add(new RiadokDetailu(t.Druh == DruhTitulu.Serial ? "Tvorcovia" : "Réžia", string.Join(", ", rezia)));
        }

        var scenar = d.Osoby.Where(o => o.Rola == RolaOsoby.Scenar).Select(o => o.Meno).ToList();
        if (scenar.Count > 0)
        {
            riadky.Add(new RiadokDetailu("Scenár", string.Join(", ", scenar)));
        }

        var herci = d.Osoby.Where(o => o.Rola == RolaOsoby.Herec).Take(12)
            .Select(o => o.Postava is { Length: > 0 } postava ? $"{o.Meno} ({postava})" : o.Meno).ToList();
        if (herci.Count > 0)
        {
            riadky.Add(new RiadokDetailu("Hrajú", string.Join(", ", herci)));
        }

        if (t.Druh == DruhTitulu.Serial && d.Epizody.Count > 0)
        {
            foreach (var seria in d.Epizody.GroupBy(e => e.Seria))
            {
                var mam = seria.Count(e => e.Mam);
                var chybaju = seria.Where(e => !e.Mam).Select(e => e.Cislo.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
                var text = $"mám {mam} z {seria.Count()}" + (chybaju.Count is > 0 and <= 20 ? " · chýba " + string.Join(", ", chybaju) : chybaju.Count > 20 ? $" · chýba {chybaju.Count}" : "");
                var videne = seria.Count(e => e.Videne);
                if (videne > 0)
                {
                    text += $" · videné {videne}";
                }

                riadky.Add(new RiadokDetailu(seria.Key == 0 ? "Špeciály" : $"Séria {seria.Key}", text));
            }
        }

        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        foreach (var v in d.Verzie)
        {
            var prvy = v.Subory.FirstOrDefault();
            if (prvy is null)
            {
                continue;
            }

            var popis = v.Seria is { } s && v.Cislo is { } c ? $"S{s:00}E{c:00} · " : "";
            popis += $"{prvy.DiskCislo} {prvy.DiskNazov}" + (pripojene.Contains(prvy.DiskId) ? " (pripojený)" : "") +
                     $" · {Format.Velkost(v.Subory.Sum(x => x.Velkost))}" +
                     (prvy.Kvalita != TriedaKvality.Nezistena ? $" · {(prvy.Kvalita == TriedaKvality.UHD4K ? "4K" : prvy.Kvalita.ToString())}" : "") +
                     (v.Subory.Count > 1 ? $" · {Format.Mnozne(v.Subory.Count, "súbor", "súbory", "súborov")}" : "") +
                     (v.Poznamka is { } poz ? $" · {poz}" : "") +
                     (!v.Pritomna ? " · zmazaná z disku" : "") + "\n" + prvy.Nazov;
            var nadpis = !v.Potvrdena ? "Verzia (čaká na potvrdenie)" : d.NajlepsieVerzie.Contains(v.Id) ? "Verzia – najlepšia" : "Verzia";
            if (d.NajlepsieVerzie.Contains(v.Id) && DovodNajlepsej(d, v) is { } dovod)
            {
                popis += $"\nnajlepšia: {dovod}";
            }

            riadky.Add(new RiadokDetailu(nadpis, popis));
        }

        foreach (var k in d.Kolekcie)
        {
            riadky.Add(new RiadokDetailu("Kolekcia", $"{k.Nazov} · mám {k.Mam} z {k.Diely}"));
        }

        if (d.ZoznamyHistra.Count > 0)
        {
            riadky.Add(new RiadokDetailu("Zoznamy HISTRA", string.Join(", ", d.ZoznamyHistra)));
        }

        if (d.NaposledyVidene is { } kedy)
        {
            riadky.Add(new RiadokDetailu("Naposledy videné", Format.Cas(kedy)));
        }

        if (d.MojaPoznamka is { } poznamka)
        {
            riadky.Add(new RiadokDetailu("Moja poznámka", poznamka));
        }

        var zdroje = d.Povod.Values.Select(p => p.Zdroj).Distinct().Where(z => z != ZdrojUdajov.Pouzivatel).Select(NazovZdroja).ToList();
        var zamknute = d.Povod.Where(p => p.Value.Zamknute).Select(p => NazovPola(p.Key)).ToList();
        riadky.Add(new RiadokDetailu("Údaje", (zdroje.Count > 0 ? "z " + string.Join(", ", zdroje) : "len z názvov súborov") +
                                              (zamknute.Count > 0 ? " · tvoje (zamknuté): " + string.Join(", ", zamknute) : "")));
        if (d.Identifikatory.Count > 0)
        {
            riadky.Add(new RiadokDetailu("Identifikátory", string.Join(", ", d.Identifikatory.Select(i => $"{NazovZdroja(i.Zdroj)} {i.Hodnota}"))));
        }

        if (d.Identifikatory.Any(i => i.Zdroj == ZdrojUdajov.Tmdb))
        {
            riadky.Add(new RiadokDetailu("Zdroj údajov", "Údaje a plagáty z TMDB (themoviedb.org). " + Katalog.Zdroje.TmdbZdroj.Upozornenie));
        }

        DetailRiadky.ItemsSource = riadky;
    }

    /// <summary>Why the best version is better than the next one of the same film or episode.</summary>
    private string? DovodNajlepsej(DetailTitulu d, VerziaTitulu najlepsia)
    {
        var poradie = PorovnanieVerzii.ZTextu(_nastavenia.PoradieKvality);
        var ja = d.Vlastnosti.FirstOrDefault(x => x.VerziaId == najlepsia.Id);
        var ostatne = d.Verzie.Where(v => v.Id != najlepsia.Id && v.EpizodaId == najlepsia.EpizodaId && v.Pritomna)
            .Select(v => d.Vlastnosti.FirstOrDefault(x => x.VerziaId == v.Id))
            .OfType<VlastnostiVerzie>()
            .OrderByDescending(x => x, Comparer<VlastnostiVerzie>.Create((a, b) => PorovnanieVerzii.Porovnaj(a, b, poradie)))
            .FirstOrDefault();
        return ja is null || ostatne is null ? null : PorovnanieVerzii.Dovod(ja, ostatne, poradie);
    }

    private static string NazovZdroja(ZdrojUdajov zdroj) => zdroj switch
    {
        ZdrojUdajov.NazovSuboru => "názvu súboru",
        ZdrojUdajov.Tmdb => "TMDB",
        ZdrojUdajov.Histra => "HISTRA",
        ZdrojUdajov.Jellyfin => "Jellyfin",
        ZdrojUdajov.Imdb => "IMDb",
        ZdrojUdajov.Ffprobe => "ffprobe",
        _ => "teba",
    };

    private static string NazovPola(string pole) => pole switch
    {
        "nazov" => "názov",
        "original" => "originál",
        "rok" => "rok",
        "dej" => "dej",
        "dlzka_min" => "dĺžka",
        _ => pole,
    };

    private static BitmapImage? NacitajObrazok(string subor)
    {
        try
        {
            var obrazok = new BitmapImage();
            obrazok.BeginInit();
            obrazok.CacheOption = BitmapCacheOption.OnLoad;
            obrazok.DecodePixelWidth = 300;
            obrazok.UriSource = new Uri(subor);
            obrazok.EndInit();
            obrazok.Freeze();
            return obrazok;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ actions on a title

    private async void PrehratTitul_Click(object sender, RoutedEventArgs e)
    {
        if (_vybranyTitul is { } id)
        {
            await PrehrajTitulAsync(id);
        }
    }

    /// <summary>Plays a title: the first present version on a connected disk (else the first present one, waiting for its disk).</summary>
    internal async Task<bool> PrehrajTitulAsync(long titulId)
    {
        if (Kniznica is not { } kniznica)
        {
            return false;
        }

        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var ciele = await Task.Run(() =>
        {
            using var c = kniznica.OtvorCitanie();
            var verzie = KniznicaTitulov.Verzie(c, titulId).Where(v => v.Pritomna).ToList();
            // a series plays its episodes in order: the first version of each episode, on a connected disk when there is one
            var vybrane = verzie
                .GroupBy(v => v.EpizodaId ?? 0)
                .Select(g => g.OrderByDescending(v => v.Subory.Any(s => pripojene.Contains(s.DiskId))).First())
                .OrderBy(v => v.Seria).ThenBy(v => v.Cislo)
                .ToList();
            if (vybrane.Count > 0 && vybrane[0].EpizodaId is null)
            {
                vybrane = [vybrane[0]];
            }

            return vybrane.SelectMany(v => KniznicaTitulov.CielVerzie(c, v.Id))
                .Select(x => new CielPrehrania(x.DiskId, x.PriecinokId, x.Subor, x.Pripona))
                .ToList();
        });
        return ciele.Count > 0 && await PrehrajAsync(ciele);
    }

    private async void DetailVidene_Click(object sender, RoutedEventArgs e)
    {
        if (_vybranyTitul is { } id && Kniznica is { } kniznica)
        {
            var videne = DetailVidene.IsChecked == true;
            await kniznica.Zapis.ZapisAsync(s => Tituly.NastavVidene(s, id, null, videne, Cas.Teraz()));
            await ZobrazKnizniciuAsync(zachovatVyber: true);
        }
    }

    private async void DetailZelane_Click(object sender, RoutedEventArgs e) => await UlozVlastneAsync();

    private async void DetailMojeHodnotenie_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_plnimDetail)
        {
            await UlozVlastneAsync();
        }
    }

    private async Task UlozVlastneAsync()
    {
        if (_vybranyTitul is not { } id || Kniznica is not { } kniznica || _detail is not { } d || d.Polozka.Id != id)
        {
            return;
        }

        int? hodnotenie = DetailMojeHodnotenie.SelectedIndex is int i and > 0 ? i : null;
        var zelany = DetailZelane.IsChecked == true;
        await kniznica.Zapis.ZapisAsync(s => Tituly.NastavVlastne(s, id, hodnotenie, d.MojaPoznamka, zelany, Cas.Teraz()));
        await ObnovPoctyKnizniceAsync();
        await ZobrazKnizniciuAsync(zachovatVyber: true);
    }

    private async void UpravitTitul_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is not { } d || Kniznica is not { } kniznica)
        {
            return;
        }

        var okno = new UpravTitulOkno(d) { Owner = this };
        if (okno.ShowDialog() != true || okno.Vysledok is not { } v)
        {
            return;
        }

        var id = d.Polozka.Id;
        await kniznica.Zapis.ZapisAsync(s =>
        {
            var teraz = Cas.Teraz();
            foreach (var (pole, hodnota) in v.Zmeny)
            {
                Tituly.UpravPole(s, id, pole, hodnota, teraz);
            }

            foreach (var pole in v.Odomknut)
            {
                Tituly.Odomkni(s, DruhObjektu.Titul, id, pole);
            }

            Tituly.NastavVlastne(s, id, d.Polozka.MojeHodnotenie, v.Poznamka, d.Polozka.Zelany, teraz);
        });
        if (v.Odomknut.Count > 0)
        {
            _ = DoplnKnizniciuAsync();
        }

        await ZobrazKnizniciuAsync(zachovatVyber: true);
    }

    private async void VybratKandidata_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: KartaKandidata karta } && _vybranyTitul is { } id)
        {
            await PotvrdKandidataAsync(id, karta.Kandidat);
        }
    }

    /// <summary>The user picks a candidate (the self-test calls it).</summary>
    internal async Task PotvrdKandidataAsync(long titulId, Kandidat kandidat)
    {
        if (_kniznicaTitulov is not { } spravca)
        {
            return;
        }

        try
        {
            StavUloh.Text = $"Načítavam „{kandidat.Nazov}“…";
            _vybranyTitul = await spravca.PotvrdAsync(titulId, kandidat);
            StavUloh.Text = $"Potvrdené: {kandidat.Nazov}.";
        }
        catch (ChybaZdroja ex)
        {
            MessageBox.Show(this, ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        await ObnovPoctyKnizniceAsync();
        await ZobrazKnizniciuAsync(zachovatVyber: true);
    }

    private async void PotvrditTitul_Click(object sender, RoutedEventArgs e)
    {
        if (_vybranyTitul is { } id && Kniznica is { } kniznica)
        {
            await kniznica.Zapis.ZapisAsync(s => Tituly.Potvrd(s, id));
            await ObnovPoctyKnizniceAsync();
            await ZobrazKnizniciuAsync(zachovatVyber: true);
        }
    }

    /// <summary>The candidate's TMDB page in the user's browser (only https://www.themoviedb.org/ addresses open).</summary>
    private void OdkazKandidata_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri is { Scheme: "https", Host: "www.themoviedb.org" } adresa)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(adresa.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                StavUloh.Text = "Prehliadač sa nepodarilo otvoriť: " + ex.Message;
            }
        }
    }

    private async void HladatKandidatov_Click(object sender, RoutedEventArgs e) => await IdentifikujAsync(DetailHladanie.Text);

    private async void DetailHladanie_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await IdentifikujAsync(DetailHladanie.Text);
        }
    }

    /// <summary>
    /// Identifies the title in the detail by name and year, by director (rezia:…) or by TMDB ID (tmdb:603, or a link to
    /// themoviedb.org) – an ID confirms at once, the rest offers candidates (the self-test calls it).
    /// </summary>
    internal async Task IdentifikujAsync(string text)
    {
        if (_detail is not { } d || _kniznicaTitulov is not { } spravca || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (spravca.Zdroje().Count == 0)
        {
            MessageBox.Show(this, "Žiadny zdroj údajov nie je zapnutý – nastav TMDB alebo HISTRA v Nastaveniach → Zdroje údajov.",
                Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var co = IdentifikaciaTitulu.Citaj(text, d.Polozka.Druh);
        try
        {
            StavUloh.Text = co.TmdbIdentifikator is { } id ? $"Načítavam TMDB {id}…" : co.Rezia is { } r ? $"Hľadám tvorbu: {r}…" : $"Hľadám „{text.Trim()}“…";
            var (kandidati, isty) = await spravca.IdentifikujAsync(d.Polozka.Id, d.Polozka.Druh, co);
            if (isty is not null)
            {
                await PotvrdKandidataAsync(d.Polozka.Id, isty);
                return;
            }

            StavUloh.Text = kandidati.Count == 0
                ? co.Rezia is { } rezia ? $"TMDB nenašla pre réžiu „{rezia}“ nič, čo by sedelo." : $"Pre „{text.Trim()}“ zdroje nič nenašli."
                : $"Nájdené návrhy: {kandidati.Count}.";
        }
        catch (ChybaZdroja ex)
        {
            MessageBox.Show(this, ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        await ZobrazDetailTituluAsync(d.Polozka.Id);
    }

    private async void NieJeTitul_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is not { } d || Kniznica is not { } kniznica)
        {
            return;
        }

        if (MessageBox.Show(this, $"„{d.Polozka.Nazov}“ nie je film ani seriál (napr. domáce video)? Jeho súbory ostanú v katalógu diskov, z knižnice titulov zmiznú.",
                Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var subory = d.Verzie.SelectMany(v => v.Subory).Select(s => s.SuborId).ToList();
        await kniznica.Zapis.ZapisAsync(s => Tituly.MimoKniznice(s, subory, pouzivatel: true));
        _vybranyTitul = null;
        await ObnovPoctyKnizniceAsync();
        await ZobrazKnizniciuAsync();
    }
}
