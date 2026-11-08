using System.Windows;

namespace Katalog.Okno;

/// <summary>Lending a disk or a title (N6): to whom (names lent to before are offered) and a note.</summary>
public partial class PozicatOkno : Window
{
    private PozicatOkno(string co, IReadOnlyList<string> mena)
    {
        InitializeComponent();
        Co.Text = co;
        Komu.ItemsSource = mena;
        Loaded += (_, _) => Komu.Focus();
    }

    /// <summary>To whom and the note, or null when the user cancelled.</summary>
    public static (string Komu, string? Poznamka)? Opytaj(Window vlastnik, string co, IReadOnlyList<string> mena)
    {
        var okno = new PozicatOkno(co, mena) { Owner = vlastnik };
        return okno.ShowDialog() == true
            ? (okno.Komu.Text.Trim(), okno.Poznamka.Text.Trim() is { Length: > 0 } p ? p : null)
            : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Komu.Text.Trim().Length == 0)
        {
            Chyba.Text = "Napíš, komu to požičiavaš.";
            Chyba.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
