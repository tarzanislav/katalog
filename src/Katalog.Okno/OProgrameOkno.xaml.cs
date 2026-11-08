using System.Windows;
using Katalog.Jadro;

namespace Katalog.Okno;

public partial class OProgrameOkno : Window
{
    public OProgrameOkno()
    {
        InitializeComponent();
        Title = "O programe " + Produkt.Nazov;
        Nazov.Text = Produkt.Nazov;
        Verzia.Text = "Verzia " + Produkt.VerziaText;
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
