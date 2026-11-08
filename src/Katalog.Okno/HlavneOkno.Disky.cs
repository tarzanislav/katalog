using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Skenovanie;
using Katalog.Ulohy;

namespace Katalog.Okno;

/// <summary>
/// Catalogues, disks and their folders (stage E1): the tree on the left, the folder's content in the middle, the detail on
/// the right, adding and scanning disks, and noticing when a disk of the library is connected.
/// </summary>
public partial class HlavneOkno
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    private readonly ObservableCollection<Uzol> _strom = [];
    private readonly DispatcherTimer _odkladZmenyZariadeni = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private StavPripojenia? _stav;
    private SpravcaSkenov? _skeny;
    private long? _zobrazenyDisk;
    private long? _zobrazenyPriecinok;
    private long? _diskOznamu;

    /// <summary>Which disks are connected now (the self-test reads it).</summary>
    internal StavPripojenia? Pripojenie => _stav;

    private async Task InicializujDiskyAsync(Kniznica kniznica)
    {
        _stav = new StavPripojenia(Zvazky.Systemove);
        _skeny = new SpravcaSkenov(kniznica);
        _skeny.Zmena += ZobrazPriebehSkenov;
        _skeny.Skoncil += (diskId, vysledok) => _ = PoSkeneAsync(diskId, vysledok);
        Uzol.NacitajDeti = NacitajDeti;
        StromDiskov.ItemsSource = _strom;
        _odkladZmenyZariadeni.Tick += async (_, _) =>
        {
            _odkladZmenyZariadeni.Stop();
            await ObnovPripojenieAsync(oznamit: true);
        };

        if (PresentationSource.FromVisual(this) is HwndSource zdroj)
        {
            zdroj.AddHook(SpravyOkna);
        }

        ObnovStrom();
        await ObnovPripojenieAsync(oznamit: true);
    }

    // ------------------------------------------------------------------ tree

    /// <summary>Builds the tree of catalogues and disks anew (after adding, moving or removing).</summary>
    private void ObnovStrom()
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        var katalogy = KatalogyADisky.Katalogy(c);
        var disky = KatalogyADisky.Disky(c);
        var otvoreneDisky = _strom.SelectMany(k => k.Deti).Where(d => d.Otvoreny).Select(d => d.Id).ToHashSet();
        _strom.Clear();
        foreach (var katalog in katalogy)
        {
            var uzol = new Uzol(DruhUzla.Katalog, katalog.Id, katalog.Nazov);
            foreach (var disk in disky.Where(d => d.KatalogId == katalog.Id))
            {
                var uzolDisku = new Uzol(DruhUzla.Disk, disk.Id, disk.Nazov, disk.Id, maDeti: disk.PoslednySken is not null)
                {
                    Popis = PopisDisku(disk, KatalogyADisky.SuhrnDisku(c, disk.Id)),
                    Pripojeny = _stav?.Pripojene.ContainsKey(disk.Id) == true,
                };
                uzol.Deti.Add(uzolDisku);
                if (otvoreneDisky.Contains(disk.Id))
                {
                    uzolDisku.Otvoreny = true;
                }
            }

            var pocet = disky.Count(d => d.KatalogId == katalog.Id);
            uzol.Popis = pocet == 0 ? "prázdny" : Format.Mnozne(pocet, "disk", "disky", "diskov");
            uzol.Otvoreny = true;
            _strom.Add(uzol);
        }

        ZiadnyDisk.Visibility = disky.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ZiadnyDisk.Text = katalogy.Count == 0
            ? "Zatiaľ žiadny disk. Pridaj prvý tlačidlom „Pridať disk“."
            : "V katalógoch zatiaľ nie je žiadny disk.";
    }

    private static string PopisDisku(Disk disk, Suhrn suhrn) => disk.PoslednySken is null
        ? $"{disk.CisloText} · zatiaľ nenaskenovaný"
        : $"{disk.CisloText} · {Format.Mnozne(suhrn.Subory, "súbor", "súbory", "súborov")} · {Format.Velkost(suhrn.Bajty)}";

    /// <summary>
    /// The name column takes what the other columns leave; in a narrow window the type and then the date step aside
    /// (the detail panel still shows them).
    /// </summary>
    private void ZoznamSuborov_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double nazov = 180, velkost = 90, zmenene = 130, typ = 90, okraj = 36;
        var sirka = ZoznamSuborov.ActualWidth - okraj;
        StlpecVelkost.Width = velkost;
        StlpecZmenene.Width = sirka >= nazov + velkost + zmenene ? zmenene : 0;
        StlpecTyp.Width = sirka >= nazov + velkost + zmenene + typ ? typ : 0;
        StlpecNazov.Width = Math.Max(nazov, sirka - velkost - StlpecZmenene.Width - StlpecTyp.Width);
    }

    private void NacitajDeti(Uzol uzol)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        var rodic = uzol.Druh == DruhUzla.Disk ? KatalogyADisky.Koren(c, uzol.Id) : uzol.Id;
        uzol.Deti.Clear();
        if (rodic is null)
        {
            return;
        }

        foreach (var priecinok in KatalogyADisky.Podpriecinky(c, rodic.Value))
        {
            uzol.Deti.Add(new Uzol(DruhUzla.Priecinok, priecinok.Id, priecinok.Nazov, uzol.DiskId, priecinok.MaPodpriecinky));
        }
    }

    private Uzol? VybratyUzol => StromDiskov.SelectedItem as Uzol;

    private void ZrusVyberVStrome()
    {
        if (VybratyUzol is { } uzol)
        {
            uzol.Vybraty = false;
        }
    }

    private void StromDiskov_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not Uzol uzol || Kniznica is not { } kniznica)
        {
            return;
        }

        NavigaciaKniznica.SelectedItem = null;
        NavigaciaZbierky.SelectedItem = null;
        switch (uzol.Druh)
        {
            case DruhUzla.Katalog:
                ZobrazPrazdny(uzol.Nazov, $"Katalóg „{uzol.Nazov}“", "Vyber disk alebo jeho priečinok vľavo. Disk pridáš tlačidlom „Pridať disk“.");
                break;
            case DruhUzla.Disk:
                using (var c = kniznica.OtvorCitanie())
                {
                    if (KatalogyADisky.Koren(c, uzol.Id) is { } koren)
                    {
                        ZobrazPriecinok(uzol.Id, koren);
                    }
                }

                break;
            case DruhUzla.Priecinok:
                ZobrazPriecinok(uzol.DiskId, uzol.Id);
                break;
        }
    }

    /// <summary>A right click selects the item under the mouse first, so the menu acts on it.</summary>
    private void StromDiskov_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var prvok = e.OriginalSource as DependencyObject;
        while (prvok is not null and not TreeViewItem)
        {
            prvok = VisualTreeHelper.GetParent(prvok);
        }

        if (prvok is TreeViewItem polozka)
        {
            polozka.IsSelected = true;
            polozka.Focus();
        }
    }

    private void PonukaStromu_Opened(object sender, RoutedEventArgs e)
    {
        var diskId = VybranyDisk();
        var jeDisk = diskId is not null;
        PonukaPrehrat.IsEnabled = jeDisk;
        PonukaAktualizovat.IsEnabled = jeDisk && _stav?.Pripojene.ContainsKey(diskId!.Value) == true && _skeny?.Bezi(diskId.Value) == false;
        PonukaOtvorit.IsEnabled = jeDisk && _stav?.Pripojene.ContainsKey(diskId!.Value) == true;
        PonukaUpravit.IsEnabled = jeDisk;
        PonukaOdstranit.IsEnabled = jeDisk && _skeny?.Bezi(diskId!.Value) == false;
        var katalog = VybranyKatalog();
        PonukaPremenovatKatalog.IsEnabled = katalog is not null;
        PonukaZmazatKatalog.IsEnabled = katalog is not null;
    }

    /// <summary>The disk the user works with: the selected disk or folder in the tree, else the shown one.</summary>
    private long? VybranyDisk() => VybratyUzol is { Druh: DruhUzla.Disk or DruhUzla.Priecinok } u ? u.DiskId : _zobrazenyDisk;

    private long? VybranyKatalog()
    {
        if (VybratyUzol is not { } uzol)
        {
            return null;
        }

        return uzol.Druh == DruhUzla.Katalog ? uzol.Id : _strom.FirstOrDefault(k => k.Deti.Any(d => d.Id == uzol.DiskId))?.Id;
    }

    // ------------------------------------------------------------------ content of a folder

    private void ZobrazPrazdny(string nadpis, string hlavny, string text)
    {
        _zobrazenyDisk = null;
        _zobrazenyPriecinok = null;
        NadpisStredu.Text = nadpis;
        PrazdnyNadpis.Text = hlavny;
        PrazdnyText.Text = text;
        UkazStred(PanelPrazdny);
        ZobrazDetail(null);
    }

    private void ZobrazPriecinok(long diskId, long priecinokId)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        if (KatalogyADisky.NajdiDisk(c, diskId) is not { } disk)
        {
            return;
        }

        _zobrazenyDisk = diskId;
        _zobrazenyPriecinok = priecinokId;
        var ajZmazane = AjZmazane.IsChecked == true;
        var cesta = KatalogyADisky.Cesta(c, priecinokId);
        var polozky = KatalogyADisky.Podpriecinky(c, priecinokId, ajZmazane).Select(PolozkaZoznamu.Z)
            .Concat(KatalogyADisky.Subory(c, priecinokId, ajZmazane).Select(PolozkaZoznamu.Z))
            .ToList();
        ZoznamSuborov.ItemsSource = polozky;

        var pripojeny = _stav?.Cesta(diskId);
        NadpisPriecinka.Text = cesta.Length == 0 ? disk.Nazov : cesta.Split('/')[^1];
        var miesto = cesta.Length == 0 ? "" : " · " + cesta.Replace('/', '\\');
        PopisPriecinka.Text = pripojeny is not null
            ? $"Disk {disk.CisloText} · {disk.Nazov} · pripojený ({PlnaCesta(pripojeny, cesta)})"
            : $"Disk {disk.CisloText} · {disk.Nazov}{miesto} · nie je pripojený, obsah je z posledného skenu ({Format.Cas(disk.PoslednySken)})";
        TlacidloHore.IsEnabled = cesta.Length > 0;
        TlacidloOtvorit.IsEnabled = pripojeny is not null;
        TlacidloAktualizovatDisk.IsEnabled = pripojeny is not null && _skeny?.Bezi(diskId) == false;
        UkazStred(PanelSuborov);

        if (polozky.Count == 0)
        {
            ZobrazDetail(disk, null, cesta);
        }
        else
        {
            ZobrazDetail(null);
        }
    }

    private static string PlnaCesta(string korenDisku, string relativna) =>
        relativna.Length == 0 ? korenDisku : Path.Combine(korenDisku, relativna.Replace('/', Path.DirectorySeparatorChar));

    private void Hore_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _zobrazenyDisk is not { } disk || _zobrazenyPriecinok is not { } priecinok)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        using var prikaz = c.CreateCommand();
        prikaz.CommandText = "SELECT rodic_id FROM priecinok WHERE id = $id";
        prikaz.Parameters.AddWithValue("$id", priecinok);
        if (prikaz.ExecuteScalar() is long rodic)
        {
            ZobrazPriecinok(disk, rodic);
        }
    }

    private void AjZmazane_Changed(object sender, RoutedEventArgs e)
    {
        if (_zobrazenyDisk is { } disk && _zobrazenyPriecinok is { } priecinok)
        {
            ZobrazPriecinok(disk, priecinok);
        }
    }

    private async void ZoznamSuborov_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject zdroj && ItemsControl.ContainerFromElement(ZoznamSuborov, zdroj) is ListViewItem)
        {
            await OtvorVZoznameSuborovAsync();
        }
    }

    private async void ZoznamSuborov_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OtvorVZoznameSuborovAsync();
        }
        else if (e.Key == Key.Back && TlacidloHore.IsEnabled)
        {
            e.Handled = true;
            Hore_Click(this, new RoutedEventArgs());
        }
    }

    /// <summary>A folder opens (unless several are selected to play), videos play, other files open in their program.</summary>
    private async Task OtvorVZoznameSuborovAsync()
    {
        var ciele = VybraneCiele();
        if (ciele.Count == 1 && ciele[0].JePriecinok && _zobrazenyDisk is { } diskId)
        {
            ZobrazPriecinok(diskId, ciele[0].PriecinokId);
        }
        else if (ciele.Count > 0 && ciele.All(c => c.DaSaPrehrat))
        {
            await PrehrajAsync(ciele);
        }
        else if (ciele.Count == 1)
        {
            OtvorSubor(ciele[0]);
        }
    }

    private string PripojDisk(long diskId)
    {
        using var c = Kniznica!.OtvorCitanie();
        var disk = KatalogyADisky.NajdiDisk(c, diskId);
        var kde = disk?.Umiestnenie is { Length: > 0 } u ? $" Leží: {u}." : "";
        return $"Pripoj disk {disk?.CisloText} ({disk?.Nazov}).{kde}";
    }

    private void ZoznamSuborov_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _zobrazenyDisk is not { } diskId || _zobrazenyPriecinok is not { } priecinok)
        {
            return;
        }

        using var c = kniznica.OtvorCitanie();
        var disk = KatalogyADisky.NajdiDisk(c, diskId);
        ZobrazDetail(disk, ZoznamSuborov.SelectedItem as PolozkaZoznamu, KatalogyADisky.Cesta(c, priecinok));
    }

    private void ZobrazDetail(Disk? disk, PolozkaZoznamu? polozka = null, string cesta = "")
    {
        if (disk is null)
        {
            DetailNadpis.Text = "Detail titulu";
            DetailPrazdny.Visibility = Visibility.Visible;
            DetailRiadky.ItemsSource = null;
            return;
        }

        var riadky = new List<RiadokDetailu>();
        var pripojeny = _stav?.Cesta(disk.Id);
        if (polozka is not null)
        {
            DetailNadpis.Text = polozka.Nazov;
            if (!polozka.JePriecinok)
            {
                riadky.Add(new RiadokDetailu("Veľkosť", $"{Format.Velkost(polozka.Velkost)} ({Format.Pocet(polozka.Velkost)} B)"));
                riadky.Add(new RiadokDetailu("Zmenené", polozka.ZmeneneText));
            }

            var vnutri = cesta.Length == 0 ? polozka.Nazov : cesta + "/" + polozka.Nazov;
            riadky.Add(new RiadokDetailu("Cesta na disku", vnutri.Replace('/', '\\')));
            if (polozka.Zmazana)
            {
                riadky.Add(new RiadokDetailu("Stav", "Pri poslednom skene už na disku nebol (ostáva v histórii)."));
            }
        }
        else
        {
            DetailNadpis.Text = disk.Nazov;
        }

        riadky.Add(new RiadokDetailu("Disk", $"{disk.CisloText} · {disk.Nazov} · {Format.Druh(disk.Druh)}"));
        riadky.Add(new RiadokDetailu("Kde je", pripojeny is not null ? $"Pripojený ako {pripojeny}" : "Nie je pripojený" + (disk.Umiestnenie is { Length: > 0 } u ? $" · leží: {u}" : "")));
        riadky.Add(new RiadokDetailu("Posledný sken", Format.Cas(disk.PoslednySken)));
        if (disk.Poznamka is { Length: > 0 } poznamka)
        {
            riadky.Add(new RiadokDetailu("Poznámka", poznamka));
        }

        DetailPrazdny.Visibility = Visibility.Collapsed;
        DetailRiadky.ItemsSource = riadky;
    }

    // ------------------------------------------------------------------ disks and catalogues

    private async void PridatDisk_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _stav is null)
        {
            return;
        }

        IReadOnlyList<KatalogKniznice> katalogy;
        IReadOnlyList<Disk> disky;
        using (var c = kniznica.OtvorCitanie())
        {
            katalogy = KatalogyADisky.Katalogy(c);
            disky = KatalogyADisky.Disky(c);
        }

        var dialog = new PridatDiskOkno(katalogy, disky, _stav.Zvazky, VybranyKatalog()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Volba is not { } volba)
        {
            return;
        }

        var diskId = await PridajDiskAsync(kniznica, volba);
        ObnovStrom();
        await ObnovPripojenieAsync(oznamit: false);
        VyberDisk(diskId);
        await SkenujAsync(diskId);
    }

    /// <summary>Writes a chosen disk (and a new catalogue for it) into the library; returns the disk's id.</summary>
    internal static async Task<long> PridajDiskAsync(Kniznica kniznica, VolbaDisku volba)
    {
        var katalog = volba.KatalogId ?? await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, volba.NovyKatalog ?? "Videotéka"));
        var koren = kniznica.NovyIdPriecinka();
        return await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PridajDisk(s, new NovyDisk(katalog, volba.Nazov, volba.Identita, volba.Koren, volba.Volne), koren));
    }

    /// <summary>Selects a disk in the tree (and shows its content).</summary>
    internal void VyberDisk(long diskId)
    {
        if (_strom.SelectMany(k => k.Deti).FirstOrDefault(d => d.Id == diskId) is { } uzol)
        {
            uzol.Vybraty = true;
        }
    }

    private async void AktualizovatDisk_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is { } diskId)
        {
            await SkenujAsync(diskId);
        }
    }

    /// <summary>Scans a connected disk into the library (in the background; the window stays usable).</summary>
    internal async Task<VysledokSkenu?> SkenujAsync(long diskId)
    {
        if (Kniznica is not { } kniznica || _skeny is null || _stav is null)
        {
            return null;
        }

        if (_stav.Ulozisko(diskId) is not { } ulozisko)
        {
            MessageBox.Show(this, PripojDisk(diskId), Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        string nazov;
        using (var c = kniznica.OtvorCitanie())
        {
            nazov = KatalogyADisky.NajdiDisk(c, diskId) is { } d ? $"{d.CisloText} {d.Nazov}" : "";
        }

        SkryOznam(diskId);
        if (_zobrazenyDisk == diskId)
        {
            TlacidloAktualizovatDisk.IsEnabled = false;
        }

        return await _skeny.SpustiAsync(diskId, nazov, ulozisko);
    }

    private Task PoSkeneAsync(long diskId, VysledokSkenu vysledok)
    {
        var text = vysledok.Stav switch
        {
            StavUlohy.Hotova => $"Sken hotový za {vysledok.Trvanie.TotalSeconds:0} s: nové {Format.Pocet(vysledok.Zmeny.Pridane)}, " +
                                $"zmenené {Format.Pocet(vysledok.Zmeny.Zmenene)}, zmazané {Format.Pocet(vysledok.Zmeny.Zmazane)}" +
                                (vysledok.Chyby > 0 ? $", nečitateľné priečinky {vysledok.Chyby}" : ""),
            StavUlohy.Zrusena => "Sken zrušený. Hotové priečinky sú uložené, ďalší sken pokračuje.",
            _ => "Sken sa nepodaril: " + (vysledok.Sprava ?? "neznáma chyba"),
        };
        StavUloh.Text = text;
        ObnovStrom();
        if (_zobrazenyDisk == diskId || PanelVysledkov.Visibility == Visibility.Visible)
        {
            ObnovStred();
        }

        return Task.CompletedTask;
    }

    /// <summary>After a scan or a change of connected disks: the shown folder or search results are read again.</summary>
    private void ObnovStred()
    {
        if (PanelVysledkov.Visibility == Visibility.Visible)
        {
            _ = HladajAsync();
        }
        else if (PanelSuborov.Visibility == Visibility.Visible && _zobrazenyDisk is { } disk && _zobrazenyPriecinok is { } priecinok)
        {
            ZobrazPriecinok(disk, priecinok);
        }
    }

    private void ZobrazPriebehSkenov()
    {
        var beziace = _skeny?.Beziace ?? [];
        if (beziace.Count == 0)
        {
            PriebehSkenu.Visibility = Visibility.Collapsed;
            TlacidloZrusitSken.Visibility = Visibility.Collapsed;
            return;
        }

        var sken = beziace[0];
        var p = sken.Priebeh;
        var dalsie = beziace.Count > 1 ? $" (+ {beziace.Count - 1} ďalšie)" : "";
        StavUloh.Text = p is null
            ? $"Skenujem disk {sken.NazovDisku}…{dalsie}"
            : $"Skenujem disk {sken.NazovDisku}: {(p.Podiel is { } podiel ? $"{podiel * 100:0} % · " : "")}{Format.Mnozne(p.Subory, "súbor", "súbory", "súborov")}{dalsie}";
        PriebehSkenu.Visibility = Visibility.Visible;
        PriebehSkenu.IsIndeterminate = p?.Podiel is null;
        PriebehSkenu.Value = (p?.Podiel ?? 0) * 100;
        TlacidloZrusitSken.Visibility = Visibility.Visible;
        TlacidloZrusitSken.Tag = sken.DiskId;
    }

    private void ZrusitSken_Click(object sender, RoutedEventArgs e)
    {
        if (TlacidloZrusitSken.Tag is long diskId)
        {
            _skeny?.Zrus(diskId);
        }
    }

    private void OtvoritVPrieskumnikovi_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is not { } diskId || _stav?.Cesta(diskId) is not { } koren || Kniznica is not { } kniznica)
        {
            return;
        }

        var cesta = koren;
        if (_zobrazenyDisk == diskId && _zobrazenyPriecinok is { } priecinok)
        {
            using var c = kniznica.OtvorCitanie();
            cesta = PlnaCesta(koren, KatalogyADisky.Cesta(c, priecinok));
        }

        if (Directory.Exists(cesta))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + cesta + "\"") { UseShellExecute = true });
        }
    }

    private async void UpravitDisk_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is not { } diskId || Kniznica is not { } kniznica)
        {
            return;
        }

        Disk? disk;
        IReadOnlyList<KatalogKniznice> katalogy;
        HashSet<int> cisla;
        using (var c = kniznica.OtvorCitanie())
        {
            disk = KatalogyADisky.NajdiDisk(c, diskId);
            katalogy = KatalogyADisky.Katalogy(c);
            cisla = KatalogyADisky.Disky(c).Select(d => d.Cislo).ToHashSet();
        }

        if (disk is null)
        {
            return;
        }

        var dialog = new UpravDiskOkno(disk, katalogy, cisla) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Vysledok is { } v)
        {
            await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.UpravDisk(s, diskId, v.Nazov, v.KatalogId, v.Cislo, v.Umiestnenie, v.Poznamka));
            ObnovStrom();
            VyberDisk(diskId);
        }
    }

    private async void OdstranitDisk_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is not { } diskId || Kniznica is not { } kniznica)
        {
            return;
        }

        Disk? disk;
        Suhrn suhrn;
        using (var c = kniznica.OtvorCitanie())
        {
            disk = KatalogyADisky.NajdiDisk(c, diskId);
            suhrn = KatalogyADisky.SuhrnDisku(c, diskId);
        }

        if (disk is null || MessageBox.Show(this,
                $"Odstrániť disk {disk.CisloText} ({disk.Nazov}) z knižnice?\n\nZ katalógu zmizne záznam o súboroch disku ({Format.Pocet(suhrn.Subory)}). " +
                "Samotný disk sa nezmení. Pred odstránením sa urobí záloha knižnice.",
                Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await ZalohujAsync(kniznica);
        await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.ZmazDisk(s, diskId));
        ObnovStrom();
        ZobrazPrazdny("Disky", "Disk je odstránený z knižnice", "Ak ho budeš znova potrebovať, pridaj ho tlačidlom „Pridať disk“.");
    }

    private async void NovyKatalog_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is { } kniznica && VstupOkno.Opytaj(this, "Nový katalóg", "Názov katalógu (napr. Videotéka, Zálohy):") is { } nazov)
        {
            await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.VytvorKatalog(s, nazov));
            ObnovStrom();
        }
    }

    private async void PremenovatKatalog_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || VybranyKatalog() is not { } id)
        {
            return;
        }

        var stary = _strom.FirstOrDefault(k => k.Id == id)?.Nazov ?? "";
        if (VstupOkno.Opytaj(this, "Premenovať katalóg", "Nový názov katalógu:", stary) is { } nazov)
        {
            await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.PremenujKatalog(s, id, nazov));
            ObnovStrom();
        }
    }

    private async void ZmazatKatalog_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || VybranyKatalog() is not { } id)
        {
            return;
        }

        var uzol = _strom.FirstOrDefault(k => k.Id == id);
        if (uzol?.Deti.Any(d => _skeny?.Bezi(d.Id) == true) == true)
        {
            MessageBox.Show(this, "Najprv počkaj, kým dobehne sken jeho disku.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"Zmazať katalóg „{uzol?.Nazov}“ aj s {uzol?.Deti.Count} diskami z knižnice?\n\nSamotné disky sa nezmenia. Pred zmazaním sa urobí záloha knižnice.",
                Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await ZalohujAsync(kniznica);
        await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.ZmazKatalog(s, id));
        ObnovStrom();
        ZobrazPrazdny("Disky", "Katalóg je zmazaný", "Zálohu knižnice nájdeš v priečinku Zalohy vedľa knižnice.");
    }

    /// <summary>A numbered backup before removing anything from the library.</summary>
    private static async Task ZalohujAsync(Kniznica kniznica)
    {
        await kniznica.Zapis.VyprazdniAsync();
        await Task.Run(() =>
        {
            using var c = kniznica.OtvorCitanie();
            ZalohyKniznice.Vedla(kniznica.Cesta).Vytvor(c, kniznica.VerziaSchemy);
        });
    }

    // ------------------------------------------------------------------ connected disks

    /// <summary>
    /// Finds which disks are connected now. A disk connected since the last look is announced (with an offer to update it),
    /// a volume recognised only uncertainly is confirmed by the user.
    /// </summary>
    internal async Task ObnovPripojenieAsync(bool oznamit)
    {
        if (Kniznica is not { } kniznica || _stav is null)
        {
            return;
        }

        IReadOnlyList<Disk> disky;
        using (var c = kniznica.OtvorCitanie())
        {
            disky = KatalogyADisky.Disky(c);
        }

        var (nove, neiste) = await _stav.ObnovAsync(disky);
        foreach (var najdeny in neiste)
        {
            if (MessageBox.Show(this,
                    $"Pripojil sa disk {najdeny.Zvazok.Koren} ({najdeny.Zvazok.Identita.Menovka}).\n\nJe to disk {najdeny.Disk.CisloText} – {najdeny.Disk.Nazov} z tvojej knižnice?",
                    Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _stav.Potvrd(najdeny);
                nove = [.. nove, najdeny];
            }
        }

        foreach (var najdeny in nove)
        {
            var identita = najdeny.Zvazok.Identita;
            await kniznica.Zapis.ZapisAsync(s => KatalogyADisky.ZapisPripojenie(s, najdeny.Disk.Id, identita, najdeny.Zvazok.Volne));
        }

        foreach (var uzol in _strom.SelectMany(k => k.Deti))
        {
            uzol.Pripojeny = _stav.Pripojene.ContainsKey(uzol.Id);
        }

        ObnovStred();

        // a disk someone waits for to play is the reason it was connected: it plays, no offer to update it
        if (await PrehrajCakajuceAsync() || _cakajuce is not null)
        {
            return;
        }

        if (oznamit && nove.FirstOrDefault(n => _skeny?.Bezi(n.Disk.Id) == false) is { } prvy)
        {
            OznamAkcia.Visibility = Visibility.Visible;
            OznamZavriet.Content = "Neskôr";
            _diskOznamu = prvy.Disk.Id;
            var dalsie = nove.Count > 1 ? $" (pripojené sú aj ďalšie: {nove.Count - 1})" : "";
            OznamText.Text = $"Disk {prvy.Disk.CisloText} – {prvy.Disk.Nazov} je pripojený ({prvy.Cesta}). Aktualizovať jeho obsah v katalógu?{dalsie}";
            Oznam.Visibility = Visibility.Visible;
        }
    }

    private async void OznamAkcia_Click(object sender, RoutedEventArgs e)
    {
        if (_diskOznamu is { } diskId)
        {
            Oznam.Visibility = Visibility.Collapsed;
            await SkenujAsync(diskId);
        }
    }

    private void OznamZavriet_Click(object sender, RoutedEventArgs e)
    {
        // "Zrušiť" of waiting for a disk to play
        _cakajuce = null;
        Oznam.Visibility = Visibility.Collapsed;
    }

    private void SkryOznam(long diskId)
    {
        if (_diskOznamu == diskId)
        {
            Oznam.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Windows reports connected and removed volumes to every top-level window; the look waits 1.5 s for quiet.</summary>
    private IntPtr SpravyOkna(IntPtr okno, int sprava, IntPtr wParam, IntPtr lParam, ref bool spracovane)
    {
        if (sprava == WmDeviceChange && (wParam.ToInt64() == DbtDeviceArrival || wParam.ToInt64() == DbtDeviceRemoveComplete))
        {
            _odkladZmenyZariadeni.Stop();
            _odkladZmenyZariadeni.Start();
        }

        return IntPtr.Zero;
    }
}
