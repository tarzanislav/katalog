using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Katalog.Data;
using Katalog.Jadro;
using Katalog.Jadro.Model;
using Katalog.Vystupy;
using Microsoft.Win32;

namespace Katalog.Okno;

/// <summary>A loan as a row of the list Požičané.</summary>
internal sealed record RiadokVypozicky(Vypozicka Vypozicka)
{
    public string Co => Vypozicka.Co;

    public string Komu => Vypozicka.Komu;

    public string OdText => Format.Datum(Vypozicka.Od);

    public string DoText => Vypozicka.Do is { } d ? Format.Datum(d) : "";

    public string? Poznamka => Vypozicka.Poznamka;
}

/// <summary>
/// Outputs (stage E6): every list exports to Excel, CSV, HTML and PDF and prints; labels with QR codes for disks and
/// titles; a catalogue exports to its own file and imports from one; loans (N6) and the user's tags of titles.
/// </summary>
public partial class HlavneOkno
{
    // ------------------------------------------------------------------ tables of the lists

    /// <summary>The search results as a table (the self-test uses it).</summary>
    internal Tabulka TabulkaVysledkov()
    {
        var polozky = ZoznamVysledkov.ItemsSource as IEnumerable<PolozkaVysledku> ?? [];
        return new Tabulka(
            "Hľadanie: " + PoleHladania.Text.Trim(),
            [
                new StlpecTabulky("Názov", Sirka: 3),
                new StlpecTabulky("Disk", Sirka: 0.6),
                new StlpecTabulky("Názov disku", Sirka: 1.2),
                new StlpecTabulky("Priečinok", Sirka: 2.5),
                new StlpecTabulky("Veľkosť", DruhStlpca.Velkost),
                new StlpecTabulky("Zmenené", DruhStlpca.Datum),
            ],
            polozky.Select(p => new object?[]
            {
                p.Nazov, "D" + p.Disk.CisloText, p.Disk.Nazov, p.CestaText, p.JePriecinok ? null : p.Polozka.Velkost,
                p.Polozka.Zmeneny is { } z ? Datum(z) : null,
            }).ToList())
        {
            Popis = $"{Produkt.Nazov} · {Format.Cas(Cas.Teraz())}",
        };
    }

    private Tabulka TabulkaPriecinka()
    {
        var polozky = ZoznamSuborov.ItemsSource as IEnumerable<PolozkaZoznamu> ?? [];
        return new Tabulka(
            NadpisPriecinka.Text,
            [
                new StlpecTabulky("Názov", Sirka: 4),
                new StlpecTabulky("Typ", Sirka: 0.8),
                new StlpecTabulky("Veľkosť", DruhStlpca.Velkost),
                new StlpecTabulky("Zmenené", DruhStlpca.Datum),
            ],
            polozky.Select(p => new object?[] { p.Nazov, p.Typ, p.JePriecinok ? null : p.Velkost, p.Zmeneny is { } z ? Datum(z) : null }).ToList())
        {
            Popis = PopisPriecinka.Text,
        };
    }

