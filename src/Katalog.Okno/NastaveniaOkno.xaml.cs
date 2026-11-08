using System.Windows;
using System.Windows.Controls;
using Katalog.Jadro.Prehravanie;
using Katalog.Media;

namespace Katalog.Okno;

/// <summary>Settings of this computer: the player, FFmpeg and reading of technical data, the theme and update checks.</summary>
public partial class NastaveniaOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly List<VolbaPrehravaca> _volby = [];
    private readonly NastrojeFfmpeg? _najdenyFfmpeg;
    private string _priecinokFfmpeg;

    internal NastaveniaOkno(Nastavenia nastavenia, IReadOnlyList<Prehravac> najdene, NastrojeFfmpeg? ffmpeg)
    {
        InitializeComponent();
        _nastavenia = nastavenia;
        _najdenyFfmpeg = ffmpeg;
        _priecinokFfmpeg = nastavenia.PriecinokFfmpeg;
        CitatTechnickeUdaje.IsChecked = nastavenia.CitatTechnickeUdaje;
        RobitNahlady.IsChecked = nastavenia.RobitNahlady;
        ZobrazFfmpeg();
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

    private void ZobrazFfmpeg()
    {
        if (_priecinokFfmpeg.Length > 0)
        {
            FfmpegPopis.Text = "Používa sa FFmpeg z vybraného priečinka: " + _priecinokFfmpeg;
        }
        else
        {
            FfmpegPopis.Text = _najdenyFfmpeg is { } f
                ? f.Zdroj switch
                {
                    ZdrojFfmpeg.Universum => "Používa sa FFmpeg z UNIVERSUM: " + f.Priecinok,
                    ZdrojFfmpeg.Pribaleny => "Používa sa FFmpeg nainštalovaný s KATALOGOM (LGPL, overený): " + f.Priecinok,
                    ZdrojFfmpeg.Cesta => "Používa sa FFmpeg nájdený v systéme: " + f.Priecinok,
                    _ => "Používa sa FFmpeg: " + f.Priecinok,
                }
                : "FFmpeg sa nenašiel – technické údaje sa nebudú čítať. Vyber priečinok s ffprobe.exe alebo KATALOG nainštaluj znova.";
        }

        TlacidloFfmpegAutomaticky.IsEnabled = _priecinokFfmpeg.Length > 0;
    }

    private void VybratFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Priečinok s ffprobe.exe a ffmpeg.exe" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (HladanieFfmpeg.V(dialog.FolderName, ZdrojFfmpeg.Zvoleny) is null)
        {
            MessageBox.Show(this, "V tomto priečinku nie je ffprobe.exe.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _priecinokFfmpeg = dialog.FolderName;
        ZobrazFfmpeg();
    }

    private void FfmpegAutomaticky_Click(object sender, RoutedEventArgs e)
    {
        _priecinokFfmpeg = "";
        ZobrazFfmpeg();
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        _nastavenia.PriecinokFfmpeg = _priecinokFfmpeg;
        _nastavenia.CitatTechnickeUdaje = CitatTechnickeUdaje.IsChecked == true;
        _nastavenia.RobitNahlady = RobitNahlady.IsChecked == true;
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
