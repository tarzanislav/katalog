using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Katalog.Data;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>
/// The card of a title in the detail (owner, 8. 11. 2026, after UNIVERSUM): links to the title's pages, identifiers, names,
/// facts, the versions with what their files are, and all titles of its collections. Also where the detail stands –
/// left of the library (default) or right of it – and how wide it is (a third of the space next to the menu).
/// </summary>
public partial class HlavneOkno
{
    /// <summary>At most this many versions show what their file is (a series can have hundreds of episodes).</summary>
    private const int NajviacVerziiSUdajmi = 6;

    private IReadOnlyList<(KolekciaKniznice Kolekcia, IReadOnlyList<PolozkaKniznice> Diely)> _detailKolekcie = [];
    private IReadOnlyDictionary<long, TechnickeUdajeSuboru> _detailTechnicke = new Dictionary<long, TechnickeUdajeSuboru>();

    /// <summary>The detail on the left of the library (default) or on its right; it takes a third of the space.</summary>
    /// <remarks>Without a chosen title or file the detail is hidden and the library takes the whole width (owner, 9. 11. 2026).</remarks>
    internal void UsporiadajStlpce()
    {
        var vpravo = _nastavenia.DetailVpravo;
        PanelDetailu.Visibility = _detailOtvoreny ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(PanelDetailu, vpravo ? 2 : 1);
        Grid.SetColumn(PanelStredu, _detailOtvoreny ? (vpravo ? 1 : 2) : 1);
        Grid.SetColumnSpan(PanelStredu, _detailOtvoreny ? 1 : 2);
        StlpecA.Width = new GridLength(vpravo ? 2 : 1, GridUnitType.Star);
        StlpecB.Width = new GridLength(vpravo ? 1 : 2, GridUnitType.Star);
        PanelDetailu.BorderThickness = vpravo ? new Thickness(1, 1, 0, 0) : new Thickness(0, 1, 1, 0);
    }

    private bool _detailOtvoreny;

    /// <summary>The detail shows something (a title, a file, a disk …) or nothing: then the library spreads over it.</summary>
    private void DetailOtvoreny(bool otvoreny)
    {
        if (_detailOtvoreny != otvoreny)
        {
            _detailOtvoreny = otvoreny;
            UsporiadajStlpce();
        }
    }

    /// <summary>Whether the detail panel is shown now (the self-test checks it).</summary>
    internal bool DetailJeOtvoreny => _detailOtvoreny;

    /// <summary>Closes the detail: nothing chosen on the wall, the posters over the whole width.</summary>
    internal void ZavrietDetail()
    {
        foreach (var p in _polozkySteny.Where(p => p.Vybrana))
        {
            p.Vybrana = false;
        }

        _vybranyTitul = null;
        ZobrazDetail(null);
    }

    private void ZavrietDetail_Click(object sender, RoutedEventArgs e) => ZavrietDetail();

    /// <summary>What the card needs besides the detail: the parts of the collections and the files of the versions.</summary>
    private static (IReadOnlyList<(KolekciaKniznice, IReadOnlyList<PolozkaKniznice>)> Kolekcie, Dictionary<long, TechnickeUdajeSuboru> Technicke)
        DoplnkyKarty(Microsoft.Data.Sqlite.SqliteConnection c, DetailTitulu? d)
    {
        if (d is null)
        {
            return ([], []);
        }

        var kontext = new KontextHladania([], DateTime.Now);
        var kolekcie = d.Kolekcie
            .Select(k => (k, KniznicaTitulov.Najdi(c, Jadro.Dotazy.Dotaz.Citaj(""), kontext, PohladKniznice.Vsetko, kolekciaId: k.Id)))
            .ToList();
        var technicke = new Dictionary<long, TechnickeUdajeSuboru>();
        foreach (var v in d.Verzie.Where(v => v.Subory.Count > 0).Take(NajviacVerziiSUdajmi))
        {
            var subor = v.Subory.FirstOrDefault(s => !s.Zmazany) ?? v.Subory[0];
            if (TechnickeUdaje.Citaj(c, subor.SuborId) is { } udaje)
            {
                technicke[v.Id] = udaje;
            }
        }

        return (kolekcie, technicke);
    }

