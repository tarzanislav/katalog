using System.Globalization;
using Katalog.Jadro;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>
/// Numbered backups of the library ("KATALOG-zaloha-0007-v1-20261108-0830.katalog"): made before every migration and on
/// request, by SQLite's own online backup (consistent even while the program runs). The newest <see cref="Pocet"/> stay.
/// </summary>
public sealed class ZalohyKniznice(string priecinok, int pocet = 10)
{
    public string Priecinok { get; } = priecinok;

    public int Pocet { get; } = pocet > 0 ? pocet : throw new ArgumentOutOfRangeException(nameof(pocet));

    /// <summary>Backups in the folder "Zalohy" next to the library.</summary>
    public static ZalohyKniznice Vedla(string cestaKniznice) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cestaKniznice))!, "Zalohy"));

    /// <summary>Existing backups, oldest first.</summary>
    public IReadOnlyList<(int Cislo, string Cesta)> Zoznam()
    {
        if (!Directory.Exists(Priecinok))
        {
            return [];
        }

        var predpona = Predpona;
        return Directory.EnumerateFiles(Priecinok, predpona + "*" + Produkt.PriponaKniznice)
            .Select(c => (Cislo: Cislo(Path.GetFileName(c), predpona), Cesta: c))
            .Where(z => z.Cislo > 0)
            .OrderBy(z => z.Cislo)
            .ToList();
    }

    /// <summary>Backs up the library of <paramref name="spojenie"/>; returns the path of the new backup.</summary>
    public string Vytvor(SqliteConnection spojenie, int verziaSchemy)
    {
        Directory.CreateDirectory(Priecinok);
        var existujuce = Zoznam();
        var cislo = existujuce.Count == 0 ? 1 : existujuce[^1].Cislo + 1;
        var nazov = string.Create(CultureInfo.InvariantCulture,
            $"{Predpona}{cislo:D4}-v{verziaSchemy}-{DateTime.Now:yyyyMMdd-HHmmss}{Produkt.PriponaKniznice}");
        var ciel = Path.Combine(Priecinok, nazov);
        var docasny = ciel + ".tmp";
        using (var spojenieZalohy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = docasny, Pooling = false }.ToString()))
        {
            spojenieZalohy.Open();
            spojenie.BackupDatabase(spojenieZalohy);
        }

        // a backup appears under its name only when it is complete
        File.Move(docasny, ciel);
        foreach (var (_, stara) in Zoznam().SkipLast(Pocet))
        {
            File.Delete(stara);
        }

        return ciel;
    }

    private static string Predpona => Produkt.Nazov + "-zaloha-";

    private static int Cislo(string nazov, string predpona) =>
        nazov.Length > predpona.Length + 4 && int.TryParse(nazov.AsSpan(predpona.Length, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var cislo)
            ? cislo
            : 0;
}
