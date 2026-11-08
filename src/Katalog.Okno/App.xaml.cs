using System.Windows;
using System.Windows.Threading;

namespace Katalog.Okno;

public partial class App : Application
{
    /// <summary>Switch used by GitHub Actions: run the self-test, write its log and screenshots, exit with 0 or 1.</summary>
    public const string PrepinacSamotestu = "--samotest";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += NeosetrenaChyba;

        if (e.Args.Contains(PrepinacSamotestu, StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(await Samotest.SpustiAsync());
            return;
        }

        var nastavenia = Nastavenia.Nacitaj();
        NastavMotiv(nastavenia.Motiv);
        var okno = new HlavneOkno(nastavenia, nastavenia.SkutocnaCestaKniznice);
        MainWindow = okno;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        okno.Show();
    }

    /// <summary>Light, dark or as Windows has it.</summary>
    public static void NastavMotiv(string motiv) =>
        Current.ThemeMode = motiv switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    private static void NeosetrenaChyba(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // the program must not vanish with a half-written library: the writer commits whole batches only, and the user
        // sees what happened
        MessageBox.Show(e.Exception.Message, Jadro.Produkt.Nazov + " – chyba", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
