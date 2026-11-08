using System.Globalization;
using Katalog.Jadro;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>The library file was made by a newer KATALOG (its schema is newer than this program knows).</summary>
public sealed class NovsiaKniznicaException(int verziaSuboru, int verziaProgramu)
    : Exception($"Knižnicu vytvoril novší {Produkt.Nazov} (schéma {verziaSuboru}, tento program pozná {verziaProgramu}). Aktualizuj program.")
{
    public int VerziaSuboru { get; } = verziaSuboru;
}

/// <summary>The file is not a KATALOG library (another SQLite database or something else).</summary>
public sealed class CudziSuborException(string cesta)
    : Exception($"Súbor {cesta} nie je knižnica programu {Produkt.Nazov}.");

/// <summary>Result of the quick integrity check made when a library opens.</summary>
/// <param name="Problemy">What SQLite reported; empty when the file is sound.</param>
public sealed record KontrolaIntegrity(bool JeVPoriadku, IReadOnlyList<string> Problemy);

/// <summary>
/// An open KATALOG library (one SQLite file, decision A1). Opening creates or migrates the schema (A9: a numbered backup
/// first), checks the file's integrity and starts the single writer. Readers take their own connections
/// (<see cref="OtvorCitanie"/>); all writes go through <see cref="Zapis"/>.
/// </summary>
public sealed class Kniznica : IAsyncDisposable
{
    /// <summary>"KATL" – marks the SQLite file as a KATALOG library (PRAGMA application_id).</summary>
    public const int IdAplikacie = 0x4B41544C;

    private readonly string _retazec;

    private Kniznica(string cesta, string retazec, int verziaSchemy, KontrolaIntegrity kontrola, string? zaloha)
    {
        Cesta = cesta;
        _retazec = retazec;
        VerziaSchemy = verziaSchemy;
        Kontrola = kontrola;
        ZalohaPredMigraciou = zaloha;
        Zapis = new ZapisovaciaFronta(retazec);
    }

    public string Cesta { get; }

    public int VerziaSchemy { get; }

    public KontrolaIntegrity Kontrola { get; }

    /// <summary>Backup made before a migration of this opening, if any.</summary>
    public string? ZalohaPredMigraciou { get; }

    /// <summary>The only writer of the library: writes are queued and committed in batches.</summary>
    public ZapisovaciaFronta Zapis { get; }