    /// <summary>The titles shown in the library as a table, with the disks they lie on (the self-test uses it).</summary>
    internal Tabulka TabulkaKniznice()
    {
        var tituly = _polozkySteny.Where(p => p.Titul is not null).Select(p => p.Titul!).ToList();
        if (tituly.Count == 0)
        {
            // collections, HISTRA lists, tags: their names and what they hold
            return new Tabulka(NadpisKniznice.Text, [new StlpecTabulky("Názov", Sirka: 3), new StlpecTabulky("Obsah", Sirka: 2)],
                _polozkySteny.Select(p => new object?[] { p.Nazov, p.Podnadpis }).ToList());
        }

        var disky = DiskyTitulov(tituly.Select(t => t.Id));
        return new Tabulka(
            NadpisKniznice.Text,
            [
                new StlpecTabulky("Názov", Sirka: 3),
                new StlpecTabulky("Originál", Sirka: 2),
                new StlpecTabulky("Rok", DruhStlpca.Rok),
                new StlpecTabulky("Druh", Sirka: 0.6),
                new StlpecTabulky("Kvalita", Sirka: 0.6),
                new StlpecTabulky("Hodnotenie", DruhStlpca.Desatinne),
                new StlpecTabulky("Moje", DruhStlpca.Cislo),
                new StlpecTabulky("Verzie", DruhStlpca.Cislo),
                new StlpecTabulky("Disky", Sirka: 1.2),
                new StlpecTabulky("Videné", Sirka: 0.5),
            ],
            tituly.Select(t => new object?[]
            {
                t.Nazov, t.Original is { } o && o != t.Nazov ? o : null, t.Rok is { } r ? (long)r : null,
                t.Druh == DruhTitulu.Serial ? "seriál" : "film",
                t.Kvalita switch { TriedaKvality.UHD4K => "4K", TriedaKvality.Nezistena => null, var k => k.ToString() },
                t.Hodnotenie, t.MojeHodnotenie is { } m ? (long)m : null, t.Verzie > 0 ? (long)t.Verzie : null,
                disky.TryGetValue(t.Id, out var d) ? string.Join(", ", d) : null,
                t.Videne ? "áno" : null,
            }).ToList())
        {
            Popis = (FilterKniznice.Text.Trim() is { Length: > 0 } f ? f + " · " : "") + $"{Produkt.Nazov} · {Format.Cas(Cas.Teraz())}",
        };
    }

    /// <summary>The overview of disks as a table (the self-test uses it).</summary>
    internal Tabulka TabulkaPrehladu() => new(
        "Prehľad diskov",
        [
            new StlpecTabulky("Disk", Sirka: 0.5),
            new StlpecTabulky("Názov", Sirka: 2),
            new StlpecTabulky("Katalóg", Sirka: 1.2),
            new StlpecTabulky("Kapacita", DruhStlpca.Velkost),
            new StlpecTabulky("Voľné", DruhStlpca.Velkost),
            new StlpecTabulky("Súbory", DruhStlpca.Cislo),
            new StlpecTabulky("Tituly", DruhStlpca.Cislo),
            new StlpecTabulky("Len tu", DruhStlpca.Cislo),
            new StlpecTabulky("Posledný sken", DruhStlpca.Datum),
            new StlpecTabulky("Leží", Sirka: 1.2),
        ],
        _prehlad.Select(r => new object?[]
        {
            "D" + r.Disk.CisloText, r.Disk.Nazov, r.Katalog, r.Disk.Identita.Velkost, r.Disk.Volne, r.Suborov, (long)r.Tituly, (long)r.LenTu,
            r.Disk.PoslednySken is { } s ? Datum(s) : null, r.Disk.Umiestnenie,
        }).ToList())
    {
        Popis = $"{Produkt.Nazov} · {Format.Cas(Cas.Teraz())}",
    };

    private static DateTime Datum(long tiky) => Cas.NaUtc(tiky).ToLocalTime().DateTime;

    private IReadOnlyDictionary<long, IReadOnlyList<string>> DiskyTitulov(IEnumerable<long> tituly)
    {
        if (Kniznica is not { } kniznica)
        {
            return new Dictionary<long, IReadOnlyList<string>>();
        }

        using var c = kniznica.OtvorCitanie();
        return KniznicaTitulov.DiskyTitulov(c, tituly);
    }

