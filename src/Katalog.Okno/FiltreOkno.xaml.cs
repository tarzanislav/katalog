using System.Windows;
using System.Windows.Controls;
using Katalog.Jadro.Dotazy;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// Filters of a search. They are not a second way to search: they read and write fields of the query in the search box
/// (decision A4), so what they set can be seen, edited by hand and saved.
/// </summary>
public partial class FiltreOkno : Window
{
    private readonly string _povodny;

    internal FiltreOkno(string text, IReadOnlyList<KatalogKniznice> katalogy, IReadOnlyList<Disk> disky)
    {
        InitializeComponent();
        _povodny = text;
        ZoznamDiskov.ItemsSource = new[] { new Volba("Všetky disky", "") }
            .Concat(disky.OrderBy(d => d.Cislo).Select(d => new Volba($"{d.CisloText} – {d.Nazov}", d.Cislo.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .ToList();
        ZoznamKatalogov.ItemsSource = new[] { new Volba("Všetky katalógy", "") }.Concat(katalogy.Select(k => new Volba(k.Nazov, k.Nazov))).ToList();
        Nacitaj(text);
    }

    /// <summary>The search text with the chosen filters (null when the user cancelled).</summary>
    internal string? Vysledok { get; private set; }

    private void Nacitaj(string text)
    {
        Vyber(VolbaTypu, Dotaz.HodnotaPola(text, PoleDotazu.Typ));
        PolePripony.Text = Dotaz.HodnotaPola(text, PoleDotazu.Pripona) ?? "";
        var (od, doo) = RozdelVelkost(Dotaz.HodnotaPola(text, PoleDotazu.Velkost));
        VelkostOd.Text = od;
        VelkostDo.Text = doo;
        PoleZmenene.Text = Dotaz.HodnotaPola(text, PoleDotazu.Zmenene) ?? "";
        Vyber(VolbaPridania, Dotaz.HodnotaPola(text, PoleDotazu.Pridane));
        Vyber(VolbaZmazanych, Dotaz.HodnotaPola(text, PoleDotazu.Zmazane));
        var disk = Dotaz.HodnotaPola(text, PoleDotazu.Disk);
        ZoznamDiskov.SelectedItem = ZoznamDiskov.Items.OfType<Volba>().FirstOrDefault(v => CisloDisku(v.Hodnota) == CisloDisku(disk ?? "")) ?? ZoznamDiskov.Items[0];
        var katalog = Dotaz.HodnotaPola(text, PoleDotazu.Katalog);
        ZoznamKatalogov.SelectedItem = ZoznamKatalogov.Items.OfType<Volba>().FirstOrDefault(v => string.Equals(v.Hodnota, katalog ?? "", StringComparison.OrdinalIgnoreCase)) ?? ZoznamKatalogov.Items[0];
        LenPripojene.IsChecked = Dotaz.HodnotaPola(text, PoleDotazu.Pripojene) is { } p && Dotaz.Citaj("pripojene:" + p).Podmienky.OfType<Pripojene>().Any(x => x.Hodnota);
    }

    private static void Vyber(ComboBox zoznam, string? hodnota)
    {
        var h = hodnota ?? "";
        zoznam.SelectedItem = zoznam.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, h, StringComparison.OrdinalIgnoreCase))
                              ?? zoznam.Items[0];
    }

    /// <summary>"4GB..20GB" → ("4GB", "20GB"); "&gt;=4GB" → ("4GB", ""); "&lt;=1GB" → ("", "1GB").</summary>
    private static (string Od, string Do) RozdelVelkost(string? hodnota)
    {
        if (string.IsNullOrEmpty(hodnota))
        {
            return ("", "");
        }

        if (hodnota.Contains("..", StringComparison.Ordinal))
        {
            var casti = hodnota.Split("..", 2);
            return (casti[0], casti[1]);
        }

        return hodnota.StartsWith('<') ? ("", hodnota.TrimStart('<', '=')) : (hodnota.TrimStart('>', '='), "");
    }

    private static int? CisloDisku(string text) =>
        int.TryParse(text.TrimStart('D', 'd'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : null;

    private void Vymazat_Click(object sender, RoutedEventArgs e)
    {
        VolbaTypu.SelectedIndex = 0;
        PolePripony.Text = "";
        VelkostOd.Text = "";
        VelkostDo.Text = "";
        PoleZmenene.Text = "";
        VolbaPridania.SelectedIndex = 0;
        ZoznamDiskov.SelectedIndex = 0;
        ZoznamKatalogov.SelectedIndex = 0;
        VolbaZmazanych.SelectedIndex = 0;
        LenPripojene.IsChecked = false;
    }

    private void Pouzit_Click(object sender, RoutedEventArgs e)
    {
        var od = VelkostOd.Text.Trim();
        var doo = VelkostDo.Text.Trim();
        var velkost = (od.Length > 0, doo.Length > 0) switch
        {
            (true, true) => od + ".." + doo,
            (true, false) => ">=" + od,
            (false, true) => "<=" + doo,
            _ => null,
        };

        var text = _povodny;
        text = Dotaz.NastavPole(text, PoleDotazu.Typ, Tag(VolbaTypu));
        text = Dotaz.NastavPole(text, PoleDotazu.Pripona, PolePripony.Text.Replace(" ", "", StringComparison.Ordinal));
        text = Dotaz.NastavPole(text, PoleDotazu.Velkost, velkost);
        text = Dotaz.NastavPole(text, PoleDotazu.Zmenene, PoleZmenene.Text.Replace(" ", "", StringComparison.Ordinal));
        text = Dotaz.NastavPole(text, PoleDotazu.Pridane, Tag(VolbaPridania));
        text = Dotaz.NastavPole(text, PoleDotazu.Disk, (ZoznamDiskov.SelectedItem as Volba)?.Hodnota);
        text = Dotaz.NastavPole(text, PoleDotazu.Katalog, (ZoznamKatalogov.SelectedItem as Volba)?.Hodnota);
        text = Dotaz.NastavPole(text, PoleDotazu.Pripojene, LenPripojene.IsChecked == true ? "ano" : null);
        text = Dotaz.NastavPole(text, PoleDotazu.Zmazane, Tag(VolbaZmazanych));

        // a value the query language does not understand is shown here, before it leaves the window
        var upozornenia = Dotaz.Citaj(text).Upozornenia;
        if (upozornenia.Count > 0 && Dotaz.Citaj(_povodny).Upozornenia.Count < upozornenia.Count)
        {
            MessageBox.Show(this, string.Join("\n", upozornenia), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Vysledok = text;
        DialogResult = true;
    }

    private static string? Tag(ComboBox zoznam) => (zoznam.SelectedItem as ComboBoxItem)?.Tag as string;

    private sealed record Volba(string Popis, string Hodnota);
}
