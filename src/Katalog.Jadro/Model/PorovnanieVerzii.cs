namespace Katalog.Jadro.Model;

/// <summary>What decides which version of a title is better (in the order the user sets).</summary>
public enum KriteriumVerzie
{
    /// <summary>Higher quality class, then more lines.</summary>
    Rozlisenie = 1,

    /// <summary>Czech or Slovak sound (both count more than one).</summary>
    ZvukCzSk = 2,

    /// <summary>Czech or Slovak subtitles.</summary>
    Titulky = 3,

    /// <summary>A newer codec (AV1, HEVC before H.264 before older ones).</summary>
    Kodek = 4,

    /// <summary>A bigger file (at the same picture, more data usually means better picture).</summary>
    Velkost = 5,

    /// <summary>HDR (Dolby Vision, HDR10, HLG).</summary>
    Hdr = 6,
}

/// <summary>What is known about one version for comparing it with the others (from its present files).</summary>
public sealed record VlastnostiVerzie(
    long VerziaId,
    TriedaKvality Trieda,
    int? Vyska,
    string? Kodek,
    IReadOnlyCollection<string> JazykyZvuku,
    IReadOnlyCollection<string> JazykyTitulkov,
    long Velkost,
    bool Hdr,
    bool Pritomna);

/// <summary>
/// Which version of a title is the best (duplicates, stage E5): the versions are compared by the criteria in the user's
/// order (by default resolution, Czech/Slovak sound, subtitles, codec, size); the first that differs decides.
/// </summary>
public static class PorovnanieVerzii
{
    public static IReadOnlyList<KriteriumVerzie> Predvolene { get; } =
        [KriteriumVerzie.Rozlisenie, KriteriumVerzie.ZvukCzSk, KriteriumVerzie.Titulky, KriteriumVerzie.Kodek, KriteriumVerzie.Velkost];

    /// <summary>The order from Settings ("1,2,3,4,5"): unknown and repeated criteria are left out, missing ones go at the end.</summary>
    public static IReadOnlyList<KriteriumVerzie> ZTextu(string? text)
    {
        var poradie = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => int.TryParse(c, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0)
            .Where(i => Enum.IsDefined(typeof(KriteriumVerzie), i))
            .Select(i => (KriteriumVerzie)i)
            .Distinct()
            .ToList();
        poradie.AddRange(Predvolene.Where(k => !poradie.Contains(k)));
        return poradie;
    }

