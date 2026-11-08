using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>A new playlist or changes to one: its name, kind, query and (for a random one) how many titles it draws.</summary>
public partial class PlaylistOkno : Window
{
    private readonly Func<string, int>? _pocitadlo;
    private readonly DispatcherTimer _odklad = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <param name="pocitadlo">How many titles a query matches (shown under it as the user types).</param>
    private PlaylistOkno(string nazov, DruhPlaylistu druh, string? dotaz, int? pocet, bool novy, Func<string, int>? pocitadlo)
    {
        InitializeComponent();
        _pocitadlo = pocitadlo;
        Title = novy ? "Nový playlist" : "Upraviť playlist";
        Nazov.Text = nazov;
        Dotaz.Text = dotaz ?? "";
        Pocet.ItemsSource = new[] { 5, 10, 15, 20, 30, 50 };
        Pocet.Text = (pocet ?? Data.Playlisty.PredvolenyPocet).ToString(CultureInfo.InvariantCulture);
        (druh switch { DruhPlaylistu.Inteligentny => DruhInteligentny, DruhPlaylistu.Nahodny => DruhNahodny, _ => DruhRucny }).IsChecked = true;
        // a playlist keeps its kind (its titles would mean something else)
        DruhRucny.IsEnabled = DruhInteligentny.IsEnabled = DruhNahodny.IsEnabled = novy;
        _odklad.Tick += (_, _) =>
        {
            _odklad.Stop();
            Spocitaj();
        };
        Loaded += (_, _) =>
        {
            Nazov.Focus();
            Nazov.SelectAll();
            Spocitaj();
        };
    }

    public DruhPlaylistu Druh => DruhNahodny.IsChecked == true ? DruhPlaylistu.Nahodny : DruhInteligentny.IsChecked == true ? DruhPlaylistu.Inteligentny : DruhPlaylistu.Rucny;

    /// <summary>The user's answer, or null when cancelled.</summary>
    public static (string Nazov, DruhPlaylistu Druh, string? Dotaz, int? Pocet)? Opytaj(
        Window vlastnik, string nazov, DruhPlaylistu druh, string? dotaz, int? pocet, bool novy, Func<string, int>? pocitadlo)
    {
        var okno = new PlaylistOkno(nazov, druh, dotaz, pocet, novy, pocitadlo) { Owner = vlastnik };
        if (okno.ShowDialog() != true)
        {
            return null;
        }

        var d = okno.Druh;
        return (okno.Nazov.Text.Trim(), d, d == DruhPlaylistu.Rucny ? null : okno.Dotaz.Text.Trim(),
            d == DruhPlaylistu.Nahodny ? okno.CitajPocet() : null);
    }

    private int? CitajPocet() =>
        int.TryParse(Pocet.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 1000 ? p : null;

    private void Druh_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelDotazu is null || PanelPoctu is null)
        {
            return;
        }

        PanelDotazu.Visibility = Druh == DruhPlaylistu.Rucny ? Visibility.Collapsed : Visibility.Visible;
        PanelPoctu.Visibility = Druh == DruhPlaylistu.Nahodny ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Dotaz_TextChanged(object sender, TextChangedEventArgs e)
    {
        _odklad.Stop();
        _odklad.Start();
    }

    private void Spocitaj()
    {
        if (_pocitadlo is null || Druh == DruhPlaylistu.Rucny)
        {
            Zodpoveda.Text = "";
            return;
        }

        var upozornenia = Jadro.Dotazy.Dotaz.Citaj(Dotaz.Text).Upozornenia;
        Zodpoveda.Text = (upozornenia.Count > 0 ? string.Join(" ", upozornenia) + " " : "") +
                         "Zodpovedá: " + Format.Mnozne(_pocitadlo(Dotaz.Text), "titul, ktorý máš", "tituly, ktoré máš", "titulov, ktoré máš") + ".";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? chyba = Nazov.Text.Trim().Length == 0 ? "Napíš názov playlistu."
            : Druh == DruhPlaylistu.Nahodny && CitajPocet() is null ? "Počet titulov je číslo od 1 do 1000."
            : null;
        if (chyba is not null)
        {
            Chyba.Text = chyba;
            Chyba.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
