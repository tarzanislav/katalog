using System.Windows;
using System.Windows.Controls;
using Katalog.Jadro.Prehravanie;

namespace Katalog.Okno;

/// <summary>Settings of this computer: the player, the theme and update checks.</summary>
public partial class NastaveniaOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly List<VolbaPrehravaca> _volby = [];

    internal NastaveniaOkno(Nastavenia nastavenia, IReadOnlyList<Prehravac> najdene)
    {
        InitializeComponent();
        _nastavenia = nastavenia;
        _volby.Add(new VolbaPrehravaca(Prehravac.Predvoleny));
        _volby.AddRange(najdene.Select(p => new VolbaPrehravaca(p)));
        var aktualny = nastavenia.SkutocnyPrehravac;
        if (aktualny.Program is { } program && _volby.All(v => !string.Equals(v.Prehravac.Program, program, StringComparison.OrdinalIgnoreCase)))
        {
            _volby.Add(new VolbaPrehravaca(aktualny));
        }

        ZoznamPrehravacov.ItemsSource = _volby;
        ZoznamPrehravacov.SelectedItem = _volby.FirstOrDefault(v => string.Equals(v.Prehravac.Program, aktualny.Program, StringComparison.OrdinalIgnoreCase)) ?? _volby[0];
        Motiv.SelectedItem = Motiv.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == nastavenia.Motiv) ?? Motiv.Items[0];
        AutomatickeAktualizacie.IsChecked = nastavenia.AutomatickeAktualizacie;
    }

    private void InyProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Program prehrávača", Filter = "Programy (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            var volba = new VolbaPrehravaca(Prehravac.ZProgramu(dialog.FileName));
            _volby.Add(volba);
            ZoznamPrehravacov.Items.Refresh();
            ZoznamPrehravacov.SelectedItem = volba;
        }
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        _nastavenia.Prehravac = (ZoznamPrehravacov.SelectedItem as VolbaPrehravaca)?.Prehravac.Program ?? "";
        _nastavenia.Motiv = (Motiv.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        _nastavenia.AutomatickeAktualizacie = AutomatickeAktualizacie.IsChecked == true;
        App.NastavMotiv(_nastavenia.Motiv);
        DialogResult = true;
    }

    private sealed record VolbaPrehravaca(Prehravac Prehravac)
    {
        public string Popis => Prehravac.Program is { } program ? $"{Prehravac.Nazov} ({program})" : Prehravac.Nazov;
    }
}
