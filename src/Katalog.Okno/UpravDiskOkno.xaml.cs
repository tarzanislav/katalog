using System.Globalization;
using System.Windows;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>What the user may change about a disk: name, label number, catalogue, where it lies, a note.</summary>
public partial class UpravDiskOkno : Window
{
    private readonly IReadOnlySet<int> _obsadeneCisla;

    internal UpravDiskOkno(Disk disk, IReadOnlyList<KatalogKniznice> katalogy, IReadOnlySet<int> obsadeneCisla)
    {
        InitializeComponent();
        _obsadeneCisla = obsadeneCisla;
        Title = $"Upraviť disk {disk.CisloText}";
        Nazov.Text = disk.Nazov;
        Cislo.Text = disk.Cislo.ToString(CultureInfo.InvariantCulture);
        Katalogy.ItemsSource = katalogy;
        Katalogy.SelectedItem = katalogy.FirstOrDefault(k => k.Id == disk.KatalogId);
        Umiestnenie.Text = disk.Umiestnenie ?? "";
        Nahlady.IsChecked = disk.Nahlady;
        Poznamka.Text = disk.Poznamka ?? "";
        PovodneCislo = disk.Cislo;
    }

    internal int PovodneCislo { get; }

    internal (string Nazov, int Cislo, long KatalogId, string? Umiestnenie, string? Poznamka, bool Nahlady)? Vysledok { get; private set; }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        if (Nazov.Text.Trim().Length == 0)
        {
            Zle("Napíš názov disku.");
            return;
        }

        if (!int.TryParse(Cislo.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var cislo) || cislo <= 0)
        {
            Zle("Číslo disku je kladné celé číslo, napríklad 3.");
            return;
        }

        if (cislo != PovodneCislo && _obsadeneCisla.Contains(cislo))
        {
            Zle($"Číslo {cislo:D3} už má iný disk.");
            return;
        }

        if (Katalogy.SelectedItem is not KatalogKniznice katalog)
        {
            Zle("Vyber katalóg.");
            return;
        }

        Vysledok = (Nazov.Text.Trim(), cislo, katalog.Id, Umiestnenie.Text, Poznamka.Text, Nahlady.IsChecked == true);
        DialogResult = true;
    }

    private void Zle(string sprava)
    {
        Chyba.Text = sprava;
        Chyba.Visibility = Visibility.Visible;
    }
}
