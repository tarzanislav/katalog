using System.Windows;

namespace Katalog.Okno;

/// <summary>A small question with one line of text (new catalogue, renaming).</summary>
public partial class VstupOkno : Window
{
    private VstupOkno(string nadpis, string otazka, string predvolene)
    {
        InitializeComponent();
        Title = nadpis;
        Otazka.Text = otazka;
        Text.Text = predvolene;
        Loaded += (_, _) =>
        {
            Text.Focus();
            Text.SelectAll();
        };
    }

    /// <summary>The typed text (trimmed), or null when the user cancelled.</summary>
    public static string? Opytaj(Window vlastnik, string nadpis, string otazka, string predvolene = "")
    {
        var okno = new VstupOkno(nadpis, otazka, predvolene) { Owner = vlastnik };
        return okno.ShowDialog() == true ? okno.Text.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Text.Text.Trim().Length == 0)
        {
            Chyba.Text = "Napíš názov.";
            Chyba.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