    /// <summary>Writes a table in the format of the file's extension (.xlsx, .csv, .html, .pdf).</summary>
    internal static void ZapisTabulku(Tabulka tabulka, string cesta)
    {
        var pripona = Path.GetExtension(cesta).ToLowerInvariant();
        if (pripona == ".pdf")
        {
            KreslenieStran.UlozPdf(StranyTabulky.Rozloz(tabulka, NaSirku(tabulka), DateTime.Now), cesta, tabulka.Nazov);
            return;
        }

        var docasny = cesta + ".tmp";
        using (var subor = File.Create(docasny))
        {
            switch (pripona)
            {
                case ".csv":
                    ZapisTabulky.Csv(tabulka, subor);
                    break;
                case ".html" or ".htm":
                    ZapisTabulky.Html(tabulka, subor);
                    break;
                default:
                    ZapisTabulky.Xlsx(tabulka, subor);
                    break;
            }
        }

        File.Move(docasny, cesta, overwrite: true);
    }

    private static bool NaSirku(Tabulka tabulka) => tabulka.Stlpce.Count > 5;

    private void Exportuj(Tabulka tabulka)
    {
        if (tabulka.Riadky.Count == 0)
        {
            MessageBox.Show(this, "Zoznam je prázdny, nie je čo exportovať.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportovať",
            Filter = "Excel (*.xlsx)|*.xlsx|CSV pre Excel (*.csv)|*.csv|Webová stránka (*.html)|*.html|PDF (*.pdf)|*.pdf",
            FileName = BezpecnyNazov(tabulka.Nazov),
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ZapisTabulku(tabulka, dialog.FileName);
            StavUloh.Text = $"Exportované: {dialog.FileName} ({Format.Mnozne(tabulka.Riadky.Count, "riadok", "riadky", "riadkov")})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Súbor sa nepodarilo uložiť: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Tlac(Tabulka tabulka)
    {
        if (tabulka.Riadky.Count == 0)
        {
            MessageBox.Show(this, "Zoznam je prázdny, nie je čo tlačiť.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TlacStran.Tlac(StranyTabulky.Rozloz(tabulka, NaSirku(tabulka), DateTime.Now), tabulka.Nazov);
    }

    /// <summary>A file name of a title ("Hľadanie: typ:video" → "Hľadanie typ video").</summary>
    private static string BezpecnyNazov(string nazov)
    {
        var zakazane = Path.GetInvalidFileNameChars();
        var text = new string(nazov.Select(c => zakazane.Contains(c) || c == ':' ? ' ' : c).ToArray());
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? Produkt.Nazov : text.Length > 80 ? text[..80].TrimEnd() : text;
    }

    private void ExportovatVysledky_Click(object sender, RoutedEventArgs e) => Exportuj(TabulkaVysledkov());

    private void TlacitVysledky_Click(object sender, RoutedEventArgs e) => Tlac(TabulkaVysledkov());

    private void ExportovatPriecinok_Click(object sender, RoutedEventArgs e) => Exportuj(TabulkaPriecinka());

    private void ExportovatKniznicu_Click(object sender, RoutedEventArgs e) => Exportuj(TabulkaKniznice());

    private void TlacitKniznicu_Click(object sender, RoutedEventArgs e) => Tlac(TabulkaKniznice());

    private async void ExportovatPrehlad_Click(object sender, RoutedEventArgs e)
    {
        if (_prehlad.Count == 0)
        {
            await ZobrazPrehladAsync();
        }

        Exportuj(TabulkaPrehladu());
    }

    private void VystupKniznice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } ponuka } tlacidlo)
        {
            ponuka.PlacementTarget = tlacidlo;
            ponuka.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ponuka.IsOpen = true;
        }
    }

    // ------------------------------------------------------------------ labels

    /// <summary>Labels of disks: number, name, catalogue, size and contents, and a QR code with the same (the self-test uses it).</summary>
    internal IReadOnlyList<UdajeStitku> StitkyDiskov(IEnumerable<long> disky)
    {
        if (Kniznica is not { } kniznica)
        {
            return [];
        }

        var vybrane = disky.ToHashSet();
        using var c = kniznica.OtvorCitanie();
        return Prehlady.Disky(c).Where(r => vybrane.Contains(r.Disk.Id)).Select(r =>
        {
            var cislo = "D" + r.Disk.CisloText;
            var kapacita = r.Disk.Identita.Velkost is > 0 and var v ? Format.Velkost(v) : null;
            var riadky = new List<string>
            {
                "Katalóg " + r.Katalog,
                string.Join(" · ", new[] { kapacita, Format.Mnozne(r.Suborov, "súbor", "súbory", "súborov"), r.Tituly > 0 ? Format.Mnozne(r.Tituly, "titul", "tituly", "titulov") : null }
                    .Where(t => t is not null)),
            };
            if (r.Disk.Umiestnenie is { Length: > 0 } u)
            {
                riadky.Add("Leží: " + u);
            }

            return new UdajeStitku(cislo, r.Disk.Nazov, riadky, $"{Produkt.Nazov} {cislo}\n{r.Disk.Nazov}\nKatalóg {r.Katalog}");
        }).ToList();
    }

    /// <summary>Labels of titles for their covers: the disks they lie on, the name, year, kind and quality (the self-test uses it).</summary>
    internal IReadOnlyList<UdajeStitku> StitkyNaTituly(IReadOnlyList<PolozkaKniznice> tituly)
    {
        var disky = DiskyTitulov(tituly.Select(t => t.Id));
        return tituly.Select(t =>
        {
            var naDiskoch = disky.TryGetValue(t.Id, out var d) ? string.Join(" · ", d) : "";
            var rok = t.Rok?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var riadky = new List<string>
            {
                string.Join(" · ", new[]
                {
                    rok, t.Druh == DruhTitulu.Serial ? "seriál" : "film",
                    t.Kvalita switch { TriedaKvality.UHD4K => "4K", TriedaKvality.Nezistena => null, var k => k.ToString() },
                }.Where(x => x is not null)),
            };
            if (t.Original is { } o && o != t.Nazov)
            {
                riadky.Add(o);
            }

            var nazov = t.Nazov + (rok is null ? "" : $" ({rok})");
            return new UdajeStitku(naDiskoch.Length > 0 ? naDiskoch : t.Nazov, t.Nazov, riadky,
                $"{Produkt.Nazov}\n{nazov}" + (naDiskoch.Length > 0 ? "\n" + naDiskoch : ""));
        }).ToList();
    }

    private void StitkyDiskov_Click(object sender, RoutedEventArgs e)
    {
        var vybrane = ZoznamPrehladu.SelectedItems.OfType<RiadokPrehladuOkna>().Select(r => r.Riadok.Disk.Id).ToList();
        var disky = vybrane.Count > 0 ? vybrane : _prehlad.Select(r => r.Disk.Id).ToList();
        StitkyOkno.Ukaz(this, _nastavenia, disky.Count == 1 ? "Štítok disku" : "Štítky diskov", StitkyDiskov(disky), "Štítky diskov");
    }

    private void StitkyZoStromu_Click(object sender, RoutedEventArgs e)
    {
        if (VybranyDisk() is { } diskId && VybratyUzol?.Druh != DruhUzla.Katalog)
        {
            StitkyOkno.Ukaz(this, _nastavenia, "Štítok disku", StitkyDiskov([diskId]), "Štítok disku");
        }
        else if (VybranyKatalog() is { } katalogId && _strom.FirstOrDefault(k => k.Id == katalogId) is { } katalog)
        {
            StitkyOkno.Ukaz(this, _nastavenia, $"Štítky diskov – {katalog.Nazov}", StitkyDiskov(katalog.Deti.Select(d => d.Id)), "Štítky " + katalog.Nazov);
        }
    }

    private void StitkyTitulov_Click(object sender, RoutedEventArgs e)
    {
        var tituly = _vybranyTitul is { } id && _polozkySteny.FirstOrDefault(p => p.Titul?.Id == id)?.Titul is { } vybrany
            ? [vybrany]
            : _polozkySteny.Where(p => p.Titul is not null).Select(p => p.Titul!).ToList();
        StitkyOkno.Ukaz(this, _nastavenia, tituly.Count == 1 ? "Štítok titulu" : "Štítky titulov", StitkyNaTituly(tituly), "Štítky titulov");
    }

    // ------------------------------------------------------------------ catalogue export and import

    private async void ExportovatKatalog_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || VybranyKatalog() is not { } id)
        {
            return;
        }