    /// <summary>Fills the card: links, identifiers, names, facts, people, plot, versions and collections.</summary>
    private void VyplnKartu(DetailTitulu d)
    {
        var t = d.Polozka;
        DetailNadpis.Visibility = Visibility.Collapsed;
        DetailPodrobnostiPanel.Visibility = Visibility.Collapsed;
        DetailOdkazy.ItemsSource = OdkazyTitulu(d);
        var identifikatory = d.Identifikatory
            .Where(i => i.Zdroj is ZdrojUdajov.Histra or ZdrojUdajov.Tmdb or ZdrojUdajov.Imdb)
            .OrderBy(i => i.Zdroj switch { ZdrojUdajov.Histra => 0, ZdrojUdajov.Tmdb => 1, _ => 2 })
            .Select(i => new RiadokDetailu(NazovZdroja(i.Zdroj), CisloIdentifikatora(i.Hodnota)))
            .ToList();
        DetailIdentifikatory.ItemsSource = identifikatory;
        DetailBlokIdentifikatorov.Visibility = identifikatory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var rok = t.Rok is { } r ? $" ({r.ToString(CultureInfo.InvariantCulture)})" : "";
        DetailHlavnyNazov.Text = t.Nazov + rok;
        DetailIneNazvy.ItemsSource = t.Original is { Length: > 0 } original && !string.Equals(original, t.Nazov, StringComparison.CurrentCultureIgnoreCase)
            ? new[] { original + rok }
            : [];
        var tmdb = d.Identifikatory.Any(i => i.Zdroj == ZdrojUdajov.Tmdb);
        DetailPodnadpis.Text = string.Join("\n", new[]
        {
            string.Join(" · ", new[]
            {
                t.Druh == DruhTitulu.Serial ? "Seriál" : "Film",
                d.DlzkaMinut is { } m ? Minutaz(m) : null,
                t.Druh == DruhTitulu.Serial && t.Epizody > 0 ? Format.Mnozne(t.Epizody, "epizóda", "epizódy", "epizód") + " na diskoch" : null,
            }.Where(x => x is not null)),
            t.Hodnotenie is { } h ? (tmdb ? "TMDB " : "hodnotenie ") + h.ToString("0.0", Format.Slovensky) +
                                    (t.MojeHodnotenie is { } moje ? $" · moje {moje}" : "") : null,
        }.Where(x => x is not null));

        var udaje = new List<RiadokDetailu>();
        if (d.Zanre.Count > 0)
        {
            udaje.Add(new RiadokDetailu("Žáner", string.Join(", ", d.Zanre)));
        }

        var rezia = d.Osoby.Where(o => o.Rola == RolaOsoby.Rezia).Select(o => o.Meno).ToList();
        if (rezia.Count > 0)
        {
            udaje.Add(new RiadokDetailu(t.Druh == DruhTitulu.Serial ? "Tvorcovia" : "Réžia", string.Join(", ", rezia)));
        }

        var scenar = d.Osoby.Where(o => o.Rola == RolaOsoby.Scenar).Select(o => o.Meno).ToList();
        if (scenar.Count > 0)
        {
            udaje.Add(new RiadokDetailu("Scenár", string.Join(", ", scenar)));
        }

        var herci = d.Osoby.Where(o => o.Rola == RolaOsoby.Herec).Take(12)
            .Select(o => o.Postava is { Length: > 0 } postava ? $"{o.Meno} ({postava})" : o.Meno).ToList();
        if (herci.Count > 0)
        {
            udaje.Add(new RiadokDetailu("Hrajú", string.Join(", ", herci)));
        }

        DetailUdaje.ItemsSource = udaje;
        DetailDej.Text = d.Dej ?? "";
        DetailDej.Visibility = d.Dej is null ? Visibility.Collapsed : Visibility.Visible;
        DetailVerzie.ItemsSource = BlokyVerzii(d);

        DetailKolekcie.ItemsSource = _detailKolekcie
            .Where(k => k.Diely.Count > 0)
            .Select(k => new SkupinaKolekcie(k.Kolekcia.Nazov, $"  ·  mám {k.Kolekcia.Mam} z {k.Kolekcia.Diely}",
                k.Diely.Select(p => new DielKolekcie(new PolozkaSteny(p, _obrazky), p.Id == t.Id)).ToList()))
            .ToList();
        DetailKolekcie.Visibility = _detailKolekcie.Any(k => k.Diely.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Each version: where it lies, and its file's picture, length, sound and subtitles.</summary>
    private List<BlokVerzie> BlokyVerzii(DetailTitulu d)
    {
        var pripojene = _stav?.Pripojene.Keys.ToHashSet() ?? [];
        var bloky = new List<BlokVerzie>();
        foreach (var v in d.Verzie)
        {
            var prvy = v.Subory.FirstOrDefault(s => !s.Zmazany) ?? v.Subory.FirstOrDefault();
            if (prvy is null)
            {
                continue;
            }

            var epizoda = v.Seria is { } s && v.Cislo is { } c ? $"S{s:00}E{c:00} · " : "";
            var duplicita = d.DuplicitneVerzie.Contains(v.Id);
            var nadpis = epizoda + (duplicita ? "Duplicita" : !v.Potvrdena ? "Verzia čaká na potvrdenie" : d.NajlepsieVerzie.Contains(v.Id) ? "Najlepšia verzia" : "Verzia") +
                         (duplicita && !v.Potvrdena ? " (čaká na potvrdenie)" : "");
            var kde = $"{prvy.DiskCislo} {prvy.DiskNazov}" + (pripojene.Contains(prvy.DiskId) ? " (pripojený)" : "") +
                      $" · {Format.Velkost(v.Subory.Sum(x => x.Velkost))}" +
                      (v.Subory.Count > 1 ? $" · {Format.Mnozne(v.Subory.Count, "súbor", "súbory", "súborov")}" : "") +
                      (v.Poznamka is { } poz ? $" · {poz}" : "") +
                      (!v.Pritomna ? " · zmazaná z disku" : "") + "\n" + prvy.Nazov;
            var riadky = new List<RiadokDetailu>();
            if (duplicita)
            {
                var ine = d.Verzie.Where(x => x.Id != v.Id && x.EpizodaId == v.EpizodaId && d.DuplicitneVerzie.Contains(x.Id))
                    .Select(x => x.Subory.FirstOrDefault(f => !f.Zmazany) ?? x.Subory.FirstOrDefault())
                    .OfType<SuborVerzie>()
                    .ToList();
                var inyDisk = ine.Any(x => x.DiskId != prvy.DiskId);
                riadky.Add(new RiadokDetailu("Pozor", (inyDisk ? "Rovnaká verzia je aj na inom disku" : "Rovnaká verzia je na tom istom disku viackrát") +
                    ": " + string.Join(", ", ine.Select(x => $"{x.DiskCislo} {x.DiskNazov}").Distinct()) +
                    ". Líši sa najviac veľkosťou do 10 % – jedna z nich je zbytočná."));
            }

            if (d.NajlepsieVerzie.Contains(v.Id) && DovodNajlepsej(d, v) is { } dovod)
            {
                riadky.Add(new RiadokDetailu("Prečo", dovod));
            }

            if (_detailTechnicke.TryGetValue(v.Id, out var udaje))
            {
                riadky.AddRange(TechnickeRiadky(udaje));
            }
            else if (bloky.Count < NajviacVerziiSUdajmi)
            {
                riadky.Add(new RiadokDetailu("Súbor", "Technické údaje ešte neprečítané – prečítajú sa, keď bude disk pripojený."));
            }

            bloky.Add(new BlokVerzie(nadpis, kde, riadky)
            {
                Podrobnosti = _detailTechnicke.TryGetValue(v.Id, out var t) ? t.Podrobnosti : [],
            });
        }

        return bloky;
    }

    /// <summary>"2 h 16 min (136 min)", or "48 min".</summary>
    internal static string Minutaz(int minuty) =>
        minuty >= 60 ? $"{minuty / 60} h {minuty % 60} min ({minuty} min)" : $"{minuty} min";

    /// <summary>"movie/603" → "603" (the kind is clear from the title), an IMDb id as it is.</summary>
    private static string CisloIdentifikatora(string hodnota) =>
        hodnota.LastIndexOf('/') is var i and >= 0 ? hodnota[(i + 1)..] : hodnota;

    /// <summary>The title on HISTRA, TMDB, IMDb and Trakt, and a search on ČSFD (ČSFD ids are not known).</summary>
    internal static List<OdkazTitulu> OdkazyTitulu(DetailTitulu d)
    {
        var t = d.Polozka;
        string? Id(ZdrojUdajov zdroj) => d.Identifikatory.FirstOrDefault(i => i.Zdroj == zdroj).Hodnota;
        var odkazy = new List<OdkazTitulu>();
        if (Id(ZdrojUdajov.Histra) is { } histra && IdTitulu().Match(histra) is { Success: true } h)
        {
            odkazy.Add(new("HISTRA", new Uri($"https://histra.net/{(h.Groups[1].Value == "movie" ? "movie" : "show")}/{h.Groups[2].Value}")));
        }

        var tmdb = Id(ZdrojUdajov.Tmdb) is { } tm && IdTitulu().Match(tm) is { Success: true } m ? m : null;
        if (tmdb is not null)
        {
            odkazy.Add(new("TMDB", new Uri($"https://www.themoviedb.org/{tmdb.Groups[1].Value}/{tmdb.Groups[2].Value}")));
        }

        if (Id(ZdrojUdajov.Imdb) is { } imdb && IdImdb().IsMatch(imdb))
        {
            odkazy.Add(new("IMDb", new Uri($"https://www.imdb.com/title/{imdb}/")));
        }

        if (tmdb is not null)
        {
            odkazy.Add(new("Trakt", new Uri($"https://trakt.tv/search/tmdb/{tmdb.Groups[2].Value}?id_type={(tmdb.Groups[1].Value == "movie" ? "movie" : "show")}")));
        }

        var hladat = t.Original is { Length: > 0 } o ? o : t.Nazov;
        odkazy.Add(new("ČSFD (hľadať)", new Uri("https://www.csfd.cz/hledat/?q=" + Uri.EscapeDataString(hladat))));
        return odkazy;
    }

    /// <summary>Only the pages of the card's links open, in the default browser.</summary>
    internal static bool SmieOtvorit(Uri adresa) =>
        adresa.Scheme == Uri.UriSchemeHttps && adresa.Host is "histra.net" or "www.themoviedb.org" or "www.imdb.com" or "trakt.tv" or "www.csfd.cz";

    private void OdkazTitulu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OdkazTitulu { Adresa: var adresa } } && SmieOtvorit(adresa))
        {
            OtvorVPrehliadaci(adresa);
        }
    }

    private void OtvorVPrehliadaci(Uri adresa)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(adresa.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StavUloh.Text = "Prehliadač sa nepodarilo otvoriť: " + ex.Message;
        }
    }

    /// <summary>A title of the collection opens in the detail (and is chosen on the wall when it is there).</summary>
    private async void DielKolekcie_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DielKolekcie diel } && !diel.Aktualny)
        {
            await OtvorTitulAsync(diel.Id);
        }
    }

    /// <summary>Shows a title in the detail; on the wall it is chosen when it is there.</summary>
    internal async Task OtvorTitulAsync(long titulId)
    {
        if (_polozkySteny.FirstOrDefault(p => p.Titul?.Id == titulId) is { } naStene)
        {
            await VyberPolozkuAsync(naStene);
            return;
        }

        foreach (var p in _polozkySteny.Where(p => p.Vybrana))
        {
            p.Vybrana = false;
        }

        _vybranyTitul = titulId;
        await ZobrazDetailTituluAsync(titulId);
    }

    [GeneratedRegex(@"^(movie|tv)/(\d{1,10})$", RegexOptions.CultureInvariant)]
    private static partial Regex IdTitulu();

    [GeneratedRegex(@"^tt\d{5,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdImdb();
}

/// <summary>A link of the title's card.</summary>
internal sealed record OdkazTitulu(string Text, Uri Adresa);

/// <summary>A version of a title on the card: where it lies and what its file is.</summary>
internal sealed record BlokVerzie(string Nadpis, string Kde, IReadOnlyList<RiadokDetailu> Riadky)
{
    /// <summary>All details of the version's file (own reader of MKV and MP4), shown on request.</summary>
    public IReadOnlyList<SekciaMedia> Podrobnosti { get; init; } = [];

    public bool MaPodrobnosti => Podrobnosti.Count > 0;
}

/// <summary>A collection on the card with all its titles.</summary>
internal sealed record SkupinaKolekcie(string Nazov, string Pocet, IReadOnlyList<DielKolekcie> Diely);

/// <summary>A title of a collection on the card: a small poster, the name and year; the shown title is framed.</summary>
internal sealed class DielKolekcie(PolozkaSteny polozka, bool aktualny)
{
    public long Id => polozka.Titul?.Id ?? 0;

    public string Nazov => polozka.Nazov;

    public string Popis => polozka.Titul?.Rok is { } rok ? $"{polozka.Nazov} ({rok.ToString(CultureInfo.InvariantCulture)})" : polozka.Nazov;

    public double Priehladnost => polozka.Priehladnost;

    public ImageSource? Obrazok => polozka.Obrazok;

    public bool Aktualny => aktualny;
}
