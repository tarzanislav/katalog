using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Jadro.Rozhrania;
using Katalog.Skenovanie;
using Katalog.Ulohy;

namespace Katalog.Okno;

/// <summary>A running scan as the window shows it.</summary>
internal sealed record BeziaciSken(long DiskId, string NazovDisku, PriebehSkenu? Priebeh);

/// <summary>
/// Runs scans: one at a time per disk, different disks side by side (each has its own lane, so a slow USB disk never
/// holds up another). Progress arrives on the window's thread. Every scan is a job of the persistent queue (stage E3):
/// a scan cut short by closing the program waits and goes on the next time its disk is connected.
/// </summary>
internal sealed class SpravcaSkenov(Kniznica kniznica)
{
    private readonly Dictionary<long, (CancellationTokenSource Zrusenie, BeziaciSken Sken)> _beziace = [];
    private readonly HashSet<long> _zrusenePouzivatelom = [];

    /// <summary>Raised on the window's thread whenever a scan starts, moves or ends.</summary>
    public event Action? Zmena;

    /// <summary>Raised on the window's thread when a scan ends.</summary>
    public event Action<long, VysledokSkenu>? Skoncil;

    public IReadOnlyList<BeziaciSken> Beziace => _beziace.Values.Select(b => b.Sken).ToList();

    public bool Bezi(long diskId) => _beziace.ContainsKey(diskId);

    public async Task<VysledokSkenu?> SpustiAsync(long diskId, string nazovDisku, IUlozisko ulozisko)
    {
        if (_beziace.ContainsKey(diskId))
        {
            return null;
        }

        using var zrusenie = new CancellationTokenSource();
        if (ulozisko is WinScpUlozisko server)
        {
            // stopping the scan stops WinSCP too
            server.Zrusenie = zrusenie.Token;
        }

        _beziace[diskId] = (zrusenie, new BeziaciSken(diskId, nazovDisku, null));
        _zrusenePouzivatelom.Remove(diskId);
        Zmena?.Invoke();
        var priebeh = new Progress<PriebehSkenu>(p =>
        {
            if (_beziace.TryGetValue(diskId, out var b))
            {
                _beziace[diskId] = (b.Zrusenie, b.Sken with { Priebeh = p });
                Zmena?.Invoke();
            }
        });

        try
        {
            var uloha = await kniznica.Zapis.ZapisAsync(s =>
            {
                var id = UlohyKniznice.Naplanuj(s, DruhUlohy.Skenovanie, Pruhy.Disk(diskId), diskId, Cas.Teraz());
                UlohyKniznice.Zacni(s, id, Cas.Teraz());
                return id;
            }).ConfigureAwait(true);
            var vysledok = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, priebeh, zrusenie.Token).ConfigureAwait(true);

            // stopped because the program closes: the job stays running in the library and waits again next time
            if (vysledok.Stav != StavUlohy.Zrusena || _zrusenePouzivatelom.Contains(diskId))
            {
                await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Skonci(s, uloha, vysledok.Stav, vysledok.Sprava, Cas.Teraz())).ConfigureAwait(true);
            }

            _beziace.Remove(diskId);
            Zmena?.Invoke();
            Skoncil?.Invoke(diskId, vysledok);
            return vysledok;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // the library closed in the meantime: the job stays in the queue for the next time
            return null;
        }
        finally
        {
            if (_beziace.Remove(diskId))
            {
                Zmena?.Invoke();
            }
        }
    }

    public void Zrus(long diskId)
    {
        if (_beziace.TryGetValue(diskId, out var b))
        {
            _zrusenePouzivatelom.Add(diskId);
            b.Zrusenie.Cancel();
        }
    }

    public void ZrusVsetky()
    {
        foreach (var b in _beziace.Values)
        {
            b.Zrusenie.Cancel();
        }
    }
}
