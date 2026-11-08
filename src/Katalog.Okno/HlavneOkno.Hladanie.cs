using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// Searching (stage E2): the search box runs the query language while the user types, the results replace the middle of
/// the window, the filters edit the text of the query, saved searches keep it under a name.
/// </summary>
public partial class HlavneOkno
{
    private readonly DispatcherTimer _odkladHladania = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private int _cisloHladania;
    private UIElement? _stredPredHladanim;

    private void InicializujHladanie() =>
        _odkladHladania.Tick += async (_, _) =>
        {
            _odkladHladania.Stop();
            await HladajAsync();
        };

    private void PoleHladania_TextChanged(object sender, TextChangedEventArgs e)
    {
        NapovedaHladania.Visibility = PoleHladania.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _odkladHladania.Stop();
        if (PoleHladania.Text.Trim().Length == 0)
        {
            _cisloHladania++;
            ZavriVysledky();
            return;
        }

        // waits for a pause in typing, so each key does not start a search
        _odkladHladania.Start();
    }

    private async void PoleHladania_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _odkladHladania.Stop();
                await HladajAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                PoleHladania.Text = "";
                break;
            case Key.Down when PanelVysledkov.Visibility == Visibility.Visible && ZoznamVysledkov.Items.Count > 0:
                e.Handled = true;
                ZoznamVysledkov.SelectedIndex = Math.Max(0, ZoznamVysledkov.SelectedIndex);
                (ZoznamVysledkov.ItemContainerGenerator.ContainerFromIndex(ZoznamVysledkov.SelectedIndex) as ListViewItem)?.Focus();
                break;
        }
    }

    /// <summary>Runs the query in the search box (off the window's thread); the self-test calls it too.</summary>
    internal async Task<VysledkyHladania?> HladajAsync()
    {
        _odkladHladania.Stop();
        if (Kniznica is not { } kniznica)
        {
            return null;
        }

        var dotaz = Dotaz.Citaj(PoleHladania.Text);
        if (dotaz.JePrazdny)
        {
            ZavriVysledky();
            return null;
        }

        var cislo = ++_cisloHladania;
        var kontext = KontextHladania.Teraz(_stav?.Pripojene.Keys.ToList() ?? []);
        VysledkyHladania vysledky;
        IReadOnlyDictionary<long, string> cesty;
        Dictionary<long, Disk> disky;
        try
        {
            (vysledky, cesty, disky) = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                var v = Hladanie.Najdi(c, dotaz, kontext);
                return (v, Hladanie.Cesty(c, v.Polozky.Select(p => p.PriecinokId)), KatalogyADisky.Disky(c).ToDictionary(d => d.Id));
            });
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException or InvalidOperationException)
        {
            if (cislo == _cisloHladania && Kniznica is not null)
            {
                UkazStred(PanelVysledkov);
                PopisVysledkov.Text = "Hľadanie sa nepodarilo: " + ex.Message;
            }

            return null;
        }

        // a newer search (or a closed library) makes this one stale
        if (cislo != _cisloHladania || Kniznica is null)
        {
            return null;
        }

        ZobrazVysledky(dotaz, vysledky, cesty, disky);
        return vysledky;
    }

    private void ZobrazVysledky(Dotaz dotaz, VysledkyHladania vysledky, IReadOnlyDictionary<long, string> cesty, Dictionary<long, Disk> disky)
    {
        if (PanelVysledkov.Visibility != Visibility.Visible)
        {
            _stredPredHladanim = new UIElement[] { PanelSuborov, PanelUlozenych, PanelKniznice, PanelPrehladu, PanelStatistik }.FirstOrDefault(p => p.Visibility == Visibility.Visible) ?? PanelPrazdny;
            UkazStred(PanelVysledkov);
        }

        var polozky = vysledky.Polozky
            .Where(p => disky.ContainsKey(p.DiskId))
            .Select(p => new PolozkaVysledku(p, disky[p.DiskId], CestaRodica(p, cesty), _stav?.Pripojene.ContainsKey(p.DiskId) == true))
            .ToList();
        ZoznamVysledkov.ItemsSource = polozky;
        ZoznamVysledkov.Visibility = polozky.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NicSaNenaslo.Visibility = polozky.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NicSaNenaslo.Text = dotaz.Zmazane == RezimZmazanych.Nie
            ? "Nič sa nenašlo. Skús menej slov alebo iný filter. Súbory, ktoré z diskov zmizli, nájdeš s filtrom zmazane:aj."
            : "Nič sa nenašlo. Skús menej slov alebo iný filter.";
        PopisVysledkov.Text = polozky.Count == 0 ? ""
            : vysledky.Viac ? $"Zobrazených prvých {Format.Pocet(polozky.Count)} – spresni hľadanie ďalším slovom alebo filtrom"
            : $"{Format.Mnozne(polozky.Count, "výsledok", "výsledky", "výsledkov")} · {vysledky.Trvanie.TotalMilliseconds:0} ms";
        UpozorneniaHladania.Text = string.Join("\n", dotaz.Upozornenia);
        UpozorneniaHladania.Visibility = dotaz.Upozornenia.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TlacidloPrehrat.IsEnabled = false;
        ZobrazDetail(null);
    }

    /// <summary>The folder a result lies in ("" for the root of its disk).</summary>
    private static string CestaRodica(NajdenaPolozka polozka, IReadOnlyDictionary<long, string> cesty)
    {
        var cesta = cesty.TryGetValue(polozka.PriecinokId, out var c) ? c : "";
        if (polozka.Druh != DruhHladania.Priecinok)
        {
            return cesta;
        }

        var lomka = cesta.LastIndexOf('/');
        return lomka < 0 ? "" : cesta[..lomka];
    }

    private void ZavriVysledky()
    {
        if (PanelVysledkov.Visibility != Visibility.Visible)
        {
            return;
        }

        UkazStred(_stredPredHladanim ?? PanelPrazdny);
        ZoznamVysledkov.ItemsSource = null;
        if (_zobrazenyDisk is { } disk && _zobrazenyPriecinok is { } priecinok && PanelSuborov.Visibility == Visibility.Visible)
        {
            ZobrazPriecinok(disk, priecinok);
        }
        else
        {
            ZobrazDetail(null);
        }
    }

    /// <summary>Shows one of the panels of the middle of the window and hides the others.</summary>
    private void UkazStred(UIElement panel)
    {
        foreach (var p in new UIElement[] { PanelPrazdny, PanelSuborov, PanelVysledkov, PanelUlozenych, PanelKniznice, PanelPrehladu, PanelStatistik, PanelVypoziciek })
        {
            p.Visibility = ReferenceEquals(p, panel) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ZavrietHladanie_Click(object sender, RoutedEventArgs e) => PoleHladania.Text = "";

    private void ZoznamVysledkov_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TlacidloPrehrat.IsEnabled = VybraneCiele().Any(c => c.DaSaPrehrat);
        if (ZoznamVysledkov.SelectedItem is PolozkaVysledku polozka)
        {
            ZobrazDetail(polozka.Disk, polozka.AkoPolozkaZoznamu, polozka.CestaPriecinka);
        }
    }

    private async void ZoznamVysledkov_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject zdroj && ItemsControl.ContainerFromElement(ZoznamVysledkov, zdroj) is ListViewItem)
        {
            await OtvorVybraneAsync();
        }
    }

    private async void ZoznamVysledkov_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OtvorVybraneAsync();
        }
    }

    /// <summary>A folder opens in the catalogue, videos play, another file opens in its program.</summary>
    private async Task OtvorVybraneAsync()
    {
        var ciele = VybraneCiele();
        if (ciele.Count == 1 && ciele[0].JePriecinok && PanelVysledkov.Visibility == Visibility.Visible)
        {
            UkazatVKatalogu_Click(this, new RoutedEventArgs());
        }
        else if (ciele.Count > 0 && ciele.All(c => c.DaSaPrehrat))
        {
            await PrehrajAsync(ciele);
        }
        else if (ciele.Count == 1)
        {
            OtvorSubor(ciele[0]);
        }
    }

    private void UkazatVKatalogu_Click(object sender, RoutedEventArgs e)
    {
        if (ZoznamVysledkov.SelectedItem is not PolozkaVysledku polozka)
        {
            return;
        }

        ZobrazPriecinok(polozka.Disk.Id, polozka.Polozka.PriecinokId);
        if (polozka.JePriecinok)
        {
            return;
        }

        var subor = ZoznamSuborov.Items.OfType<PolozkaZoznamu>().FirstOrDefault(p => !p.JePriecinok && p.Id == polozka.Polozka.Id);
        if (subor is not null)
        {
            ZoznamSuborov.SelectedItem = subor;
            ZoznamSuborov.ScrollIntoView(subor);
        }
    }

    private void ZoznamVysledkov_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // the name gets the larger part of what the disk and size leave; the folder only when there is room for both
        const double disk = 70, velkost = 90, okraj = 36;
        var zvysok = Math.Max(160, ZoznamVysledkov.ActualWidth - okraj - disk - velkost);
        var cesta = zvysok >= 340 ? Math.Floor(zvysok * 0.4) : 0;
        VysledokDisk.Width = disk;
        VysledokVelkost.Width = velkost;
        VysledokCesta.Width = cesta;
        VysledokNazov.Width = zvysok - cesta;
    }

    // ------------------------------------------------------------------ filters

    private void Filtre_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        IReadOnlyList<KatalogKniznice> katalogy;
        IReadOnlyList<Disk> disky;
        using (var c = kniznica.OtvorCitanie())
        {
            katalogy = KatalogyADisky.Katalogy(c);
            disky = KatalogyADisky.Disky(c);
        }

        var okno = new FiltreOkno(PoleHladania.Text, katalogy, disky) { Owner = this };
        if (okno.ShowDialog() == true && okno.Vysledok is { } text)
        {
            PoleHladania.Text = text;
            PoleHladania.CaretIndex = text.Length;
            PoleHladania.Focus();
        }
    }

    // ------------------------------------------------------------------ saved searches

    private void ZobrazUlozene()
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        IReadOnlyList<UlozeneHladanie> ulozene;
        using (var c = kniznica.OtvorCitanie())
        {
            ulozene = UlozeneHladania.Zoznam(c);
        }

        _zobrazenyDisk = null;
        _zobrazenyPriecinok = null;
        ZoznamUlozenych.ItemsSource = ulozene;
        ZiadneUlozene.Visibility = ulozene.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ZoznamUlozenychZmeneny();
        UkazStred(PanelUlozenych);
        ZobrazDetail(null);
    }

    private void ZoznamUlozenych_SelectionChanged(object sender, SelectionChangedEventArgs e) => ZoznamUlozenychZmeneny();

    private void ZoznamUlozenychZmeneny()
    {
        var vybrane = ZoznamUlozenych.SelectedItem is UlozeneHladanie;
        TlacidloSpustitUlozene.IsEnabled = vybrane;
        TlacidloPremenovatUlozene.IsEnabled = vybrane;
        TlacidloZmazatUlozene.IsEnabled = vybrane;
    }

    private async void UlozitHladanie_Click(object sender, RoutedEventArgs e)
    {
        var text = PoleHladania.Text.Trim();
        if (Kniznica is not { } kniznica || text.Length == 0)
        {
            return;
        }

        var predvolene = text.Length > 40 ? text[..40] : text;
        if (VstupOkno.Opytaj(this, "Uložiť hľadanie", "Názov hľadania (napr. Filmy v 4K, Na zálohu):", predvolene) is not { } nazov)
        {
            return;
        }

        await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Uloz(s, nazov, text));
        StavUloh.Text = $"Hľadanie „{nazov}“ je uložené (Zbierky → Uložené hľadania).";
    }

    private async void SpustitUlozene_Click(object sender, RoutedEventArgs e)
    {
        if (ZoznamUlozenych.SelectedItem is UlozeneHladanie ulozene)
        {
            PoleHladania.Text = ulozene.Dotaz;
            _odkladHladania.Stop();
            await HladajAsync();
        }
    }

    private async void PremenovatUlozene_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is { } kniznica && ZoznamUlozenych.SelectedItem is UlozeneHladanie ulozene &&
            VstupOkno.Opytaj(this, "Premenovať hľadanie", "Nový názov hľadania:", ulozene.Nazov) is { } nazov)
        {
            try
            {
                await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Premenuj(s, ulozene.Id, nazov));
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                MessageBox.Show(this, $"Hľadanie s názvom „{nazov}“ už je.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            ZobrazUlozene();
        }
    }

    private async void ZmazatUlozene_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is { } kniznica && ZoznamUlozenych.SelectedItem is UlozeneHladanie ulozene &&
            MessageBox.Show(this, $"Zmazať uložené hľadanie „{ulozene.Nazov}“?", Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await kniznica.Zapis.ZapisAsync(s => UlozeneHladania.Zmaz(s, ulozene.Id));
            ZobrazUlozene();
        }
    }

    // ------------------------------------------------------------------ paths

    private void KopirovatCestu_Click(object sender, RoutedEventArgs e)
    {
        var ciele = VybraneCiele();
        if (Kniznica is not { } kniznica || ciele.Count == 0)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        var riadky = ciele.Select(ciel =>
        {
            var vnutri = KatalogyADisky.Cesta(c, ciel.PriecinokId);
            if (ciel.Subor is { } subor)
            {
                vnutri = vnutri.Length == 0 ? subor : vnutri + "/" + subor;
            }

            if (_stav?.Cesta(ciel.DiskId) is { } koren)
            {
                return PlnaCesta(koren, vnutri);
            }

            var disk = KatalogyADisky.NajdiDisk(c, ciel.DiskId);
            return $"[disk {disk?.CisloText} {disk?.Nazov}]\\{vnutri.Replace('/', '\\')}";
        });
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, riadky));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // the clipboard is busy in another program; the user simply tries again
        }
    }

    private void OtvoritVysledokVPrieskumnikovi_Click(object sender, RoutedEventArgs e)
    {
        if (VybraneCiele() is not [var ciel, ..] || Kniznica is not { } kniznica)
        {
            return;
        }

        if (_stav?.Cesta(ciel.DiskId) is not { } koren)
        {
            MessageBox.Show(this, PripojDisk(ciel.DiskId), Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string priecinok;
        using (var c = kniznica.OtvorCitanie())
        {
            priecinok = PlnaCesta(koren, KatalogyADisky.Cesta(c, ciel.PriecinokId));
        }

        var argument = ciel.Subor is { } subor ? "/select,\"" + Path.Combine(priecinok, subor) + "\"" : "\"" + priecinok + "\"";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
    }
}
