using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Katalog.Jadro.Model;
using Katalog.Jadro.Prehravanie;
using Katalog.Media;
using Katalog.Zdroje;

namespace Katalog.Okno;

/// <summary>
/// Settings of this computer: the player, FFmpeg and reading of technical data, the theme and update checks; the sources
/// of data with their keys (own ones win over UNIVERSUM's; a key is never shown again after it is saved).
/// </summary>
public partial class NastaveniaOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly List<VolbaPrehravaca> _volby = [];
    private readonly NastrojeFfmpeg? _najdenyFfmpeg;
    private string _priecinokFfmpeg;
    private readonly Tajomstva _tajomstva;
    private bool _odstranitTmdb;
    private bool _odstranitHistra;
    private readonly ObservableCollection<PolozkaKriteria> _kriteria;

    internal NastaveniaOkno(Nastavenia nastavenia, IReadOnlyList<Prehravac> najdene, NastrojeFfmpeg? ffmpeg, Tajomstva tajomstva, bool zdroje = false)
    {
        InitializeComponent();
        _nastavenia = nastavenia;
        _tajomstva = tajomstva;
        PouzivatTmdb.IsChecked = nastavenia.PouzivatTmdb;
        PouzivatHistra.IsChecked = nastavenia.PouzivatHistra;
        PouzivatJellyfin.IsChecked = nastavenia.PouzivatJellyfin;
        JellyfinAdresa.Text = nastavenia.JellyfinAdresa;
        JellyfinPouzivatel.Text = nastavenia.JellyfinPouzivatel;
        JellyfinKlucPopis.Text = tajomstva.Ma("jellyfin") ? "Kľúč API (uložený)" : "Kľúč API";
        _kriteria = new ObservableCollection<PolozkaKriteria>(PorovnanieVerzii.ZTextu(nastavenia.PoradieKvality).Select(k => new PolozkaKriteria(k)));
        PoradieKvality.ItemsSource = _kriteria;
        ZobrazKluce();
        if (zdroje)
        {
            Karty.SelectedItem = KartaZdroje;
        }

        _najdenyFfmpeg = ffmpeg;
        _priecinokFfmpeg = nastavenia.PriecinokFfmpeg;
        CitatTechnickeUdaje.IsChecked = nastavenia.CitatTechnickeUdaje;
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

    /// <summary>
    /// What each key is now: linked to UNIVERSUM's (the text says so), the own one, or none – then only the field for a
    /// new key is offered.
    /// </summary>
    private void ZobrazKluce()
    {
        var kluce = _tajomstva.Kluce();
        var (tmdbUniversum, histraUniversum) = KluceZdrojov.Universum(KluceZdrojov.SuborUniversum, _ => "?");
        var vlastnyTmdb = _tajomstva.Ma("tmdb") && !_odstranitTmdb;
        var vlastnaHistra = _tajomstva.Ma("histra") && !_odstranitHistra;
        var tmdbZUniversa = !vlastnyTmdb && kluce.Tmdb.Povod == PovodKluca.Universum;
        var histraZUniversa = !vlastnaHistra && kluce.Histra.Povod == PovodKluca.Universum;

        TmdbStav.Text = vlastnyTmdb ? "Používa sa tvoj vlastný kľúč TMDB."
            : tmdbZUniversa ? "Momentálne prepojené na kľúč TMDB z UNIVERSUM."
            : "";
        TmdbStav.Visibility = TmdbStav.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TmdbPopisPola.Text = tmdbZUniversa || (vlastnyTmdb && tmdbUniversum is not null)
            ? "Vlastný kľúč TMDB (má prednosť pred kľúčom z UNIVERSUM):"
            : vlastnyTmdb ? "Nový kľúč TMDB (nahradí uložený):" : "Kľúč TMDB:";
        TlacidloOdstranitTmdb.Visibility = vlastnyTmdb ? Visibility.Visible : Visibility.Collapsed;

        HistraStav.Text = vlastnaHistra ? "Používa sa tvoj vlastný token HISTRA."
            : histraZUniversa ? "Momentálne prepojené na token HISTRA z UNIVERSUM."
            : "";
        HistraStav.Visibility = HistraStav.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HistraPopisPola.Text = histraZUniversa || (vlastnaHistra && histraUniversum is not null)
            ? "Vlastný token HISTRA (má prednosť pred tokenom z UNIVERSUM):"
            : vlastnaHistra ? "Nový token HISTRA (nahradí uložený):" : "Token HISTRA:";
        TlacidloOdstranitHistra.Visibility = vlastnaHistra ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KriteriumHore_Click(object sender, RoutedEventArgs e) => PosunKriterium(-1);

    private void KriteriumDole_Click(object sender, RoutedEventArgs e) => PosunKriterium(1);

    private void PosunKriterium(int smer)
    {
        if (PoradieKvality.SelectedIndex is var i && i >= 0 && i + smer >= 0 && i + smer < _kriteria.Count)
        {
            _kriteria.Move(i, i + smer);
            PoradieKvality.SelectedIndex = i + smer;
        }
    }

    private void OdstranitTmdb_Click(object sender, RoutedEventArgs e)
    {
        _odstranitTmdb = true;
        TmdbKluc.Clear();
        ZobrazKluce();
    }

    private void OdstranitHistra_Click(object sender, RoutedEventArgs e)
    {
        _odstranitHistra = true;
        HistraToken.Clear();
        ZobrazKluce();
    }

    private void OdkazHistra_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HistraZdroj.StrankaTokenu.AbsoluteUri) { UseShellExecute = true });

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        var histra = HistraToken.Password.Trim();
        if (histra.Length > 0 && !HistraZdroj.JePlatnyToken(histra))
        {
            Karty.SelectedItem = KartaZdroje;
            MessageBox.Show(this, "Token HISTRA má neplatné znaky – skopíruj ho znova celý, bez medzier.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PouzivatJellyfin.IsChecked == true && JellyfinZdroj.Over(JellyfinAdresa.Text) is null)
        {
            Karty.SelectedItem = KartaZdroje;
            MessageBox.Show(this, "Adresa servera Jellyfin musí byť https://…, alebo http://… len v domácej sieti (napr. http://192.168.1.20:8096).",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (_odstranitTmdb || TmdbKluc.Password.Trim().Length > 0)
            {
                _tajomstva.Zapis("tmdb", TmdbKluc.Password);
            }

            if (_odstranitHistra || histra.Length > 0)
            {
                _tajomstva.Zapis("histra", histra);
            }

            if (JellyfinKluc.Password.Trim().Length > 0)
            {
                _tajomstva.Zapis("jellyfin", JellyfinKluc.Password);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Kľúče sa nepodarilo uložiť: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _nastavenia.PoradieKvality = PorovnanieVerzii.NaText(_kriteria.Select(k => k.Kriterium));
        _nastavenia.PouzivatTmdb = PouzivatTmdb.IsChecked == true;
        _nastavenia.PouzivatHistra = PouzivatHistra.IsChecked == true;
        _nastavenia.PouzivatJellyfin = PouzivatJellyfin.IsChecked == true;
        _nastavenia.JellyfinAdresa = JellyfinAdresa.Text.Trim();
        _nastavenia.JellyfinPouzivatel = JellyfinPouzivatel.Text.Trim();
        _nastavenia.PriecinokFfmpeg = _priecinokFfmpeg;
        _nastavenia.CitatTechnickeUdaje = CitatTechnickeUdaje.IsChecked == true;
        _nastavenia.Prehravac = (ZoznamPrehravacov.SelectedItem as VolbaPrehravaca)?.Prehravac.Program ?? "";
        _nastavenia.Motiv = (Motiv.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        _nastavenia.AutomatickeAktualizacie = AutomatickeAktualizacie.IsChecked == true;
        App.NastavMotiv(_nastavenia.Motiv);
        DialogResult = true;
    }

    private sealed record PolozkaKriteria(KriteriumVerzie Kriterium)
    {
        public string Nazov => PorovnanieVerzii.Nazov(Kriterium);
    }

    private sealed record VolbaPrehravaca(Prehravac Prehravac)
    {
        public string Popis => Prehravac.Program is { } program ? $"{Prehravac.Nazov} ({program})" : Prehravac.Nazov;
    }
}
