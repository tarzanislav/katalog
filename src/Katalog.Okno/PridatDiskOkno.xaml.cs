using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;

namespace Katalog.Okno;

/// <summary>What the user chose to add: where it is now, its identity and how it is named.</summary>
/// <param name="KatalogId">An existing catalogue, or null when <paramref name="NovyKatalog"/> is to be created.</param>
/// <param name="Nahlady">Previews of its videos are made (R10: off unless chosen).</param>
/// <param name="Ulozisko">A server's storage already checked (stage E8), used for its first scan.</param>
internal sealed record VolbaDisku(
    string Cesta, IdentitaZvazku Identita, string Koren, string Nazov, long? KatalogId, string? NovyKatalog, long? Volne, bool Nahlady = false,
    IUlozisko? Ulozisko = null);

/// <summary>Adding a disk: a whole connected volume, one folder of it, a network share by its address, or a server through WinSCP.</summary>
public partial class PridatDiskOkno : Window
{
    private static readonly KatalogKniznice NovyKatalogVolba = new(0, "Nový katalóg…", null, int.MaxValue);

    private readonly IReadOnlyList<Disk> _disky;
    private readonly IZvazky _zvazky;

    private string? _winScp;

    internal PridatDiskOkno(IReadOnlyList<KatalogKniznice> katalogy, IReadOnlyList<Disk> disky, IZvazky zvazky, long? predvolenyKatalog, string priecinokWinScp = "")
    {
        InitializeComponent();
        PriecinokWinScp = priecinokWinScp;
        // checked only now: during InitializeComponent the controls its handler touches do not exist yet
        VolbaZvazok.IsChecked = true;
        _disky = disky;
        _zvazky = zvazky;
        Katalogy.ItemsSource = katalogy.Append(NovyKatalogVolba).ToList();
        Katalogy.SelectedItem = katalogy.FirstOrDefault(k => k.Id == predvolenyKatalog) ?? katalogy.FirstOrDefault() ?? NovyKatalogVolba;
        NovyKatalog.Text = katalogy.Count == 0 ? "Videotéka" : "";
        ZobrazNovyKatalog();
        Loaded += async (_, _) =>
        {
            NacitajLokality();
            await NacitajZvazkyAsync();
        };
    }

    internal VolbaDisku? Volba { get; private set; }

    /// <summary>The folder of WinSCP.com the user chose here (empty = found by itself), kept in the settings.</summary>
    internal string PriecinokWinScp { get; private set; }

    /// <summary>The sites saved in WinSCP (only their names; passwords stay in WinSCP).</summary>
    private void NacitajLokality()
    {
        _winScp = WinScp.Najdi(PriecinokWinScp);
        if (_winScp is null)
        {
            StavWinScp.Text = "WinSCP sa na tomto počítači nenašiel (winscp.net).";
            TlacidloWinScp.Visibility = Visibility.Visible;
            LokalityWinScp.IsEnabled = false;
            return;
        }

        var lokality = WinScp.UlozeneLokality(_winScp);
        LokalityWinScp.ItemsSource = lokality;
        LokalityWinScp.IsEnabled = lokality.Count > 0;
        TlacidloWinScp.Visibility = Visibility.Collapsed;
        StavWinScp.Text = lokality.Count == 0
            ? "Vo WinSCP nie je uložená žiadna lokalita. Ulož si ju tam (aj s heslom alebo kľúčom) a pripoj sa ňou raz."
            : "KATALOG použije uloženú lokalitu WinSCP – heslo ani kľúč nikdy nevidí. Zo servera len číta zoznam súborov.";
    }

    private void VybratWinScp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Nájdi WinSCP.com", Filter = "WinSCP.com|WinSCP.com" };
        if (dialog.ShowDialog(this) == true && Path.GetDirectoryName(dialog.FileName) is { } priecinok)
        {
            PriecinokWinScp = priecinok;
            NacitajLokality();
        }
    }

    private void LokalityWinScp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LokalityWinScp.SelectedItem is string lokalita)
        {
            VolbaServer.IsChecked = true;
            NazovDisku.Text = lokalita.Split('/')[^1];
        }
    }

    private void PriecinokServera_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && PriecinokServera.Text.Trim('/').Length > 0)
        {
            VolbaServer.IsChecked = true;
            NazovDisku.Text = PriecinokServera.Text.Trim('/').Split('/')[^1];
        }
    }

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
        else if (VolbaServer.IsChecked == true)
        {
            return await PripravServerAsync(nazov, katalog, novyKatalog);
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

        return new VolbaDisku(cesta, identita, koren, nazov, novyKatalog is null ? katalog?.Id : null, novyKatalog, _zvazky.Miesto(cesta).Volne,
            Nahlady.IsChecked == true);
    }

    /// <summary>A server: the site opens and its folder can be listed (one connection; the scan reads the rest).</summary>
    private async Task<VolbaDisku?> PripravServerAsync(string nazov, KatalogKniznice? katalog, string? novyKatalog)
    {
        if (_winScp is null)
        {
            return Zle("Najprv nainštaluj WinSCP alebo ho vyber tlačidlom „Vybrať WinSCP…“.");
        }

        if (LokalityWinScp.SelectedItem is not string lokalita)
        {
            return Zle("Vyber lokalitu uloženú vo WinSCP.");
        }

        if (nazov.Length == 0)
        {
            return Zle("Napíš názov disku.");
        }

        var koren = string.Join('/', PriecinokServera.Text.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        var identita = new IdentitaZvazku(DruhDisku.Vzdialeny, null, null, null, lokalita, null, null, WinScpUlozisko.Adresa(lokalita));
        var rovnaky = _disky.FirstOrDefault(d => string.Equals(d.Koren, koren, StringComparison.Ordinal) &&
                                                ZhodaZvazku.Porovnaj(d.Identita, identita).Verdikt == VerdiktZhody.Isto);
        if (rovnaky is not null)
        {
            return Zle($"Tento priečinok servera už je v knižnici ako disk {rovnaky.CisloText} ({rovnaky.Nazov}).");
        }

        var ulozisko = WinScpUlozisko.SWinScp(_winScp, lokalita, koren);
        Chyba.Text = "Pripájam sa cez WinSCP…";
        Chyba.Visibility = Visibility.Visible;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await Task.Run(ulozisko.Over);
        }
        catch (IOException ex)
        {
            return Zle(ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        Chyba.Visibility = Visibility.Collapsed;
        return new VolbaDisku(ulozisko.Koren, identita, koren, nazov, novyKatalog is null ? katalog?.Id : null, novyKatalog, null, false, ulozisko);
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