        var nazov = _strom.FirstOrDefault(k => k.Id == id)?.Nazov ?? "Katalóg";
        var dialog = new SaveFileDialog
        {
            Title = "Exportovať katalóg",
            Filter = $"Knižnica {Produkt.Nazov} (*{Produkt.PriponaKniznice})|*{Produkt.PriponaKniznice}",
            FileName = BezpecnyNazov(nazov) + Produkt.PriponaKniznice,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await kniznica.Zapis.VyprazdniAsync();
            await PrenosKatalogu.ExportujAsync(kniznica, id, dialog.FileName);
            StavUloh.Text = $"Katalóg „{nazov}“ je exportovaný: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(this, "Katalóg sa nepodarilo exportovať: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async void ImportovatKatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importovať katalóg",
            Filter = $"Knižnica {Produkt.Nazov} (*{Produkt.PriponaKniznice})|*{Produkt.PriponaKniznice}|Všetky súbory (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ImportujKatalogAsync(dialog.FileName, opytat: true);
        }
    }

    /// <summary>Imports the catalogues of a library file (the self-test calls it without asking).</summary>
    internal async Task<VysledokImportu?> ImportujKatalogAsync(string subor, bool opytat)
    {
        if (Kniznica is not { } kniznica)
        {
            return null;
        }

        try
        {
            var katalogy = await Task.Run(() => PrenosKatalogu.Katalogy(subor));
            if (katalogy.Count == 0)
            {
                MessageBox.Show(this, "V súbore nie je žiadny katalóg.", Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var zoznam = string.Join("\n", katalogy.Select(k => $"• {k.Nazov} – {Format.Mnozne(k.Diskov, "disk", "disky", "diskov")}, {Format.Mnozne(k.Suborov, "súbor", "súbory", "súborov")}"));
            if (opytat && MessageBox.Show(this,
                    $"Importovať zo súboru {Path.GetFileName(subor)}:\n\n{zoznam}\n\nDisky, ktoré knižnica už má, sa vynechajú; tituly, ktoré pozná, sa spoja. Pred importom sa urobí záloha knižnice.",
                    Produkt.Nazov, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                return null;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            await ZalohujAsync(kniznica);
            var vysledok = await PrenosKatalogu.ImportujAsync(kniznica, subor);
            ObnovStrom();
            await ObnovPoctyKnizniceAsync();
            Mouse.OverrideCursor = null;

            var text = vysledok.Katalogy.Count == 0
                ? "Nič nové: knižnica už má všetky disky zo súboru."
                : $"Importované: {string.Join(", ", vysledok.Katalogy)} – {Format.Mnozne(vysledok.Diskov, "disk", "disky", "diskov")}, " +
                  $"{Format.Mnozne(vysledok.Suborov, "súbor", "súbory", "súborov")}, nové tituly {Format.Pocet(vysledok.NovychTitulov)}, " +
                  $"spojené so známymi {Format.Pocet(vysledok.ZlucenychTitulov)}.";
            if (vysledok.Preskocene.Count > 0)
            {
                text += $"\n\nVynechané (knižnica ich už má): {string.Join(", ", vysledok.Preskocene)}.";
            }

            if (vysledok.Precislovane.Count > 0)
            {
                text += $"\n\nNové čísla diskov (pôvodné boli obsadené – oprav ich štítky): {string.Join(", ", vysledok.Precislovane)}.";
            }

            StavUloh.Text = text.Split('\n')[0];
            if (opytat)
            {
                MessageBox.Show(this, text, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // new titles get their posters and details from the sources
            _ = DoplnKnizniciuAsync();
            return vysledok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CudziSuborException or NovsiaKniznicaException
                                       or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, "Import sa nepodaril: " + ex.Message, Produkt.Nazov, MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ------------------------------------------------------------------ loans (N6)

    private Vypozicka? PozicanyDisk(long diskId)
    {
        if (Kniznica is not { } kniznica)
        {
            return null;
        }

        using var c = kniznica.OtvorCitanie();
        return Vypozicky.Otvorena(c, diskId, null);
    }

    private async void PozicatDisk_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || VybranyDisk() is not { } diskId)
        {
            return;
        }

        if (PozicanyDisk(diskId) is { } vypozicka)
        {
            await kniznica.Zapis.ZapisAsync(s => Vypozicky.Vrat(s, vypozicka.Id, Cas.Teraz()));
            StavUloh.Text = $"Disk {vypozicka.Co} je vrátený.";
        }
        else
        {
            using var c = kniznica.OtvorCitanie();
            var disk = KatalogyADisky.NajdiDisk(c, diskId);
            if (disk is null || PozicatOkno.Opytaj(this, $"Disk D{disk.CisloText} {disk.Nazov}", Vypozicky.Mena(c)) is not { } odpoved)
            {
                return;
            }

            await kniznica.Zapis.ZapisAsync(s => Vypozicky.Pozicaj(s, diskId, null, odpoved.Komu, odpoved.Poznamka, Cas.Teraz()));
            StavUloh.Text = $"Disk D{disk.CisloText} je požičaný: {odpoved.Komu}.";
        }

        if (PanelVypoziciek.Visibility == Visibility.Visible)
        {
            await ZobrazVypozickyAsync();
        }
        else if (_zobrazenyDisk == diskId || VybranyDisk() == diskId)
        {
            using var c = kniznica.OtvorCitanie();
            ZobrazDetail(KatalogyADisky.NajdiDisk(c, diskId));
        }
    }

    private async void PozicatTitul_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _vybranyTitul is not { } titulId || _detail is not { } detail)
        {
            return;
        }

        if (_detailVypozicka is { } vypozicka)
        {
            await kniznica.Zapis.ZapisAsync(s => Vypozicky.Vrat(s, vypozicka.Id, Cas.Teraz()));
        }
        else
        {
            IReadOnlyList<string> mena;
            using (var c = kniznica.OtvorCitanie())
            {
                mena = Vypozicky.Mena(c);
            }

            var nazov = detail.Polozka.Nazov + (detail.Polozka.Rok is { } r ? $" ({r})" : "");
            if (PozicatOkno.Opytaj(this, nazov, mena) is not { } odpoved)
            {
                return;
            }

            await kniznica.Zapis.ZapisAsync(s => Vypozicky.Pozicaj(s, null, titulId, odpoved.Komu, odpoved.Poznamka, Cas.Teraz()));
        }

        await ZobrazDetailTituluAsync(titulId);
    }

    /// <summary>The list of loans: what is lent now, or with the history (the self-test awaits it).</summary>
    internal async Task ZobrazVypozickyAsync()
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        UkazStred(PanelVypoziciek);
        _zobrazenyDisk = null;
        _zobrazenyPriecinok = null;
        ZobrazDetail(null);
        var aj = AjVratene.IsChecked == true;
        IReadOnlyList<Vypozicka> vypozicky;
        try
        {
            vypozicky = await Task.Run(() =>
            {
                using var c = kniznica.OtvorCitanie();
                return aj ? Vypozicky.Vsetky(c) : Vypozicky.Otvorene(c);
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return;
        }

        ZoznamVypoziciek.ItemsSource = vypozicky.Select(v => new RiadokVypozicky(v)).ToList();
        var otvorene = vypozicky.Count(v => v.Otvorena);
        PocetVypoziciek.Text = otvorene > 0 ? $"požičané: {Format.Pocet(otvorene)}" : "";
        ZiadnaVypozicka.Visibility = vypozicky.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NavigaciaPozicane.Content = otvorene > 0 ? $"Požičané ({Format.Pocet(otvorene)})" : "Požičané";
        ZoznamVypoziciek_SelectionChanged(ZoznamVypoziciek, null!);
    }

    private void ZoznamVypoziciek_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vybrane = ZoznamVypoziciek.SelectedItems.OfType<RiadokVypozicky>().ToList();
        TlacidloVratit.IsEnabled = vybrane.Any(v => v.Vypozicka.Otvorena);
        TlacidloZmazatVypozicku.IsEnabled = vybrane.Count > 0;
    }

    private async void AjVratene_Changed(object sender, RoutedEventArgs e) => await ZobrazVypozickyAsync();

    private async void VratitVypozicku_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        var vybrane = ZoznamVypoziciek.SelectedItems.OfType<RiadokVypozicky>().Where(v => v.Vypozicka.Otvorena).Select(v => v.Vypozicka.Id).ToList();
        var teraz = Cas.Teraz();
        await kniznica.Zapis.ZapisAsync(s =>
        {
            foreach (var id in vybrane)
            {
                Vypozicky.Vrat(s, id, teraz);
            }
        });
        await ZobrazVypozickyAsync();
    }

    private async void ZmazatVypozicku_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica)
        {
            return;
        }

        var vybrane = ZoznamVypoziciek.SelectedItems.OfType<RiadokVypozicky>().Select(v => v.Vypozicka.Id).ToList();
        if (vybrane.Count == 0 || MessageBox.Show(this, $"Zmazať {Format.Mnozne(vybrane.Count, "záznam", "záznamy", "záznamov")} o požičaní? (Nič sa tým nevráti – na to je tlačidlo Vrátené.)",
                Produkt.Nazov, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await kniznica.Zapis.ZapisAsync(s =>
        {
            foreach (var id in vybrane)
            {
                Vypozicky.Zmaz(s, id);
            }
        });
        await ZobrazVypozickyAsync();
    }

    private void ExportovatVypozicky_Click(object sender, RoutedEventArgs e)
    {
        var riadky = ZoznamVypoziciek.ItemsSource as IEnumerable<RiadokVypozicky> ?? [];
        Exportuj(new Tabulka(
            "Požičané",
            [
                new StlpecTabulky("Čo", Sirka: 3),
                new StlpecTabulky("Komu", Sirka: 1.5),
                new StlpecTabulky("Od", DruhStlpca.Datum),
                new StlpecTabulky("Vrátené", DruhStlpca.Datum),
                new StlpecTabulky("Poznámka", Sirka: 2),
            ],
            riadky.Select(r => new object?[]
            {
                r.Co, r.Komu, Datum(r.Vypozicka.Od).Date, r.Vypozicka.Do is { } d ? Datum(d).Date : null, r.Poznamka,
            }).ToList())
        {
            Popis = $"{Produkt.Nazov} · {Format.Cas(Cas.Teraz())}",
        });
    }

    // ------------------------------------------------------------------ tags of titles

    private async void StitkyTitulu_Click(object sender, RoutedEventArgs e)
    {
        if (Kniznica is not { } kniznica || _vybranyTitul is not { } titulId)
        {
            return;
        }

        if (VstupOkno.Opytaj(this, "Štítky titulu", "Štítky oddelené čiarkou (napr. Vianoce, Pre deti). Prázdne pole štítky odoberie:",
                string.Join(", ", _detailStitky), prazdne: true) is not { } text)
        {
            return;
        }

        var nazvy = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await kniznica.Zapis.ZapisAsync(s => StitkyTitulov.Nastav(s, [titulId], nazvy));
        await ZobrazDetailTituluAsync(titulId);
    }
}
