using System.Diagnostics;
using System.Globalization;
using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;
using Microsoft.Data.Sqlite;

namespace Katalog.Ulohy;

/// <summary>Progress of a scan. <see cref="Podiel"/> is 0 to 1, or null when the size of the whole is not known yet.</summary>
public sealed record PriebehSkenu(double? Podiel, long Subory, long Priecinky, long Bajty, string Cesta);

/// <summary>Result of a scan: how it ended and what it changed in the library.</summary>
public sealed record VysledokSkenu(StavUlohy Stav, PocetZmien Zmeny, long Subory, long Priecinky, int Chyby, TimeSpan Trvanie, string? Sprava);

/// <summary>
/// Scans a disk into the library: walks its storage (on a low-priority thread), compares every folder with what the library
/// holds and writes only the differences (stage E1, difference update). It may be cancelled at any moment: only folders
/// already compared are changed, so the library stays consistent and the next scan simply goes on.
/// </summary>
public static class SkenovanieDisku
{
    /// <summary>How many folder writes may wait for the writer before the walk slows down (bounds the memory).</summary>
    private const int NajviacCakajucich = 512;

    public static async Task<VysledokSkenu> SpustiAsync(
        Kniznica kniznica, long diskId, IUlozisko ulozisko, IProgress<PriebehSkenu>? priebeh, CancellationToken zrusenie)
    {
        var cas = Stopwatch.StartNew();
        await using var citanie = await kniznica.OtvorCitanieAsync(zrusenie).ConfigureAwait(false);
        var koren = KatalogyADisky.Koren(citanie, diskId) ?? throw new InvalidOperationException("Disk nie je v knižnici.");
        var predtym = KatalogyADisky.SuhrnDisku(citanie, diskId);
        var (velkost, volne) = ulozisko.Miesto();
        var celkomBajtov = ulozisko is LokalneUlozisko { JeKorenZvazku: true } && velkost is { } v && volne is { } f ? v - f : (long?)null;

        var zaciatok = Cas.Teraz();
        var idSkenu = await kniznica.Zapis.ZapisAsync(s => Skalar(s, "INSERT INTO sken(disk_id, zacal, stav) VALUES ($disk, $zacal, 1) RETURNING id",
            ("$disk", diskId), ("$zacal", zaciatok))).ConfigureAwait(false);

        var priecinky = new Dictionary<string, long>(StringComparer.Ordinal) { [""] = koren };
        var nove = new HashSet<long>();
        var cakajuce = new List<Task<PocetZmien>>();
        var zmeny = default(PocetZmien);
        long subory = 0, pocetPriecinkov = 0, bajty = 0;
        var chyby = 0;
        var poslednaSprava = Stopwatch.StartNew();
        StavUlohy stav;
        string? sprava = null;

        try
        {
            await foreach (var obsah in Prechadzac.Spusti(ulozisko, zrusenie).ReadAllAsync(zrusenie).ConfigureAwait(false))
            {
                if (obsah.Chyba is not null)
                {
                    chyby++;
                    continue;
                }

                if (!priecinky.TryGetValue(obsah.Cesta, out var id))
                {
                    // its parent could not be compared (cannot happen in a depth-first walk; kept as a guard)
                    continue;
                }

                IReadOnlyList<UlozenaPolozka> ulozene = nove.Contains(id) ? [] : ZapisSkenu.UlozeneDeti(citanie, id);
                var rozdiel = RozdielPriecinka.Porovnaj(ulozene, obsah.Polozky);
                var idNovych = new long[rozdiel.NovePriecinky.Count];
                for (var i = 0; i < idNovych.Length; i++)
                {
                    idNovych[i] = kniznica.NovyIdPriecinka();
                    nove.Add(idNovych[i]);
                    priecinky[Spoj(obsah.Cesta, rozdiel.NovePriecinky[i].Nazov)] = idNovych[i];
                }

                foreach (var (idPodpriecinka, nazov) in rozdiel.ExistujucePriecinky)
                {
                    priecinky[Spoj(obsah.Cesta, nazov)] = idPodpriecinka;
                }

                // a compared folder is no longer needed in the map (its subfolders are)
                priecinky.Remove(obsah.Cesta);

                if (!rozdiel.JeBezZmeny)
                {
                    var teraz = Cas.Teraz();
                    var idPriecinka = id;
                    cakajuce.Add(kniznica.Zapis.ZapisAsync(s => ZapisSkenu.Zapis(s, diskId, idPriecinka, rozdiel, idNovych, teraz)));
                    if (cakajuce.Count >= NajviacCakajucich)
                    {
                        zmeny += await Dokonci(cakajuce).ConfigureAwait(false);
                    }
                }

                pocetPriecinkov++;
                foreach (var polozka in obsah.Polozky)
                {
                    if (!polozka.JePriecinok)
                    {
                        subory++;
                        bajty += polozka.Velkost;
                    }
                }

                if (priebeh is not null && poslednaSprava.ElapsedMilliseconds >= 100)
                {
                    poslednaSprava.Restart();
                    priebeh.Report(new PriebehSkenu(Podiel(subory, bajty, predtym.Subory, celkomBajtov), subory, pocetPriecinkov, bajty, obsah.Cesta));
                }
            }

            zmeny += await Dokonci(cakajuce).ConfigureAwait(false);
            stav = StavUlohy.Hotova;
        }
        catch (OperationCanceledException)
        {
            zmeny += await DokonciPoChybe(cakajuce).ConfigureAwait(false);
            stav = StavUlohy.Zrusena;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            zmeny += await DokonciPoChybe(cakajuce).ConfigureAwait(false);
            stav = StavUlohy.Chyba;
            sprava = ex.Message;
        }

        // a scan whose root could not be read changed nothing and is an error
        if (stav == StavUlohy.Hotova && pocetPriecinkov == 0)
        {
            stav = StavUlohy.Chyba;
            sprava ??= "Disk sa nedal prečítať.";
        }

        var koniec = Cas.Teraz();
        var identita = ulozisko.Identita();
        await kniznica.Zapis.ZapisAsync(s =>
        {
            Vykonaj(s, "UPDATE sken SET skoncil = $koniec, pridane = $p, zmenene = $z, zmazane = $m, chyby = $ch, stav = $stav WHERE id = $id",
                ("$koniec", koniec), ("$p", zmeny.Pridane), ("$z", zmeny.Zmenene), ("$m", zmeny.Zmazane), ("$ch", chyby), ("$stav", (int)stav), ("$id", idSkenu));
            if (stav == StavUlohy.Hotova)
            {
                Vykonaj(s, "UPDATE disk SET posledny_sken = $koniec WHERE id = $id", ("$koniec", koniec), ("$id", diskId));
            }

            if (identita is not null)
            {
                KatalogyADisky.ZapisPripojenie(s, diskId, identita, ulozisko.Miesto().Volne);
            }
        }).ConfigureAwait(false);

        priebeh?.Report(new PriebehSkenu(stav == StavUlohy.Hotova ? 1 : null, subory, pocetPriecinkov, bajty, ""));
        return new VysledokSkenu(stav, zmeny, subory, pocetPriecinkov, chyby, cas.Elapsed, sprava);
    }

