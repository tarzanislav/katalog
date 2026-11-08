using System.Diagnostics;
using System.IO;
using System.Windows;
using Katalog.Vystupy;

namespace Katalog.Okno;

/// <summary>Making the private web catalogue (stage E9): the folder, the name on the page and the password (never stored).</summary>
public partial class WebKatalogOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly Func<string, string, string, bool, bool, IProgress<string>, Task<VysledokWebu>> _vytvor;

    /// <param name="vytvor">Makes the catalogue: folder, name, password, posters, paths, progress.</param>
    internal WebKatalogOkno(Nastavenia nastavenia, Func<string, string, string, bool, bool, IProgress<string>, Task<VysledokWebu>> vytvor)
    {
        InitializeComponent();
        _nastavenia = nastavenia;
        _vytvor = vytvor;
        Priecinok.Text = nastavenia.PriecinokWebu;
        Nazov.Text = nastavenia.NazovWebu.Length > 0 ? nastavenia.NazovWebu : "Moja videotéka";
        Plagaty.IsChecked = nastavenia.PlagatyNaWebe;
        Cesty.IsChecked = nastavenia.CestyNaWebe;
    }

    private void Vybrat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Priečinok webového katalógu (prázdny, alebo kam sa uložil predtým)" };
        if (dialog.ShowDialog(this) == true)
        {
            Priecinok.Text = dialog.FolderName;
        }
    }

    private async void Vytvorit_Click(object sender, RoutedEventArgs e)
    {
        string? chyba = Priecinok.Text.Length == 0 ? "Vyber priečinok."
            : Nazov.Text.Trim().Length == 0 ? "Napíš názov katalógu."
            : Heslo.Password.Length < WebovyKatalog.NajkratsieHeslo ? $"Heslo musí mať aspoň {WebovyKatalog.NajkratsieHeslo} znakov."
            : Heslo.Password != Heslo2.Password ? "Heslá sa nezhodujú."
            : null;
        if (chyba is not null)
        {
            Ukaz(chyba, zle: true);
            return;
        }

        _nastavenia.PriecinokWebu = Priecinok.Text;
        _nastavenia.NazovWebu = Nazov.Text.Trim();
        _nastavenia.PlagatyNaWebe = Plagaty.IsChecked == true;
        _nastavenia.CestyNaWebe = Cesty.IsChecked == true;
        try
        {
            _nastavenia.Uloz();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        TlacidloVytvorit.IsEnabled = false;
        TlacidloOtvorit.Visibility = Visibility.Collapsed;
        try
        {
            var vysledok = await _vytvor(Priecinok.Text, Nazov.Text.Trim(), Heslo.Password, Plagaty.IsChecked == true, Cesty.IsChecked == true,
                new Progress<string>(t => Ukaz(t, zle: false)));
            Ukaz($"Hotovo: {Format.Mnozne(vysledok.Titulov, "titul", "tituly", "titulov")}, {Format.Mnozne(vysledok.Plagatov, "plagát", "plagáty", "plagátov")}, " +
                 $"{Format.Velkost(vysledok.Bajtov)}. Otvor index.html v prehliadači a zadaj heslo.", zle: false);
            TlacidloOtvorit.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Ukaz(ex.Message, zle: true);
        }
        finally
        {
            TlacidloVytvorit.IsEnabled = true;
            Heslo.Clear();
            Heslo2.Clear();
        }
    }

    private void Otvorit_Click(object sender, RoutedEventArgs e)
    {
        var stranka = Path.Combine(Priecinok.Text, "index.html");
        try
        {
            Process.Start(new ProcessStartInfo(stranka) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Ukaz("Stránku sa nepodarilo otvoriť: " + ex.Message, zle: true);
        }
    }

    private void Ukaz(string text, bool zle)
    {
        Sprava.Text = text;
        if (zle)
        {
            Sprava.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
        }
        else
        {
            Sprava.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
        }

        Sprava.Visibility = Visibility.Visible;
    }
}
