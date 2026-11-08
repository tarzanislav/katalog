using System.Globalization;
using System.Windows;
using Katalog.Data;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>What the user changed about a title: fields to lock with their new values, fields to give back to the sources, the note.</summary>
internal sealed record UpravaTitulu(IReadOnlyList<(string Pole, object? Hodnota)> Zmeny, IReadOnlyList<string> Odomknut, string? Poznamka);

/// <summary>Editing a title by hand (A5): a changed field is locked, no source overwrites it; it can be given back.</summary>
public partial class UpravTitulOkno : Window
{
    private readonly DetailTitulu _detail;
    private readonly string? _dej;

    internal UpravTitulOkno(DetailTitulu detail)
    {
        InitializeComponent();
        _detail = detail;
        _dej = detail.Dej;
        Title = "Upraviť – " + detail.Polozka.Nazov;
        Nazov.Text = detail.Polozka.Nazov;
        Original.Text = detail.Polozka.Original ?? "";
        Rok.Text = detail.Polozka.Rok?.ToString(CultureInfo.InvariantCulture) ?? "";
        Dej.Text = detail.Dej ?? "";
        Poznamka.Text = detail.MojaPoznamka ?? "";
        Odomknut.Visibility = detail.Povod.Values.Any(p => p.Zamknute) ? Visibility.Visible : Visibility.Collapsed;
    }

    internal UpravaTitulu? Vysledok { get; private set; }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        var nazov = Nazov.Text.Trim();
        if (nazov.Length == 0)
        {
            ZobrazChybu("Názov nemôže byť prázdny.");
            return;
        }

        int? rok = null;
        if (Rok.Text.Trim().Length > 0)
        {
            if (!int.TryParse(Rok.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r is < 1870 or > 2200)
            {
                ZobrazChybu("Rok musí byť číslo od 1870 do 2200.");
                return;
            }

            rok = r;
        }

        var zmeny = new List<(string, object?)>();
        if (nazov != _detail.Polozka.Nazov)
        {
            zmeny.Add(("nazov", nazov));
        }

        var original = Original.Text.Trim();
        if (original != (_detail.Polozka.Original ?? ""))
        {
            zmeny.Add(("original", original.Length == 0 ? null : original));
        }

        if (rok != _detail.Polozka.Rok)
        {
            zmeny.Add(("rok", rok));
        }

        var dej = Dej.Text.Trim();
        if (dej != (_dej ?? ""))
        {
            zmeny.Add(("dej", dej.Length == 0 ? null : dej));
        }

        var odomknut = Odomknut.IsChecked == true
            ? _detail.Povod.Where(p => p.Value.Zamknute && p.Value.Zdroj == ZdrojUdajov.Pouzivatel).Select(p => p.Key).ToList()
            : [];
        var poznamka = Poznamka.Text.Trim();
        Vysledok = new UpravaTitulu(zmeny, odomknut, poznamka.Length == 0 ? null : poznamka);
        DialogResult = true;
    }

    private void ZobrazChybu(string text)
    {
        Chyba.Text = text;
        Chyba.Visibility = Visibility.Visible;
    }
}