    /// <summary>
    /// Share of the scan done: by files when the disk was scanned before, else by bytes of the used space of a whole volume;
    /// never 1 before the end (the last folders may still come).
    /// </summary>
    internal static double? Podiel(long subory, long bajty, long predtymSuborov, long? celkomBajtov)
    {
        double? podiel = predtymSuborov > 0 ? (double)subory / predtymSuborov
            : celkomBajtov is > 0 ? (double)bajty / celkomBajtov.Value
            : null;
        return podiel is { } p ? Math.Min(p, 0.99) : null;
    }

    private static string Spoj(string cesta, string nazov) => cesta.Length == 0 ? nazov : cesta + "/" + nazov;

    private static async Task<PocetZmien> Dokonci(List<Task<PocetZmien>> cakajuce)
    {
        var vysledky = await Task.WhenAll(cakajuce).ConfigureAwait(false);
        cakajuce.Clear();
        var spolu = default(PocetZmien);
        foreach (var v in vysledky)
        {
            spolu += v;
        }

        return spolu;
    }

    /// <summary>After a cancellation or an error the queued folder writes still finish (each folder is whole or untouched).</summary>
    private static async Task<PocetZmien> DokonciPoChybe(List<Task<PocetZmien>> cakajuce)
    {
        var spolu = default(PocetZmien);
        foreach (var uloha in cakajuce)
        {
            try
            {
                spolu += await uloha.ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // that folder stays as it was; the next scan compares it again
            }
        }

        cakajuce.Clear();
        return spolu;
    }

    private static long Skalar(SqliteConnection spojenie, string sql, params (string Nazov, object Hodnota)[] parametre)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        foreach (var (nazov, hodnota) in parametre)
        {
            prikaz.Parameters.AddWithValue(nazov, hodnota);
        }

        return Convert.ToInt64(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql, params (string Nazov, object Hodnota)[] parametre)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        foreach (var (nazov, hodnota) in parametre)
        {
            prikaz.Parameters.AddWithValue(nazov, hodnota);
        }

        prikaz.ExecuteNonQuery();
    }
}
