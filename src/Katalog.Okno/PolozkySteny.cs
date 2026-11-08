using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Katalog.Data;
using Katalog.Jadro.Model;

namespace Katalog.Okno;

/// <summary>A tile of the poster wall (a title, a collection or a HISTRA list), also a row of the list.</summary>
internal sealed class PolozkaSteny : INotifyPropertyChanged
{
    /// <summary>Posters are decoded at the width of a tile (memory stays small with thousands of titles).</summary>
    public const int SirkaPlagatu = 150;

    private readonly string? _subor;
    private bool _vybrana;

    public PolozkaSteny(PolozkaKniznice titul, UlozisteObrazkov? obrazky)
    {
        Titul = titul;
        Nazov = titul.Nazov;
        Podnadpis = string.Join(" · ", new[]
        {
            titul.Rok?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            titul.Druh == DruhTitulu.Serial ? (titul.Epizody > 0 ? Format.Mnozne(titul.Epizody, "epizóda", "epizódy", "epizód") : "seriál") : null,
            titul.Kvalita switch { TriedaKvality.UHD4K => "4K", TriedaKvality.Nezistena => null, var k => k.ToString() },
            titul.Verzie > 1 && titul.Druh == DruhTitulu.Film ? $"{titul.Verzie} verzie" : null,
        }.Where(t => t is not null));
        Stitok = titul.NaPotvrdenie ? "Na potvrdenie" : !titul.Mam ? (titul.Zelany ? "Želané" : "Nemám") : titul.Videne ? "Videné" : null;
        Priehladnost = titul.Mam || titul.NaPotvrdenie ? 1 : 0.55;
        _subor = titul.Plagat is { } p && obrazky is not null ? obrazky.Cesta(p) : null;
        Ikona = titul.Druh == DruhTitulu.Serial ? "" : "";
    }

    public PolozkaSteny(KolekciaKniznice kolekcia, UlozisteObrazkov? obrazky)
    {
        Kolekcia = kolekcia;
        Nazov = kolekcia.Nazov;
        Podnadpis = $"mám {kolekcia.Mam} z {kolekcia.Diely}";
        Stitok = kolekcia.Mam < kolekcia.Diely ? $"chýba {kolekcia.Diely - kolekcia.Mam}" : null;
        Priehladnost = 1;
        _subor = kolekcia.Plagat is { } p && obrazky is not null ? obrazky.Cesta(p) : null;
        Ikona = "";
    }

    public PolozkaSteny(ZoznamHistraKniznice zoznam)
    {
        Zoznam = zoznam;
        Nazov = zoznam.Nazov;
        Podnadpis = $"mám {zoznam.Mam} z {zoznam.Polozky}" + (zoznam.Synchronizovany is { } s ? " · načítaný " + Format.Cas(s) : "");
        Priehladnost = 1;
        Ikona = "";
    }

    public PolozkaKniznice? Titul { get; }

    public KolekciaKniznice? Kolekcia { get; }

    public ZoznamHistraKniznice? Zoznam { get; }

    public string Nazov { get; }

    public string Podnadpis { get; }

    /// <summary>"Na potvrdenie", "Nemám", "Želané", "Videné" – a badge on the tile.</summary>
    public string? Stitok { get; }

    public bool MaStitok => Stitok is not null;

    /// <summary>Titles the user does not have are faded.</summary>
    public double Priehladnost { get; }

    /// <summary>The glyph shown without a poster.</summary>
    public string Ikona { get; }

    public bool MaPlagat => _subor is not null;

    // list columns
    public string RokText => Titul?.Rok?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string DruhText => Titul?.Druh switch { DruhTitulu.Serial => "seriál", DruhTitulu.Film => "film", _ => Kolekcia is not null ? "kolekcia" : "zoznam" };

    public string KvalitaText => Titul?.Kvalita switch { TriedaKvality.UHD4K => "4K", TriedaKvality.Nezistena or null => "", var k => k.ToString()! };

    public string HodnotenieText => Titul?.Hodnotenie is { } h ? h.ToString("0.0", Format.Slovensky) : "";

    /// <summary>
    /// The poster decoded at tile width – read from its file only when the tile is shown (bound with IsAsync, so off the
    /// window's thread); null without a poster or when its file is missing.
    /// </summary>
    public ImageSource? Obrazok
    {
        get
        {
            if (_subor is null || !File.Exists(_subor))
            {
                return null;
            }

            try
            {
                var obrazok = new BitmapImage();
                obrazok.BeginInit();
                obrazok.CacheOption = BitmapCacheOption.OnLoad;
                obrazok.DecodePixelWidth = SirkaPlagatu;
                obrazok.UriSource = new Uri(_subor);
                obrazok.EndInit();
                obrazok.Freeze();
                return obrazok;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    public bool Vybrana
    {
        get => _vybrana;
        set
        {
            if (_vybrana != value)
            {
                _vybrana = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vybrana)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One row of the poster wall: as many tiles as fit the width (the rows are virtualised, so thousands scroll smoothly).</summary>
internal sealed record RiadokSteny(IReadOnlyList<PolozkaSteny> Polozky);

/// <summary>A candidate of a source as the detail of a title offers it.</summary>
internal sealed record KartaKandidata(Jadro.Rozhrania.Kandidat Kandidat)
{
    public string Nazov => Kandidat.Nazov;

    public string Podnadpis => string.Join(" · ", new[]
    {
        Kandidat.Rok?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Kandidat.Original is { } o && o != Kandidat.Nazov ? o : null,
        $"zhoda {Kandidat.Zhoda * 100:0} %",
    }.Where(t => t is not null));

    /// <summary>The candidate's poster from the source's image server (shown only while choosing).</summary>
    public Uri? Plagat => Kandidat.Plagat;
}
