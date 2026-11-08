using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Aktualizacie;

namespace Katalog.Okno;

public partial class HlavneOkno : Window
{
    private readonly Nastavenia _nastavenia;
    private readonly string _cestaKniznice;
    private readonly TaskCompletionSource _pripravene = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HlavneOkno(Nastavenia nastavenia, string cestaKniznice)
    {
        _nastavenia = nastavenia;
        _cestaKniznice = cestaKniznice;
        InitializeComponent();
        // selected only now: during InitializeComponent the other list does not exist yet
        NavigaciaKniznica.SelectedIndex = 0;
        InicializujHladanie();
        Title = Produkt.Nazov;
        StavVerzie.Text = Produkt.Nazov + " " + Produkt.VerziaText;
        Loaded += async (_, _) => await PoNacitaniAsync();
        Closed += (_, _) => ZavriKniznicu();
    }

    /// <summary>The open library; null when it could not be opened.</summary>
    public Kniznica? Kniznica { get; private set; }

    /// <summary>Completes when the window has opened its library (the self-test waits for it).</summary>
    public Task Pripravene => _pripravene.Task;

    private async Task PoNacitaniAsync()
    {
        try
        {
            Kniznica = await Kniznica.OtvorAsync(_cestaKniznice);
            StavKniznice.Text = PopisKniznice(Kniznica);
            await InicializujDiskyAsync(Kniznica);
            if (!Kniznica.Kontrola.JeVPoriadku)
            {
                MessageBox.Show(this,
                    "Kontrola knižnice našla problémy:\n\n" + string.Join("\n", Kniznica.Kontrola.Problemy) +
                    "\n\nNič sa nemenilo. Obnov knižnicu z poslednej zálohy (priečinok Zalohy vedľa knižnice).",
                    Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) when (ex is NovsiaKniznicaException or CudziSuborException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            StavKniznice.Text = "Knižnicu sa nepodarilo otvoriť: " + ex.Message;
            MessageBox.Show(this, ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _pripravene.TrySetResult();
        }

        await KontrolujAktualizacieAsync(rucne: false);
    }

    private static string PopisKniznice(Kniznica kniznica)
    {
        var text = $"Knižnica: {Path.GetFileName(kniznica.Cesta)} · schéma v{kniznica.VerziaSchemy} · " +
                   (kniznica.Kontrola.JeVPoriadku ? "v poriadku" : "našla sa chyba");
        return kniznica.ZalohaPredMigraciou is { } zaloha ? text + $" · pred aktualizáciou zálohovaná ({Path.GetFileName(zaloha)})" : text;
    }

    private void ZavriKniznicu()
    {
        // running scans stop (each folder is written whole or not at all); the writer finishes its last batch and the WAL
        // is merged, so the file can be copied right away
        _skeny?.ZrusVsetky();
        if (Kniznica is { } kniznica)
        {
            Kniznica = null;
            kniznica.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private void Navigacia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox zoznam || zoznam.SelectedItem is not ListBoxItem polozka)
        {
            return;
        }

        // the two lists and the tree act as one navigation: choosing in one clears the others
        var druhy = ReferenceEquals(zoznam, NavigaciaKniznica) ? NavigaciaZbierky : NavigaciaKniznica;
        druhy.SelectedItem = null;
        if (!IsLoaded)
        {
            NadpisStredu.Text = polozka.Content as string ?? "";
            return;
        }

        ZrusVyberVStrome();
        if (ReferenceEquals(polozka, NavigaciaUlozene))
        {
            ZobrazUlozene();
            return;
        }

        ZobrazPrazdny(polozka.Content as string ?? "", "Táto časť príde v ďalších etapách",
            "Filmy, seriály a kolekcie s plagátmi prídu v etape E4, playlisty v E7, duplicity a prehľad diskov v E5. " +
            "Súbory a priečinky už nájdeš hľadaním hore.");
    }

    private async void TlacidloAktualizacie_Click(object sender, RoutedEventArgs e) => await KontrolujAktualizacieAsync(rucne: true);

    private void TlacidloOPrograme_Click(object sender, RoutedEventArgs e) => new OProgrameOkno { Owner = this }.ShowDialog();

    private async void TlacidloNastavenia_Click(object sender, RoutedEventArgs e)
    {
        var prehravace = await Task.Run(Prehravace.Najdi);
        if (new NastaveniaOkno(_nastavenia, prehravace) { Owner = this }.ShowDialog() == true)
        {
            UlozNastavenia();
        }
    }

    /// <summary>
    /// Asks GitHub for the latest release (at start once a day, or on the button). Nothing about the user is sent. A release
    /// signed with the release key can be installed right away; otherwise its page opens.
    /// </summary>
    private async Task KontrolujAktualizacieAsync(bool rucne)
    {
        if (!rucne && !PravidloAktualizacii.MaKontrolovat(_nastavenia.AutomatickeAktualizacie, _nastavenia.PoslednaKontrolaAktualizaciiUtc, DateTime.UtcNow))
        {
            return;
        }

        TlacidloAktualizacie.IsEnabled = false;
        try
        {
            using var sluzba = new SluzbaAktualizacii();
            var vydanie = await sluzba.PosledneAsync(CancellationToken.None);
            _nastavenia.PoslednaKontrolaAktualizaciiUtc = DateTime.UtcNow;
            UlozNastavenia();

            if (vydanie is null || !vydanie.JeNovsieAko(Produkt.Verzia))
            {
                if (rucne)
                {
                    MessageBox.Show(this, $"Máš najnovšiu verziu {Produkt.Nazov} {Produkt.VerziaText}.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            var verzia = vydanie.Verzia.ToString(3);
            if (!rucne && _nastavenia.VynechanaVerzia == verzia)
            {
                return;
            }

            var novinky = vydanie.Poznamky.Length > 900 ? vydanie.Poznamky[..900] + "…" : vydanie.Poznamky;
            if (vydanie.DaSaNainstalovat)
            {
                var odpoved = MessageBox.Show(this,
                    $"Je k dispozícii {Produkt.Nazov} {verzia}.\n\n{novinky}\n\n" +
                    "Áno = stiahnuť, overiť podpis a nainštalovať\nNie = túto verziu vynechať\nZrušiť = pripomenúť neskôr",
                    Produkt.Nazov + " – nová verzia", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
                if (odpoved == MessageBoxResult.Yes)
                {
                    StavKniznice.Text = $"Sťahujem a overujem {Produkt.Nazov} {verzia}…";
                    var instalator = await sluzba.StiahniAOverAsync(vydanie, CancellationToken.None);
                    // after a silent update the installer starts KATALOG again (/SPUSTIT=1)
                    Process.Start(new ProcessStartInfo(instalator, "/SILENT /SPUSTIT=1") { UseShellExecute = true });
                    Application.Current.Shutdown();
                }
                else if (odpoved == MessageBoxResult.No)
                {
                    _nastavenia.VynechanaVerzia = verzia;
                    UlozNastavenia();
                }
            }
            else if (MessageBox.Show(this,
                         $"Je k dispozícii {Produkt.Nazov} {verzia}.\n\n{novinky}\n\nOtvoriť stránku vydania?",
                         Produkt.Nazov + " – nová verzia", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(vydanie.Stranka.ToString()) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            if (rucne)
            {
                MessageBox.Show(this, "Kontrola aktualizácií sa nepodarila: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            TlacidloAktualizacie.IsEnabled = true;
        }
    }

    private void UlozNastavenia()
    {
        try
        {
            _nastavenia.Uloz();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings are a convenience: the program goes on without saving them
        }
    }
}
