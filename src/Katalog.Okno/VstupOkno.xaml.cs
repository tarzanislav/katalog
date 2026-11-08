using System.Windows;

namespace Katalog.Okno;

/// <summary>A small question with one line of text (new catalogue, renaming).</summary>
public partial class VstupOkno : Window
{
    private readonly bool _prazdne;

    private VstupOkno(string nadpis, string otazka, string predvolene, bool prazdne)
    {
        _prazdne = prazdne;
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
    /// <param name="prazdne">An empty answer is fine (e.g. no tags at all).</param>
    public static string? Opytaj(Window vlastnik, string nadpis, string otazka, string predvolene = "", bool prazdne = false)
    {
        var okno = new VstupOkno(nadpis, otazka, predvolene, prazdne) { Owner = vlastnik };
        return okno.ShowDialog() == true ? okno.Text.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_prazdne && Text.Text.Trim().Length == 0)
        {
            Chyba.Text = "Napíš názov.";
            Chyba.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