    public static string NaText(IEnumerable<KriteriumVerzie> poradie) =>
        string.Join(",", poradie.Select(k => ((int)k).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>More than zero when <paramref name="a"/> is better than <paramref name="b"/>, less when worse, zero when equal.</summary>
    public static int Porovnaj(VlastnostiVerzie a, VlastnostiVerzie b, IReadOnlyList<KriteriumVerzie> poradie)
    {
        foreach (var kriterium in poradie)
        {
            var rozdiel = kriterium switch
            {
                KriteriumVerzie.Rozlisenie => ((int)a.Trieda).CompareTo((int)b.Trieda) is var t && t != 0 ? t : (a.Vyska ?? 0).CompareTo(b.Vyska ?? 0),
                KriteriumVerzie.ZvukCzSk => CzSk(a.JazykyZvuku).CompareTo(CzSk(b.JazykyZvuku)),
                KriteriumVerzie.Titulky => CzSk(a.JazykyTitulkov).CompareTo(CzSk(b.JazykyTitulkov)),
                KriteriumVerzie.Kodek => Kodek(a.Kodek).CompareTo(Kodek(b.Kodek)),
                // sizes within 2 % are the same (a remux and its copy differ by a few kilobytes)
                KriteriumVerzie.Velkost => Math.Abs(a.Velkost - b.Velkost) <= Math.Max(a.Velkost, b.Velkost) / 50 ? 0 : a.Velkost.CompareTo(b.Velkost),
                KriteriumVerzie.Hdr => a.Hdr.CompareTo(b.Hdr),
                _ => 0,
            };
            if (rozdiel != 0)
            {
                return rozdiel;
            }
        }

        return 0;
    }

    /// <summary>Two versions whose sizes differ by at most this share count as the same size.</summary>
    public const double ToleranciaVelkosti = 0.10;

    /// <summary>
    /// Two versions are the same (a duplicate, owner 8. 11. 2026) only when nothing tells them apart: the same quality class,
    /// picture height and codec (when known), HDR, the same languages of sound and subtitles and a size differing by at most
    /// <see cref="ToleranciaVelkosti"/>. FHD and 4K, a 16 GB and a 4 GB file or other languages are different versions.
    /// Whether they lie on the same disk (otherwise one is a backup) is decided by the caller.
    /// </summary>
    public static bool SuRovnake(VlastnostiVerzie a, VlastnostiVerzie b)
    {
        if (!a.Pritomna || !b.Pritomna || a.Trieda != b.Trieda || a.Hdr != b.Hdr)
        {
            return false;
        }

        if (a.Vyska is { } va && b.Vyska is { } vb && va != vb)
        {
            return false;
        }

        if (a.Kodek is { } ka && b.Kodek is { } kb && !string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!a.JazykyZvuku.ToHashSet(StringComparer.Ordinal).SetEquals(b.JazykyZvuku) ||
            !a.JazykyTitulkov.ToHashSet(StringComparer.Ordinal).SetEquals(b.JazykyTitulkov))
        {
            return false;
        }

        var mensia = Math.Min(a.Velkost, b.Velkost);
        var vacsia = Math.Max(a.Velkost, b.Velkost);
        return mensia > 0 && vacsia <= mensia * (1 + ToleranciaVelkosti);
    }

    /// <summary>The best present version when there are at least two present ones; null otherwise or when the best ones are equal.</summary>
    public static long? Najlepsia(IEnumerable<VlastnostiVerzie> verzie, IReadOnlyList<KriteriumVerzie> poradie)
    {
        var pritomne = verzie.Where(v => v.Pritomna).ToList();
        if (pritomne.Count < 2)
        {
            return null;
        }

        var zoradene = pritomne.OrderByDescending(v => v, Comparer<VlastnostiVerzie>.Create((a, b) => Porovnaj(a, b, poradie))).ToList();
        return Porovnaj(zoradene[0], zoradene[1], poradie) > 0 ? zoradene[0].VerziaId : null;
    }

    /// <summary>Why a version is better than another, in words ("lepšie rozlíšenie").</summary>
    public static string? Dovod(VlastnostiVerzie lepsia, VlastnostiVerzie horsia, IReadOnlyList<KriteriumVerzie> poradie)
    {
        foreach (var kriterium in poradie)
        {
            if (Porovnaj(lepsia, horsia, [kriterium]) > 0)
            {
                return kriterium switch
                {
                    KriteriumVerzie.Rozlisenie => "lepšie rozlíšenie",
                    KriteriumVerzie.ZvukCzSk => "český alebo slovenský zvuk",
                    KriteriumVerzie.Titulky => "české alebo slovenské titulky",
                    KriteriumVerzie.Kodek => "novší kodek",
                    KriteriumVerzie.Velkost => "väčší súbor",
                    _ => "HDR",
                };
            }
        }

        return null;
    }

    public static string Nazov(KriteriumVerzie kriterium) => kriterium switch
    {
        KriteriumVerzie.Rozlisenie => "Rozlíšenie",
        KriteriumVerzie.ZvukCzSk => "Český alebo slovenský zvuk",
        KriteriumVerzie.Titulky => "České alebo slovenské titulky",
        KriteriumVerzie.Kodek => "Kodek (AV1, HEVC pred H.264)",
        KriteriumVerzie.Velkost => "Veľkosť súboru",
        _ => "HDR",
    };

    private static int CzSk(IReadOnlyCollection<string> jazyky) => (jazyky.Contains("cs") ? 1 : 0) + (jazyky.Contains("sk") ? 1 : 0);

    private static int Kodek(string? kodek) => kodek?.ToLowerInvariant() switch
    {
        null or "" => 0,
        "av1" or "hevc" or "h265" or "vvc" => 3,
        "h264" or "avc" or "vp9" => 2,
        _ => 1,
    };
}
