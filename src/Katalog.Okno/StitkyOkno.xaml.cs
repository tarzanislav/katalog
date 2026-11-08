using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Katalog.Jadro;
using Katalog.Vystupy;
using Microsoft.Win32;

namespace Katalog.Okno;

/// <summary>
/// Labels with QR codes for disks or titles (stage E6): a format of sheet or label printer, the first free label of a
/// sheet already used, copies, a frame and the printer's shift; the preview shows the pages exactly as they print.
/// </summary>
public partial class StitkyOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly IReadOnlyList<UdajeStitku> _polozky;
    private readonly string _nazovSuboru;
    private IReadOnlyList<StranaNakresu> _strany = [];
    private int _strana;
    private bool _plnim = true;

    internal StitkyOkno(Nastavenia nastavenia, string nadpis, IReadOnlyList<UdajeStitku> polozky, string nazovSuboru)
    {
        InitializeComponent();
        _nastavenia = nastavenia;
        _polozky = polozky;
        _nazovSuboru = nazovSuboru;
        Title = nadpis;
        Nadpis.Text = nadpis;

        var moznosti = nastavenia.Stitky;
        Sablona.ItemsSource = Stitky.Sablony;
        Sablona.SelectedItem = Stitky.Sablona(moznosti.Sablona);
        Kopie.ItemsSource = Enumerable.Range(1, 10).ToList();
        Kopie.SelectedItem = Math.Clamp(moznosti.Kopie, 1, 10);
        Qr.IsChecked = moznosti.Qr;
        Ramcek.IsChecked = moznosti.Ramcek;
        PosunX.Text = moznosti.PosunX.ToString("0.#", Format.Slovensky);
        PosunY.Text = moznosti.PosunY.ToString("0.#", Format.Slovensky);
        NaplnZacatOd(1);
        _plnim = false;
        Prepocitaj();
    }

    /// <summary>The pages as they print (the self-test reads them).</summary>
    internal IReadOnlyList<StranaNakresu> Strany => _strany;

    /// <summary>Shows the window with labels for disks or titles.</summary>
    internal static void Ukaz(Window vlastnik, Nastavenia nastavenia, string nadpis, IReadOnlyList<UdajeStitku> polozky, string nazovSuboru)
    {
        if (polozky.Count == 0)
        {
            MessageBox.Show(vlastnik, "Nie je čo tlačiť.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new StitkyOkno(nastavenia, nadpis, polozky, nazovSuboru) { Owner = vlastnik }.ShowDialog();
    }

    private MoznostiStitkov Moznosti() => new()
    {
        Sablona = (Sablona.SelectedItem as SablonaStitkov)?.Kluc ?? Stitky.Predvolena,
        ZacatOd = ZacatOd.SelectedItem is int z ? z : 1,
        Kopie = Kopie.SelectedItem is int k ? k : 1,
        Qr = Qr.IsChecked == true,
        Ramcek = Ramcek.IsChecked == true,
        PosunX = Cislo(PosunX.Text),
        PosunY = Cislo(PosunY.Text),
    };

    private static double Cislo(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cislo) ? Math.Clamp(cislo, -20, 20) : 0;

    private void NaplnZacatOd(int vybrane)
    {
        var sablona = Sablona.SelectedItem as SablonaStitkov ?? Stitky.Sablony[0];
        ZacatOd.ItemsSource = Enumerable.Range(1, sablona.NaStranu).ToList();
        ZacatOd.SelectedItem = Math.Clamp(vybrane, 1, sablona.NaStranu);
        ZacatOd.IsEnabled = sablona.Harok;
    }

    private void Volba_Changed(object sender, RoutedEventArgs e)
    {
        if (_plnim)
        {
            return;
        }

        if (ReferenceEquals(sender, Sablona))
        {
            _plnim = true;
            NaplnZacatOd(1);
            _plnim = false;
        }

        Prepocitaj();
    }

    private void Posun_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_plnim)
        {
            Prepocitaj();
        }
    }

    private void Prepocitaj()
    {
        var moznosti = Moznosti();
        _strany = Stitky.Rozloz(_polozky, moznosti);
        var spolu = _polozky.Count * moznosti.Kopie;
        Popis.Text = $"{Format.Mnozne(spolu, "štítok", "štítky", "štítkov")} · {Format.Mnozne(_strany.Count, "strana", "strany", "strán")}";
        _strana = Math.Clamp(_strana, 0, Math.Max(0, _strany.Count - 1));
        UkazStranu();
    }

    private void UkazStranu()
    {
        if (_strany.Count == 0)
        {
            Nahlad.Source = null;
            CisloStrany.Text = "";
            return;
        }

        // the preview at 1.5× screen resolution, so small text stays readable when the window is large
        Nahlad.Source = KreslenieStran.Nahlad(_strany[_strana], 144);
        CisloStrany.Text = $"Strana {_strana + 1} z {_strany.Count}";
        Predosla.IsEnabled = _strana > 0;
        Dalsia.IsEnabled = _strana < _strany.Count - 1;
    }

    private void Predosla_Click(object sender, RoutedEventArgs e)
    {
        _strana--;
        UkazStranu();
    }

    private void Dalsia_Click(object sender, RoutedEventArgs e)
    {
        _strana++;
        UkazStranu();
    }

    private void UlozMoznosti()
    {
        // the first free label belongs to one sheet: next time it starts at 1 again
        _nastavenia.Stitky = Moznosti() with { ZacatOd = 1 };
        try
        {
            _nastavenia.Uloz();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Tlacit_Click(object sender, RoutedEventArgs e)
    {
        UlozMoznosti();
        if (TlacStran.Tlac(_strany, Title))
        {
            UkazSpravu("Štítky sú poslané na tlačiareň.");
        }
    }

    private void UlozitPdf_Click(object sender, RoutedEventArgs e)
    {
        UlozMoznosti();
        var dialog = new SaveFileDialog
        {
            Title = "Uložiť štítky ako PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = _nazovSuboru + ".pdf",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            KreslenieStran.UlozPdf(_strany, dialog.FileName, Title);
            UkazSpravu($"Uložené: {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "PDF sa nepodarilo uložiť: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void UkazSpravu(string text)
    {
        Sprava.Text = text;
        Sprava.Visibility = Visibility.Visible;
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e)
    {
        UlozMoznosti();
        Close();
    }
}

/// <summary>Printing drawn pages on a printer the user chooses (the paper turns for wide pages, a label printer gets the label's size).</summary>
internal static class TlacStran
{
    public static bool Tlac(IReadOnlyList<StranaNakresu> strany, string nazov)
    {
        if (strany.Count == 0)
        {
            return false;
        }

        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        var prva = strany[0];
        try
        {
            var a4 = Math.Abs(Math.Min(prva.Sirka, prva.Vyska) - Nakres.SirkaA4) < 1 && Math.Abs(Math.Max(prva.Sirka, prva.Vyska) - Nakres.VyskaA4) < 1;
            if (a4)
            {
                dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                dialog.PrintTicket.PageOrientation = prva.Sirka > prva.Vyska ? PageOrientation.Landscape : PageOrientation.Portrait;
            }
            else
            {
                dialog.PrintTicket.PageMediaSize = new PageMediaSize(prva.Sirka * KreslenieStran.DipNaMm, prva.Vyska * KreslenieStran.DipNaMm);
            }
        }
        catch (Exception ex) when (ex is PrintQueueException or InvalidOperationException)
        {
            // no printer installed: the dialog says so itself
        }

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        dialog.PrintDocument(new StrankovacNakresu(strany), nazov);
        return true;
    }
}