    /// <summary>
    /// Opens (or creates) the library at <paramref name="cesta"/>. A library made by an older program is migrated after a
    /// numbered backup into <paramref name="zalohy"/> (default: the folder "Zalohy" next to the library).
    /// </summary>
    public static async Task<Kniznica> OtvorAsync(string cesta, ZalohyKniznice? zalohy = null, CancellationToken zrusenie = default)
    {
        cesta = Path.GetFullPath(cesta);
        Directory.CreateDirectory(Path.GetDirectoryName(cesta)!);
        var retazec = Retazec(cesta);
        zalohy ??= ZalohyKniznice.Vedla(cesta);

        await using var spojenie = await OtvorSpojenieAsync(retazec, zrusenie).ConfigureAwait(false);
        KontrolujVerziuSqlite(spojenie);
        var idAplikacie = Convert.ToInt32(Skalar(spojenie, "PRAGMA application_id"), CultureInfo.InvariantCulture);
        var verzia = Convert.ToInt32(Skalar(spojenie, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        var prazdna = Convert.ToInt64(Skalar(spojenie, "SELECT count(*) FROM sqlite_schema"), CultureInfo.InvariantCulture) == 0;

        if (!prazdna && idAplikacie != IdAplikacie)
        {
            throw new CudziSuborException(cesta);
        }

        if (verzia > Migracie.Posledna)
        {
            throw new NovsiaKniznicaException(verzia, Migracie.Posledna);
        }

        var kontrola = prazdna ? new KontrolaIntegrity(true, []) : Skontroluj(spojenie);
        string? zaloha = null;
        if (verzia < Migracie.Posledna)
        {
            if (!prazdna)
            {
                if (!kontrola.JeVPoriadku)
                {
                    // a damaged file is never migrated: the user restores a backup first
                    return new Kniznica(cesta, retazec, verzia, kontrola, null);
                }

                zaloha = zalohy.Vytvor(spojenie, verzia);
            }

            Migracie.Vykonaj(spojenie, verzia);
            verzia = Migracie.Posledna;
        }

        return new Kniznica(cesta, retazec, verzia, kontrola, zaloha);
    }

    /// <summary>A read-only connection for queries (many may be open at once next to the writer).</summary>
    public async Task<SqliteConnection> OtvorCitanieAsync(CancellationToken zrusenie = default)
    {
        var spojenie = await OtvorSpojenieAsync(_retazec, zrusenie).ConfigureAwait(false);
        Vykonaj(spojenie, "PRAGMA query_only = 1");
        return spojenie;
    }

    /// <summary>Synchronous variant for short reads.</summary>
    public SqliteConnection OtvorCitanie()
    {
        var spojenie = OtvorSpojenie(_retazec);
        Vykonaj(spojenie, "PRAGMA query_only = 1");
        return spojenie;
    }

    /// <summary>Quick integrity check of the whole file (PRAGMA quick_check).</summary>
    public KontrolaIntegrity SkontrolujTeraz()
    {
        using var spojenie = OtvorSpojenie(_retazec);
        return Skontroluj(spojenie);
    }

    public async ValueTask DisposeAsync()
    {
        await Zapis.DisposeAsync().ConfigureAwait(false);
        // the library file is closed for good (WAL merged), so it can be copied or backed up right away
        SqliteConnection.ClearAllPools();
    }

    internal static string Retazec(string cesta) => new SqliteConnectionStringBuilder
    {
        DataSource = cesta,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        ForeignKeys = true,
        Pooling = true,
        DefaultTimeout = 30,
    }.ToString();

    internal static async Task<SqliteConnection> OtvorSpojenieAsync(string retazec, CancellationToken zrusenie)
    {
        var spojenie = new SqliteConnection(retazec);
        await spojenie.OpenAsync(zrusenie).ConfigureAwait(false);
        Nastav(spojenie);
        return spojenie;
    }

    internal static SqliteConnection OtvorSpojenie(string retazec)
    {
        var spojenie = new SqliteConnection(retazec);
        spojenie.Open();
        Nastav(spojenie);
        return spojenie;
    }

    internal static SqliteCommand Prikaz(SqliteConnection spojenie, string sql)
    {
        var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        return prikaz;
    }

    internal static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Prikaz(spojenie, sql);
        prikaz.ExecuteNonQuery();
    }

    internal static object? Skalar(SqliteConnection spojenie, string sql)
    {
        using var prikaz = Prikaz(spojenie, sql);
        return prikaz.ExecuteScalar();
    }

    /// <summary>Settings of every connection: WAL (readers never wait for the writer, kept under 64 MB), 64 MB cache, 5 s busy wait.</summary>
    private static void Nastav(SqliteConnection spojenie) =>
        Vykonaj(spojenie, """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -65536;
            PRAGMA journal_size_limit = 67108864;
            PRAGMA foreign_keys = ON;
            PRAGMA query_only = 0;
            """);

    /// <summary>
    /// The schema needs SQLite 3.45 or newer (STRICT tables, contentless-delete full-text index, trigram tokens without
    /// diacritics); the SQLite built into the program is newer, this guards against a wrong runtime.
    /// </summary>
    private static void KontrolujVerziuSqlite(SqliteConnection spojenie)
    {
        var text = Convert.ToString(Skalar(spojenie, "SELECT sqlite_version()"), CultureInfo.InvariantCulture) ?? "0";
        if (!Version.TryParse(text, out var verzia) || verzia < new Version(3, 45))
        {
            throw new NotSupportedException($"{Produkt.Nazov} potrebuje SQLite 3.45 alebo novší, k dispozícii je {text}.");
        }
    }

    private static KontrolaIntegrity Skontroluj(SqliteConnection spojenie)
    {
        var problemy = new List<string>();
        using var prikaz = Prikaz(spojenie, "PRAGMA quick_check(20)");
        using var citac = prikaz.ExecuteReader();
        while (citac.Read())
        {
            var riadok = citac.GetString(0);
            if (riadok != "ok")
            {
                problemy.Add(riadok);
            }
        }

        return new KontrolaIntegrity(problemy.Count == 0, problemy);
    }
}
