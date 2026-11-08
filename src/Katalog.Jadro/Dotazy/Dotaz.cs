using System.Globalization;
using System.Text;
using Katalog.Jadro.Model;

namespace Katalog.Jadro.Dotazy;

/// <summary>
/// A query of the one query language of KATALOG (decision A4): what the user types into the search box, what the filters
/// write, what a saved search keeps, and later what playlists, labels and the web catalogue use.
/// <code>
/// matrix 1999                 names containing both words (any part of a word, diacritics and letter case ignored)
/// "pán prsteňov"              an exact phrase
/// -sample                     names without this word
/// pripona:mkv,mp4             extension          typ:video,titulky,zvuk,obrazok,dokument,archiv,priecinok,subor
/// velkost:>4GB  velkost:1GB..8GB  (B, kB, MB, GB, TB; a bare value means "at least", a bare number is GB)
/// zmenene:2024  zmenene:>=2024-05  zmenene:&lt;1.3.2025   pridane:7d (the last 7 days; t = weeks, m = months, r = years)
/// disk:3,7  disk:"Filmy 1"    katalog:Videotéka    pripojene:ano|nie    zmazane:aj|len
/// </code>
/// Every condition must hold; a minus before a field negates it (-pripona:srt). What cannot be understood is reported in
/// <see cref="Upozornenia"/> and left out, never guessed silently.
/// </summary>
public sealed class Dotaz
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, PoleDotazu> NazvyPoli = new(StringComparer.Ordinal)
    {
        ["pripona"] = PoleDotazu.Pripona, ["ext"] = PoleDotazu.Pripona,
        ["typ"] = PoleDotazu.Typ, ["druh"] = PoleDotazu.Typ,
        ["velkost"] = PoleDotazu.Velkost, ["size"] = PoleDotazu.Velkost,
        ["zmenene"] = PoleDotazu.Zmenene, ["zmeneny"] = PoleDotazu.Zmenene, ["datum"] = PoleDotazu.Zmenene,
        ["pridane"] = PoleDotazu.Pridane, ["pridany"] = PoleDotazu.Pridane, ["nove"] = PoleDotazu.Pridane,
        ["disk"] = PoleDotazu.Disk,
        ["katalog"] = PoleDotazu.Katalog,
        ["pripojene"] = PoleDotazu.Pripojene, ["pripojeny"] = PoleDotazu.Pripojene,
        ["zmazane"] = PoleDotazu.Zmazane, ["zmazany"] = PoleDotazu.Zmazane,
        ["kvalita"] = PoleDotazu.Kvalita, ["rozlisenie"] = PoleDotazu.Kvalita,
        ["kodek"] = PoleDotazu.Kodek, ["codec"] = PoleDotazu.Kodek,
        ["zvuk"] = PoleDotazu.Zvuk, ["audio"] = PoleDotazu.Zvuk, ["dabing"] = PoleDotazu.Zvuk,
        ["titulky"] = PoleDotazu.Titulky, ["sub"] = PoleDotazu.Titulky,
        ["hdr"] = PoleDotazu.Hdr,
        ["dlzka"] = PoleDotazu.Dlzka, ["cas"] = PoleDotazu.Dlzka,
        ["poskodene"] = PoleDotazu.Poskodene, ["poskodeny"] = PoleDotazu.Poskodene, ["chyby"] = PoleDotazu.Poskodene,
        ["rok"] = PoleDotazu.Rok, ["year"] = PoleDotazu.Rok,
        ["zaner"] = PoleDotazu.Zaner, ["zanre"] = PoleDotazu.Zaner, ["genre"] = PoleDotazu.Zaner,
        ["videne"] = PoleDotazu.Videne, ["videny"] = PoleDotazu.Videne, ["pozrete"] = PoleDotazu.Videne,
        ["hodnotenie"] = PoleDotazu.Hodnotenie, ["rating"] = PoleDotazu.Hodnotenie,
        ["moje"] = PoleDotazu.MojeHodnotenie, ["mojehodnotenie"] = PoleDotazu.MojeHodnotenie,
        ["herec"] = PoleDotazu.Herec, ["herci"] = PoleDotazu.Herec, ["hra"] = PoleDotazu.Herec,
        ["rezia"] = PoleDotazu.Rezia, ["reziser"] = PoleDotazu.Rezia,
        ["kolekcia"] = PoleDotazu.Kolekcia, ["kolekcie"] = PoleDotazu.Kolekcia,
        ["kniznica"] = PoleDotazu.Kniznica, ["titul"] = PoleDotazu.Kniznica,
        ["mam"] = PoleDotazu.Mam,
        ["zelane"] = PoleDotazu.Zelane, ["zelany"] = PoleDotazu.Zelane, ["chcem"] = PoleDotazu.Zelane,
    };

    private Dotaz(string text, IReadOnlyList<Podmienka> podmienky, IReadOnlyList<string> upozornenia)
    {
        Text = text;
        Podmienky = podmienky;
        Upozornenia = upozornenia;
    }

    /// <summary>The text as the user wrote it.</summary>
    public string Text { get; }

    public IReadOnlyList<Podmienka> Podmienky { get; }

    /// <summary>What was not understood and left out (in Slovak, for the window).</summary>
    public IReadOnlyList<string> Upozornenia { get; }

    /// <summary>Nothing to search: no condition that narrows the result.</summary>
    public bool JePrazdny => Podmienky.All(p => p is Zmazane);

    public RezimZmazanych Zmazane => Podmienky.OfType<Zmazane>().LastOrDefault()?.Rezim ?? RezimZmazanych.Nie;

    /// <summary>Whether folders can match (no condition that only files have).</summary>
    public bool HladaPriecinky => Podmienky.All(p => p switch
    {
        Pripony { Zaporna: false } or Velkost { Zaporna: false } or MedialnaPodmienka or TitulovaPodmienka => false,
        Obdobie { Pole: PoleDotazu.Pridane, Zaporna: false } => false,
        Typy { Zaporna: false } t => t.Priecinky,
        Typy { Zaporna: true } t => !t.Priecinky,
        _ => true,
    });

    /// <summary>Whether files can match.</summary>
    public bool HladaSubory => Podmienky.All(p => p switch
    {
        Typy { Zaporna: false } t => t.VsetkySubory || t.Druhy.Count > 0,
        Typy { Zaporna: true } t => !t.VsetkySubory,
        _ => true,
    });

    public static Dotaz Citaj(string? text)
    {
        text ??= "";
        var podmienky = new List<Podmienka>();
        var upozornenia = new List<string>();
        foreach (var token in Rozdel(text))
        {
            if (token.Pole is null)
            {
                if (token.Hodnota.Length > 0)
                {
                    podmienky.Add(new Slovo(token.Hodnota, token.Uvodzovky, token.Zaporny));
                }

                continue;
            }

            if (!NazvyPoli.TryGetValue(BezDiakritiky(token.Pole), out var pole))
            {
                // not a field (yet): "re:zero" or "zvuk:cs" before stage E3 – searched as written
                upozornenia.Add($"Pole „{token.Pole}“ nepoznám, hľadám „{token.Pole}:{token.Hodnota}“ ako text.");
                podmienky.Add(new Slovo(token.Pole + ":" + token.Hodnota, token.Uvodzovky, token.Zaporny));
                continue;
            }

            if (Pole(pole, token.Hodnota, token.Zaporny) is { } podmienka)
            {
                podmienky.Add(podmienka);
            }
            else
            {
                upozornenia.Add($"„{token.Pole}:{token.Hodnota}“ nerozumiem, vynechávam to. Príklad: {Priklad(pole)}");
            }
        }

        return new Dotaz(text, podmienky, upozornenia);
    }

    /// <summary>The canonical name of a field, as the filters write it.</summary>
    public static string NazovPola(PoleDotazu pole) => pole switch
    {
        PoleDotazu.Pripona => "pripona",
        PoleDotazu.Typ => "typ",
        PoleDotazu.Velkost => "velkost",
        PoleDotazu.Zmenene => "zmenene",
        PoleDotazu.Pridane => "pridane",
        PoleDotazu.Disk => "disk",
        PoleDotazu.Katalog => "katalog",
        PoleDotazu.Pripojene => "pripojene",
        PoleDotazu.Kvalita => "kvalita",
        PoleDotazu.Kodek => "kodek",
        PoleDotazu.Zvuk => "zvuk",
        PoleDotazu.Titulky => "titulky",
        PoleDotazu.Hdr => "hdr",
        PoleDotazu.Dlzka => "dlzka",
        PoleDotazu.Poskodene => "poskodene",
        PoleDotazu.Rok => "rok",
        PoleDotazu.Zaner => "zaner",
        PoleDotazu.Videne => "videne",
        PoleDotazu.Hodnotenie => "hodnotenie",
        PoleDotazu.MojeHodnotenie => "moje",
        PoleDotazu.Herec => "herec",
        PoleDotazu.Rezia => "rezia",
        PoleDotazu.Kolekcia => "kolekcia",
        PoleDotazu.Kniznica => "kniznica",
        PoleDotazu.Mam => "mam",
        PoleDotazu.Zelane => "zelane",
        _ => "zmazane",
    };

    /// <summary>
    /// The text with the field set to a value (or removed, when <paramref name="hodnota"/> is null or empty). The rest of
    /// the text stays exactly as the user wrote it; this is how the filters edit the search box.
    /// </summary>
    public static string NastavPole(string text, PoleDotazu pole, string? hodnota)
    {
        var casti = new List<string>();
        var poslednyKoniec = 0;
        // the text without the field's words; the other words stay as written (only runs of spaces become one)
        var vysledok = new StringBuilder();
        foreach (var token in Rozdel(text))
        {
            if (token.Pole is not null && NazvyPoli.TryGetValue(BezDiakritiky(token.Pole), out var p) && p == pole)
            {
                vysledok.Append(text, poslednyKoniec, token.Zaciatok - poslednyKoniec);
                poslednyKoniec = token.Zaciatok + token.Dlzka;
            }
        }

        vysledok.Append(text, poslednyKoniec, text.Length - poslednyKoniec);
        casti.AddRange(vysledok.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrWhiteSpace(hodnota))
        {
            var h = hodnota.Trim();
            casti.Add(NazovPola(pole) + ":" + (h.Contains(' ', StringComparison.Ordinal) ? "\"" + h.Replace("\"", "", StringComparison.Ordinal) + "\"" : h));
        }

        return string.Join(' ', casti);
    }

    /// <summary>The value of the first (not negated) occurrence of a field in the text, for the filters to show.</summary>
    public static string? HodnotaPola(string text, PoleDotazu pole) => Rozdel(text)
        .Where(t => !t.Zaporny && t.Pole is not null && NazvyPoli.TryGetValue(BezDiakritiky(t.Pole), out var p) && p == pole)
        .Select(t => t.Hodnota)
        .FirstOrDefault();

    // ------------------------------------------------------------------ fields

    private static Podmienka? Pole(PoleDotazu pole, string hodnota, bool zaporny)
    {
        var h = hodnota.Trim();
        if (h.Length == 0)
        {
            return null;
        }

        switch (pole)
        {
            case PoleDotazu.Pripona:
                var pripony = Zoznam(h).Select(p => p.TrimStart('.').ToLowerInvariant()).Where(p => p.Length > 0).Distinct().ToList();
                return pripony.Count == 0 ? null : new Pripony(pripony, zaporny);

            case PoleDotazu.Typ:
                var druhy = new List<DruhSuboru>();
                bool priecinky = false, vsetky = false;
                foreach (var slovo in Zoznam(h).Select(BezDiakritiky))
                {
                    switch (slovo)
                    {
                        case "video" or "videa" or "film" or "filmy": druhy.Add(DruhSuboru.Video); break;
                        case "titulky" or "titulok": druhy.Add(DruhSuboru.Titulky); break;
                        case "zvuk" or "hudba" or "audio": druhy.Add(DruhSuboru.Zvuk); break;
                        case "obrazok" or "obrazky" or "foto": druhy.Add(DruhSuboru.Obrazok); break;
                        case "dokument" or "dokumenty" or "text": druhy.Add(DruhSuboru.Dokument); break;
                        case "archiv" or "archivy": druhy.Add(DruhSuboru.Archiv); break;
                        case "priecinok" or "priecinky" or "adresar": priecinky = true; break;
                        case "subor" or "subory": vsetky = true; break;
                        default: return null;
                    }
                }

                return new Typy(druhy.Distinct().ToList(), priecinky, vsetky, zaporny);

            case PoleDotazu.Velkost:
                return Rozsah(h, CitajVelkost) is var (od, doo, ok) && ok ? VelkostAk(od, doo, zaporny) : null;

            case PoleDotazu.Zmenene or PoleDotazu.Pridane:
                return CitajObdobie(h) is var (odH, doH, platne) && platne ? new Obdobie(pole, odH, doH, zaporny) : null;

            case PoleDotazu.Disk:
                var cisla = new List<int>();
                var nazvy = new List<string>();
                foreach (var d in Zoznam(h))
                {
                    var bezD = d.Length > 1 && (d[0] is 'D' or 'd') && char.IsAsciiDigit(d[1]) ? d[1..] : d;
                    if (int.TryParse(bezD, NumberStyles.None, Invariant, out var cislo) && cislo > 0)
                    {
                        cisla.Add(cislo);
                    }
                    else
                    {
                        nazvy.Add(d);
                    }
                }

                return new Disky(cisla, nazvy, zaporny);

            case PoleDotazu.Katalog:
                return new Katalogy(Zoznam(h), zaporny);

            case PoleDotazu.Pripojene:
                return AnoNie(h) is { } ano ? new Pripojene(ano != zaporny) : null;

            case PoleDotazu.Kvalita:
                return CitajKvality(h) is { Count: > 0 } triedy ? new Kvality(triedy, zaporny) : null;

            case PoleDotazu.Kodek:
                var kodeky = Zoznam(h).Select(KodekVidea).ToList();
                return kodeky.Count == 0 || kodeky.Any(k => k is null) ? null : new Kodeky(kodeky.Select(k => k!).Distinct().ToList(), zaporny);

            case PoleDotazu.Zvuk or PoleDotazu.Titulky:
                var druhStopy = pole == PoleDotazu.Zvuk ? DruhStopy.Zvuk : DruhStopy.Titulky;
                if (AnoNie(h) is { } maju)
                {
                    return new JazykyStop(druhStopy, [], true, zaporny == maju);
                }

                var jazyky = Zoznam(h).Select(Jazyky.Normalizuj).ToList();
                return jazyky.Count == 0 || jazyky.Any(j => j is null) ? null : new JazykyStop(druhStopy, jazyky.Select(j => j!).Distinct().ToList(), false, zaporny);

            case PoleDotazu.Hdr:
                var druhyHdr = new List<Hdr>();
                foreach (var slovo in Zoznam(h).Select(BezDiakritiky))
                {
                    switch (slovo)
                    {
                        case "ano" or "yes" or "1": druhyHdr.AddRange([Hdr.Hdr10, Hdr.Hdr10Plus, Hdr.DolbyVision, Hdr.Hlg]); break;
                        case "nie" or "no" or "0" or "sdr": druhyHdr.Add(Hdr.Ziadne); break;
                        case "hdr10" or "hdr": druhyHdr.Add(Hdr.Hdr10); break;
                        case "hdr10+" or "hdr10plus": druhyHdr.Add(Hdr.Hdr10Plus); break;
                        case "dv" or "dolbyvision" or "dovi": druhyHdr.Add(Hdr.DolbyVision); break;
                        case "hlg": druhyHdr.Add(Hdr.Hlg); break;
                        default: return null;
                    }
                }

                return new HdrDruhy(druhyHdr.Distinct().ToList(), zaporny);

            case PoleDotazu.Dlzka:
                return Rozsah(h, CitajDlzku) is var (odMs, doMs, platnaDlzka) && platnaDlzka && (odMs is not null || doMs is not null)
                    ? new Dlzka(odMs, doMs, zaporny)
                    : null;

            case PoleDotazu.Poskodene:
                return AnoNie(h) is { } poskodene ? new Poskodene(poskodene != zaporny) : null;

            case PoleDotazu.Rok:
                // a plain year is that year (not "at least" as with sizes)
                var rokRozsah = int.TryParse(h, NumberStyles.None, Invariant, out var jedenRok) ? ((long?)jedenRok, (long?)jedenRok, true) : Rozsah(h, CitajRok);
                return rokRozsah is var (rokOd, rokDo, platnyRok) && platnyRok && rokOd is null or >= 1800 && rokDo is null or <= 2300
                    ? new Roky((int?)rokOd, (int?)rokDo, zaporny)
                    : null;

            case PoleDotazu.Zaner:
                var zanre = Zoznam(h).Select(Normalizacia.Nazov).Where(z => z.Length > 0).Distinct().ToList();
                return zanre.Count == 0 ? null : new Zanre(zanre, zaporny);

            case PoleDotazu.Videne:
                return AnoNie(h) is { } videne ? new Videne(videne != zaporny) : null;

            case PoleDotazu.Hodnotenie or PoleDotazu.MojeHodnotenie:
                return Rozsah(h, CitajDesatiny) is var (hOd, hDo, platneH) && platneH
                    ? new Hodnotenia(hOd / 10.0, hDo / 10.0, pole == PoleDotazu.MojeHodnotenie, zaporny)
                    : null;

            case PoleDotazu.Herec or PoleDotazu.Rezia:
                var mena = Zoznam(h).Where(m => m.Length >= 3).ToList();
                return mena.Count == 0 ? null : new Osoby(pole == PoleDotazu.Herec ? RolaOsoby.Herec : RolaOsoby.Rezia, mena, zaporny);

            case PoleDotazu.Kolekcia:
                if (AnoNie(h) is { } vKolekcii)
                {
                    return new Kolekcie([], zaporny == vKolekcii);
                }

                var kolekcie = Zoznam(h).Where(k => k.Length >= 3).ToList();
                return kolekcie.Count == 0 ? null : new Kolekcie(kolekcie, zaporny);

            case PoleDotazu.Kniznica:
                return BezDiakritiky(h) switch
                {
                    "film" or "filmy" or "movie" => new VKniznici(StavVKniznici.Film, zaporny),
                    "serial" or "serialy" or "tv" => new VKniznici(StavVKniznici.Serial, zaporny),
                    "ano" or "a" or "yes" or "1" => new VKniznici(StavVKniznici.VKniznici, zaporny),
                    "nie" or "n" or "no" or "0" or "mimo" => new VKniznici(StavVKniznici.MimoKniznice, zaporny),
                    "potvrdit" or "napotvrdenie" or "nepotvrdene" => new VKniznici(StavVKniznici.NaPotvrdenie, zaporny),
                    _ => null,
                };

            case PoleDotazu.Mam:
                return AnoNie(h) is { } mam ? new Mam(mam != zaporny) : null;

            case PoleDotazu.Zelane:
                return AnoNie(h) is { } zelane ? new Zelane(zelane != zaporny) : null;

            default:
                return BezDiakritiky(h) switch
                {
                    "nie" or "no" or "false" or "0" => new Zmazane(zaporny ? RezimZmazanych.Len : RezimZmazanych.Nie),
                    "aj" or "all" or "vsetky" => new Zmazane(RezimZmazanych.Aj),
                    "len" or "ano" or "yes" or "true" or "1" or "only" => new Zmazane(zaporny ? RezimZmazanych.Nie : RezimZmazanych.Len),
                    _ => null,
                };
        }

        static Velkost? VelkostAk(long? od, long? doo, bool zaporny) => od is null && doo is null ? null : new Velkost(od, doo, zaporny);
    }

    private static string Priklad(PoleDotazu pole) => pole switch
    {
        PoleDotazu.Pripona => "pripona:mkv,mp4",
        PoleDotazu.Typ => "typ:video (alebo titulky, zvuk, obrazok, dokument, archiv, priecinok, subor)",
        PoleDotazu.Velkost => "velkost:>4GB alebo velkost:1GB..8GB",
        PoleDotazu.Zmenene => "zmenene:2024, zmenene:>=2024-05 alebo zmenene:30d",
        PoleDotazu.Pridane => "pridane:7d (posledných 7 dní) alebo pridane:>=1.11.2026",
        PoleDotazu.Disk => "disk:3 alebo disk:\"Filmy 1\"",
        PoleDotazu.Katalog => "katalog:Videotéka",
        PoleDotazu.Pripojene => "pripojene:ano",
        PoleDotazu.Kvalita => "kvalita:4k,fhd alebo kvalita:>=fhd (sd, hd, fhd, 4k)",
        PoleDotazu.Kodek => "kodek:hevc (h265, h264, av1, vp9, mpeg2, mpeg4, vc1)",
        PoleDotazu.Zvuk => "zvuk:cs, zvuk:sk,cs alebo zvuk:nie",
        PoleDotazu.Titulky => "titulky:sk, titulky:ano alebo titulky:nie",
        PoleDotazu.Hdr => "hdr:ano (alebo hdr10, dv, hlg, nie)",
        PoleDotazu.Dlzka => "dlzka:>2h, dlzka:<30min alebo dlzka:80min..100min",
        PoleDotazu.Poskodene => "poskodene:ano",
        PoleDotazu.Rok => "rok:2001, rok:1990..1999 alebo rok:>=2020",
        PoleDotazu.Zaner => "zaner:komedia,horor",
        PoleDotazu.Videne => "videne:ano alebo videne:nie",
        PoleDotazu.Hodnotenie => "hodnotenie:>=7.5",
        PoleDotazu.MojeHodnotenie => "moje:>=8",
        PoleDotazu.Herec => "herec:\"Meno Priezvisko\" (aspoň 3 písmená)",
        PoleDotazu.Rezia => "rezia:Priezvisko (aspoň 3 písmená)",
        PoleDotazu.Kolekcia => "kolekcia:ano alebo kolekcia:\"Názov kolekcie\"",
        PoleDotazu.Kniznica => "kniznica:film (alebo serial, ano, nie, potvrdit)",
        PoleDotazu.Mam => "mam:nie (tituly, ktoré nemáš)",
        PoleDotazu.Zelane => "zelane:ano",
        _ => "zmazane:aj alebo zmazane:len",
    };

    private static IReadOnlyList<string> Zoznam(string hodnota) =>
        hodnota.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool? AnoNie(string hodnota) => BezDiakritiky(hodnota) switch
    {
        "ano" or "a" or "yes" or "true" or "1" => true,
        "nie" or "n" or "no" or "false" or "0" => false,
        _ => null,
    };

    /// <summary>Operators and ranges of a value: "&gt;x", "&gt;=x", "&lt;x", "&lt;=x", "=x", "x" (at least), "a..b".</summary>
    private static (long? Od, long? Do, bool Platne) Rozsah(string h, Func<string, long?> cislo)
    {
        if (h.Contains("..", StringComparison.Ordinal))
        {
            var casti = h.Split("..", 2);
            long? od = casti[0].Length == 0 ? null : cislo(casti[0]);
            long? doo = casti[1].Length == 0 ? null : cislo(casti[1]);
            var platne = (casti[0].Length == 0 || od is not null) && (casti[1].Length == 0 || doo is not null) && (od is not null || doo is not null);
            return (od, doo, platne);
        }

        var (op, zvysok) = Operator(h);
        if (cislo(zvysok) is not { } x)
        {
            return (null, null, false);
        }

        return op switch
        {
            ">" => (x + 1, null, true),
            "<" => (null, x - 1, true),
            "<=" => (null, x, true),
            "=" => (x, x, true),
            _ => (x, null, true),
        };
    }

    private static (string Operator, string Zvysok) Operator(string h)
    {
        foreach (var op in new[] { ">=", "<=", ">", "<", "=" })
        {
            if (h.StartsWith(op, StringComparison.Ordinal))
            {
                return (op, h[op.Length..].Trim());
            }
        }

        return ("", h);
    }

    private static long? CitajRok(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, Invariant, out var rok) && rok is >= 1800 and <= 2300 ? rok : null;

    /// <summary>"7.5" or "7,5" as tenths (75): ratings go from 0 to 10.</summary>
    private static long? CitajDesatiny(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, Invariant, out var x) && x is >= 0 and <= 10
            ? (long)Math.Round(x * 10)
            : null;

    /// <summary>"4GB", "700 MB", "1,5T", "4" (GB) – in bytes, by 1024 as Windows counts sizes.</summary>
    private static long? CitajVelkost(string text)
    {
        var t = text.Trim().Replace(',', '.').Replace(" ", "", StringComparison.Ordinal);
        var koniecCisla = 0;
        while (koniecCisla < t.Length && (char.IsAsciiDigit(t[koniecCisla]) || t[koniecCisla] == '.'))
        {
            koniecCisla++;
        }

        if (koniecCisla == 0 || !decimal.TryParse(t[..koniecCisla], NumberStyles.AllowDecimalPoint, Invariant, out var cislo))
        {
            return null;
        }

        var nasobok = t[koniecCisla..].ToLowerInvariant() switch
        {
            "b" => 1L,
            "k" or "kb" or "kib" => 1L << 10,
            "m" or "mb" or "mib" => 1L << 20,
            "" or "g" or "gb" or "gib" => 1L << 30,
            "t" or "tb" or "tib" => 1L << 40,
            _ => 0,
        };
        if (nasobok == 0)
        {
            return null;
        }

        var bajty = cislo * nasobok;
        return bajty > long.MaxValue ? null : (long)decimal.Round(bajty);
    }

    /// <summary>"4k,fhd", "&gt;=fhd", "&lt;hd" – classes of resolution.</summary>
    private static List<TriedaKvality>? CitajKvality(string h)
    {
        static TriedaKvality? Trieda(string t) => BezDiakritiky(t) switch
        {
            "sd" or "480p" or "576p" => TriedaKvality.SD,
            "hd" or "720p" => TriedaKvality.HD,
            "fhd" or "fullhd" or "1080p" or "1080" => TriedaKvality.FHD,
            "4k" or "uhd" or "2160p" or "2160" => TriedaKvality.UHD4K,
            _ => null,
        };

        var (op, zvysok) = Operator(h);
        if (op.Length > 0 && op != "=")
        {
            if (Trieda(zvysok) is not { } hranica)
            {
                return null;
            }

            var vsetky = new[] { TriedaKvality.SD, TriedaKvality.HD, TriedaKvality.FHD, TriedaKvality.UHD4K };
            return vsetky.Where(t => op switch
            {
                ">" => t > hranica,
                ">=" => t >= hranica,
                "<" => t < hranica,
                _ => t <= hranica,
            }).ToList();
        }

        var triedy = Zoznam(zvysok).Select(Trieda).ToList();
        return triedy.Any(t => t is null) ? null : triedy.Select(t => t!.Value).Distinct().ToList();
    }

    /// <summary>The codec of the picture as ffprobe names it: "h265", "x265" and "hevc" are one codec.</summary>
    private static string? KodekVidea(string text) => BezDiakritiky(text).Replace(".", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal) switch
    {
        "hevc" or "h265" or "x265" => "hevc",
        "h264" or "x264" or "avc" => "h264",
        "av1" => "av1",
        "vp9" => "vp9",
        "vp8" => "vp8",
        "mpeg2" or "mpeg2video" => "mpeg2video",
        "mpeg4" or "xvid" or "divx" => "mpeg4",
        "vc1" => "vc1",
        _ => null,
    };

    /// <summary>"2h", "1,5h", "90min", "45m", "30s", "1:30" (h:mm), a bare number = minutes – in milliseconds.</summary>
    private static long? CitajDlzku(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace(',', '.');
        if (t.Contains(':', StringComparison.Ordinal))
        {
            var casti = t.Split(':');
            return casti.Length == 2 && int.TryParse(casti[0], NumberStyles.None, Invariant, out var hodiny) &&
                   int.TryParse(casti[1], NumberStyles.None, Invariant, out var minuty) && minuty < 60
                ? (hodiny * 60L + minuty) * 60_000
                : null;
        }

        var koniec = 0;
        while (koniec < t.Length && (char.IsAsciiDigit(t[koniec]) || t[koniec] == '.'))
        {
            koniec++;
        }

        if (koniec == 0 || !double.TryParse(t[..koniec], NumberStyles.AllowDecimalPoint, Invariant, out var cislo))
        {
            return null;
        }

        double? nasobok = t[koniec..] switch
        {
            "h" or "hod" => 3_600_000,
            "" or "m" or "min" => 60_000,
            "s" or "sek" => 1_000,
            _ => null,
        };
        return nasobok is { } n ? (long)Math.Round(cislo * n) : null;
    }

    // ------------------------------------------------------------------ time

    private static (Hranica? Od, Hranica? Do, bool Platne) CitajObdobie(string h)
    {
        if (h.Contains("..", StringComparison.Ordinal))
        {
            var casti = h.Split("..", 2);
            var od = casti[0].Length == 0 ? null : CitajCas(casti[0]);
            var doo = casti[1].Length == 0 ? null : CitajCas(casti[1]);
            var platne = (casti[0].Length == 0 || od is not null) && (casti[1].Length == 0 || doo is not null) && (od is not null || doo is not null);
            // a range of relative bounds runs from the older one: 30d..7d = from 30 days ago to 7 days ago
            return (od?.Zaciatok, doo is { Relativny: true } ? doo.Value.Zaciatok : doo?.Koniec, platne);
        }

        var (op, zvysok) = Operator(h);
        if (CitajCas(zvysok) is not { } cas)
        {
            return (null, null, false);
        }

        if (cas.Relativny)
        {
            // pridane:7d = within the last 7 days; pridane:<7d = longer ago than 7 days
            return op is "<" or "<=" ? (null, cas.Zaciatok, true) : (cas.Zaciatok, null, true);
        }

        return op switch
        {
            ">" => (cas.Koniec, null, true),
            ">=" => (cas.Zaciatok, null, true),
            "<" => (null, cas.Zaciatok, true),
            "<=" => (null, cas.Koniec, true),
            _ => (cas.Zaciatok, cas.Koniec, true),
        };
    }

    /// <summary>A written time: a year, month or day (as an interval), "dnes", "vcera", or "7d", "2t", "3m", "1r" before now.</summary>
    private static CitanyCas? CitajCas(string text)
    {
        var t = BezDiakritiky(text.Trim());
        if (t is "dnes" or "today")
        {
            return CitanyCas.Den(DateTime.Today);
        }

        if (t is "vcera" or "yesterday")
        {
            return CitanyCas.Den(DateTime.Today.AddDays(-1));
        }

        if (t.Length >= 2 && int.TryParse(t[..^1], NumberStyles.None, Invariant, out var pocet) && pocet is > 0 and < 10000)
        {
            JednotkaCasu? jednotka = t[^1] switch
            {
                'd' => JednotkaCasu.Den,
                't' or 'w' => JednotkaCasu.Tyzden,
                'm' => JednotkaCasu.Mesiac,
                'r' or 'y' => JednotkaCasu.Rok,
                _ => null,
            };
            if (jednotka is { } j)
            {
                var hranica = Hranica.Pred(pocet, j);
                return new CitanyCas(hranica, hranica, true);
            }
        }

        if (DateTime.TryParseExact(t, ["yyyy-M-d", "d.M.yyyy", "d. M. yyyy"], Invariant, DateTimeStyles.None, out var den))
        {
            return CitanyCas.Den(den);
        }

        if (DateTime.TryParseExact(t, ["yyyy-M", "M/yyyy", "M.yyyy"], Invariant, DateTimeStyles.None, out var mesiac))
        {
            return new CitanyCas(Hranica.Datum(mesiac), Hranica.Datum(mesiac.AddMonths(1)), false);
        }

        if (t.Length == 4 && int.TryParse(t, NumberStyles.None, Invariant, out var rok) && rok is >= 1900 and <= 2999)
        {
            var zaciatok = new DateTime(rok, 1, 1, 0, 0, 0, DateTimeKind.Local);
            return new CitanyCas(Hranica.Datum(zaciatok), Hranica.Datum(zaciatok.AddYears(1)), false);
        }

        return null;
    }

    private readonly record struct CitanyCas(Hranica Zaciatok, Hranica Koniec, bool Relativny)
    {
        public static CitanyCas Den(DateTime den) => new(Hranica.Datum(den.Date), Hranica.Datum(den.Date.AddDays(1)), false);
    }

    // ------------------------------------------------------------------ text

    /// <summary>Lower case without diacritics ("Zmenené" → "zmenene").</summary>
    public static string BezDiakritiky(string text)
    {
        var rozlozeny = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var vysledok = new StringBuilder(rozlozeny.Length);
        foreach (var znak in rozlozeny)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(znak) != UnicodeCategory.NonSpacingMark)
            {
                vysledok.Append(znak);
            }
        }

        return vysledok.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>A word of the query as written: where it is in the text, a minus before it, a field name, its value.</summary>
    private readonly record struct Token(int Zaciatok, int Dlzka, bool Zaporny, string? Pole, string Hodnota, bool Uvodzovky);

    /// <summary>
    /// Splits the text into words by spaces; quotes keep spaces inside a word ("pán prsteňov", disk:"Filmy 1"); a field
    /// is a name of letters before the first colon outside quotes.
    /// </summary>
    private static IEnumerable<Token> Rozdel(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                yield break;
            }

            var zaciatok = i;
            var zaporny = text[i] == '-' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]);
            if (zaporny)
            {
                i++;
            }

            var slovo = new StringBuilder();
            var vUvodzovkach = false;
            var uvodzovky = false;
            var dvojbodka = -1;
            while (i < text.Length && (vUvodzovkach || !char.IsWhiteSpace(text[i])))
            {
                var znak = text[i++];
                if (znak == '"')
                {
                    vUvodzovkach = !vUvodzovkach;
                    uvodzovky = true;
                    continue;
                }

                if (znak == ':' && !vUvodzovkach && dvojbodka < 0)
                {
                    dvojbodka = slovo.Length;
                }

                slovo.Append(znak);
            }

            var cele = slovo.ToString();
            string? pole = null;
            var hodnota = cele;
            // one letter before a colon is a drive (C:\Filmy), not a field
            if (dvojbodka > 1 && cele[..dvojbodka].All(char.IsLetter))
            {
                pole = cele[..dvojbodka];
                hodnota = cele[(dvojbodka + 1)..];
            }

            // a lone minus or empty quotes search nothing
            if (pole is null && (hodnota.Trim().Length == 0 || (!uvodzovky && hodnota == "-")))
            {
                continue;
            }

            yield return new Token(zaciatok, i - zaciatok, zaporny, pole, hodnota.Trim(), uvodzovky);
        }
    }
}
