using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Katalog.Data;

/// <summary>
/// The single writer of a library (design principle "one writer"). Writes from any thread are queued and committed in
/// batches: one transaction takes every write waiting at that moment (up to <see cref="NajviacVDavke"/>), each inside its
/// own savepoint, so a failing write is undone alone and the others of the batch still commit. Readers never wait for
/// the writer (WAL).
/// </summary>
public sealed class ZapisovaciaFronta : IAsyncDisposable
{
    public const int NajviacVDavke = 512;

    private readonly Channel<Praca> _fronta = Channel.CreateUnbounded<Praca>(new UnboundedChannelOptions { SingleReader = true });
    private readonly string _retazec;
    private readonly Task _beh;

    internal ZapisovaciaFronta(string retazec)
    {
        _retazec = retazec;
        _beh = Task.Run(BehAsync);
    }

    /// <summary>Number of committed batches (for tests and the status line).</summary>
    public long PocetDavok { get; private set; }

    /// <summary>Queues a write; completes when it is committed (or fails with its own exception).</summary>
    public Task ZapisAsync(Action<SqliteConnection> zapis) =>
        ZapisAsync(spojenie =>
        {
            zapis(spojenie);
            return true;
        });

    /// <summary>Queues a write that returns a value (e.g. a new id); completes when it is committed.</summary>
    public Task<T> ZapisAsync<T>(Func<SqliteConnection, T> zapis)
    {
        var praca = new Praca<T>(zapis);
        if (!_fronta.Writer.TryWrite(praca))
        {
            throw new ObjectDisposedException(nameof(ZapisovaciaFronta));
        }

        return praca.Vysledok.Task;
    }

    /// <summary>Completes when everything queued so far is committed.</summary>
    public Task VyprazdniAsync() => ZapisAsync(_ => { });

    public async ValueTask DisposeAsync()
    {
        _fronta.Writer.TryComplete();
        await _beh.ConfigureAwait(false);
    }

    private async Task BehAsync()
    {
        await using var spojenie = await Kniznica.OtvorSpojenieAsync(_retazec, CancellationToken.None).ConfigureAwait(false);
        var davka = new List<Praca>(NajviacVDavke);
        while (await _fronta.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            davka.Clear();
            while (davka.Count < NajviacVDavke && _fronta.Reader.TryRead(out var praca))
            {
                davka.Add(praca);
            }

            Zapis(spojenie, davka);
        }
    }

    private void Zapis(SqliteConnection spojenie, List<Praca> davka)
    {
        try
        {
            Kniznica.Vykonaj(spojenie, "BEGIN IMMEDIATE");
        }
        catch (Exception ex)
        {
            foreach (var praca in davka)
            {
                praca.Zlyhala(ex);
            }

            return;
        }

        var hotove = new List<Praca>(davka.Count);
        foreach (var praca in davka)
        {
            Kniznica.Vykonaj(spojenie, "SAVEPOINT zapis");
            try
            {
                praca.Vykonaj(spojenie);
                Kniznica.Vykonaj(spojenie, "RELEASE zapis");
                hotove.Add(praca);
            }
            catch (Exception ex)
            {
                Kniznica.Vykonaj(spojenie, "ROLLBACK TO zapis");
                Kniznica.Vykonaj(spojenie, "RELEASE zapis");
                praca.Zlyhala(ex);
            }
        }

        try
        {
            Kniznica.Vykonaj(spojenie, "COMMIT");
            PocetDavok++;
            foreach (var praca in hotove)
            {
                praca.Potvrd();
            }
        }
        catch (Exception ex)
        {
            try
            {
                Kniznica.Vykonaj(spojenie, "ROLLBACK");
            }
            catch (SqliteException)
            {
                // the transaction is already gone
            }

            foreach (var praca in hotove)
            {
                praca.Zlyhala(ex);
            }
        }
    }

    private abstract class Praca
    {
        public abstract void Vykonaj(SqliteConnection spojenie);

        public abstract void Potvrd();

        public abstract void Zlyhala(Exception chyba);
    }

    private sealed class Praca<T>(Func<SqliteConnection, T> zapis) : Praca
    {
        private T? _hodnota;

        public TaskCompletionSource<T> Vysledok { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Vykonaj(SqliteConnection spojenie) => _hodnota = zapis(spojenie);

        public override void Potvrd() => Vysledok.TrySetResult(_hodnota!);

        public override void Zlyhala(Exception chyba) => Vysledok.TrySetException(chyba);
    }
}
