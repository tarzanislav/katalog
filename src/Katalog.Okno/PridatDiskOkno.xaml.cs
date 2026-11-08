using System.IO;
using System.Windows;
using System.Windows.Controls;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;

namespace Katalog.Okno;

/// <summary>What the user chose to add: where it is now, its identity and how it is named.</summary>
/// <param name="KatalogId">An existing catalogue, or null when <paramref name="NovyKatalog"/> is to be created.</param>
internal sealed record VolbaDisku(string Cesta, IdentitaZvazku Identita, string Koren, string Nazov, long? KatalogId, string? NovyKatalog, long? Volne);

/// <summary>Adding a disk: a whole connected volume, one folder of it, or a network share by its address.</summary>
public partial class PridatDiskOkno : Window
{
    private static readonly KatalogKniznice NovyKatalogVolba = new(0, "Nový katalóg…", null, int.MaxValue);

    private readonly IReadOnlyList<Disk> _disky;
    private readonly IZvazky _zvazky;

    internal PridatDiskOkno(IReadOnlyList<KatalogKniznice> katalogy, IReadOnlyList<Disk> disky, IZvazky zvazky, long? predvolenyKatalog)
    {
        InitializeComponent();
        _disky = disky;
        _zvazky = zvazky;
        Katalogy.ItemsSource = katalogy.Append(NovyKatalogVolba).ToList();
        Katalogy.SelectedItem = katalogy.FirstOrDefault(k => k.Id == predvolenyKatalog) ?? katalogy.FirstOrDefault() ?? NovyKatalogVolba;
        NovyKatalog.Text = katalogy.Count == 0 ? "Videotéka" : "";
        ZobrazNovyKatalog();
        Loaded += async (_, _) => await NacitajZvazkyAsync();
    }

    internal VolbaDisku? Volba { get; private set; }

