using System.Windows;
using Katalog.Zdroje;

namespace Katalog.Okno;

/// <summary>Picking another poster of a title from those TMDB has (while confirming a candidate, or later in the detail).</summary>
public partial class PlagatyOkno : Window
{
    private Uri? _vybrany;

    private PlagatyOkno(string co, IReadOnlyList<PlagatTitulu> plagaty)
    {
        InitializeComponent();
        Co.Text = co;
        Zoznam.ItemsSource = plagaty.Select(p => new MoznostPlagatu(p.Adresa, Popis(p))).ToList();
    }

    /// <summary>The address of the picked poster, or null when the user cancelled.</summary>
    public static Uri? Vyber(Window vlastnik, string co, IReadOnlyList<PlagatTitulu> plagaty)
    {
        var okno = new PlagatyOkno(co, plagaty) { Owner = vlastnik };
        return okno.ShowDialog() == true ? okno._vybrany : null;
    }

    internal static string Popis(PlagatTitulu p) => string.Join(" · ", new[]
    {
        p.Jazyk switch { "sk" => "slovenský", "cs" => "český", "en" => "anglický", null => "bez textu", var j => j },
        p.Sirka is { } s && p.Vyska is { } v ? $"{s} × {v}" : null,
    }.Where(t => t is not null));

    private void Plagat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MoznostPlagatu m })
        {
            _vybrany = m.Adresa;
            DialogResult = true;
        }
    }

    internal sealed record MoznostPlagatu(Uri Adresa, string Popis);
}