    private async Task NacitajZvazkyAsync()
    {
        var zvazky = await Task.Run(_zvazky.Pripojene);
        ZoznamZvazkov.ItemsSource = zvazky.Select(z => new PolozkaZvazku(z, Popis(z))).ToList();
        NacitavamZvazky.Text = zvazky.Count == 0 ? "Nenašiel sa žiadny pripojený disk." : "";
        NacitavamZvazky.Visibility = zvazky.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string Popis(PripojenyZvazok z)
    {
        var casti = new List<string> { Format.Druh(z.Identita.Druh), Format.Velkost(z.Identita.Velkost) };
        if (z.Volne is { } volne)
        {
            casti.Add("voľné " + Format.Velkost(volne));
        }

        var znamy = _disky.FirstOrDefault(d => d.Koren.Length == 0 && ZhodaZvazku.Porovnaj(d.Identita, z.Identita).Verdikt == VerdiktZhody.Isto);
        if (znamy is not null)
        {
            casti.Add($"už je v knižnici ako disk {znamy.CisloText}");
        }

        return string.Join(" · ", casti);
    }

    private void Volba_Changed(object sender, RoutedEventArgs e) => Chyba.Visibility = Visibility.Collapsed;

    private void ZoznamZvazkov_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoznamZvazkov.SelectedItem is PolozkaZvazku p)
        {
            VolbaZvazok.IsChecked = true;
            NazovDisku.Text = p.Zvazok.Identita.Menovka ?? "Disk " + p.Zvazok.Koren.TrimEnd('\\', ':');
        }
    }

    private void VybratPriecinok_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Priečinok, ktorý bude diskom katalógu" };
        if (dialog.ShowDialog(this) == true)
        {
            CestaPriecinka.Text = dialog.FolderName;
            VolbaPriecinok.IsChecked = true;
            NazovDisku.Text = Path.GetFileName(dialog.FolderName.TrimEnd('\\')) is { Length: > 0 } meno ? meno : dialog.FolderName;
        }
    }

    private void SietovaAdresa_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SietovaAdresa.Text.Length > 0)
        {
            VolbaSiet.IsChecked = true;
            var casti = SietovaAdresa.Text.Trim('\\').Split('\\');
            NazovDisku.Text = casti[^1];
        }
    }

    private void Katalogy_SelectionChanged(object sender, SelectionChangedEventArgs e) => ZobrazNovyKatalog();

    private void ZobrazNovyKatalog()
    {
        var novy = ReferenceEquals(Katalogy.SelectedItem, NovyKatalogVolba);
        NovyKatalog.Visibility = novy ? Visibility.Visible : Visibility.Collapsed;
        NovyKatalogPopis.Visibility = NovyKatalog.Visibility;
    }

    private async void Pridat_Click(object sender, RoutedEventArgs e)
    {
        TlacidloPridat.IsEnabled = false;
        try
        {
            var volba = await PripravVolbuAsync();
            if (volba is not null)
            {
                Volba = volba;
                DialogResult = true;
            }
        }
        finally
        {
            TlacidloPridat.IsEnabled = true;
        }
    }

    private async Task<VolbaDisku?> PripravVolbuAsync()
    {
        var nazov = NazovDisku.Text.Trim();
        var katalog = Katalogy.SelectedItem as KatalogKniznice;
        var novyKatalog = ReferenceEquals(katalog, NovyKatalogVolba) ? NovyKatalog.Text.Trim() : null;
        if (novyKatalog is { Length: 0 })
        {
            return Zle("Napíš názov nového katalógu.");
        }

        string cesta;
        IdentitaZvazku? identita;
        var koren = "";
        if (VolbaZvazok.IsChecked == true)
        {
            if (ZoznamZvazkov.SelectedItem is not PolozkaZvazku p)
            {
                return Zle("Vyber disk zo zoznamu.");
            }

            cesta = p.Zvazok.Koren;
            identita = p.Zvazok.Identita;
        }
        else if (VolbaPriecinok.IsChecked == true)
        {
            cesta = CestaPriecinka.Text;
            if (cesta.Length == 0)
            {
                return Zle("Vyber priečinok.");
            }

            identita = await Task.Run(() => _zvazky.Identita(cesta));
            koren = RozpoznanieDiskov.RelativnyKoren(Path.GetPathRoot(cesta)!, cesta);
        }
        else
        {
            cesta = SietovaAdresa.Text.Trim();
            if (!cesta.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return Zle(@"Sieťová adresa začína dvoma spätnými lomkami, napríklad \\nas\filmy.");
            }

            identita = await Task.Run(() => _zvazky.Identita(cesta));
        }

        if (identita is null)
        {
            return Zle("Disk alebo priečinok teraz nie je dostupný.");
        }

        if (nazov.Length == 0)
        {
            return Zle("Napíš názov disku.");
        }

        var rovnaky = _disky.FirstOrDefault(d => string.Equals(d.Koren, koren, StringComparison.OrdinalIgnoreCase) &&
                                                ZhodaZvazku.Porovnaj(d.Identita, identita).Verdikt == VerdiktZhody.Isto);
        if (rovnaky is not null)
        {
            return Zle($"Tento disk už je v knižnici ako disk {rovnaky.CisloText} ({rovnaky.Nazov}). Na jeho aktualizáciu použi „Aktualizovať disk“.");
        }

        return new VolbaDisku(cesta, identita, koren, nazov, novyKatalog is null ? katalog?.Id : null, novyKatalog, _zvazky.Miesto(cesta).Volne);
    }

    private VolbaDisku? Zle(string sprava)
    {
        Chyba.Text = sprava;
        Chyba.Visibility = Visibility.Visible;
        return null;
    }

    private sealed record PolozkaZvazku(PripojenyZvazok Zvazok, string Popis)
    {
        public string Nadpis => Zvazok.Koren + "   " + (Zvazok.Identita.Menovka ?? "");
    }
}
