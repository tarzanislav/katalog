using Katalog.Data;
using Katalog.Jadro.Model;
using Katalog.Media;
using Katalog.Ulohy;

namespace Katalog.Okno;

/// <summary>Reading of technical data of one disk as the window shows it.</summary>
internal sealed record BeziaceCitanie(long DiskId, string NazovDisku, PriebehCitania Priebeh);

/// <summary>
/// Reads technical data of videos (stage E3): one disk at a time per disk, different disks side by side, with low
/// priority. Every run is a job of the persistent queue: closing the program leaves it waiting, so it goes on next time.
/// A reading the user stopped waits until the user starts it again or the program starts next time.
/// </summary>
internal sealed class SpravcaCitania(Kniznica kniznica, UlozisteObrazkov obrazky)
{
    private readonly Dictionary<long, (CancellationTokenSource Zrusenie, BeziaceCitanie Citanie, bool Pouzivatel)> _beziace = [];
    private readonly HashSet<long> _pozastavene = [];

    /// <summary>Raised on the window's thread whenever a reading starts, moves or ends.</summary>
    public event Action? Zmena;

    /// <summary>Raised on the window's thread when a reading ends.</summary>
    public event Action<long, VysledokCitania>? Skoncilo;

    public IReadOnlyList<BeziaceCitanie> Beziace => _beziace.Values.Select(b => b.Citanie).ToList();

    public bool Bezi(long diskId) => _beziace.ContainsKey(diskId);

    /// <summary>The user stopped it in this session: it does not start by itself again.</summary>
    public bool Pozastavene(long diskId) => _pozastavene.Contains(diskId);

    /// <summary>Starts reading a disk (unless it already runs); <paramref name="pouzivatel"/> = the user asked for it.</summary>
    public async Task<VysledokCitania?> SpustiAsync(long diskId, string nazovDisku, string koren, NastrojeFfmpeg nastroje, bool nahlady, bool pouzivatel)
    {
        if (_beziace.ContainsKey(diskId) || (!pouzivatel && _pozastavene.Contains(diskId)))
        {
            return null;
        }

        _pozastavene.Remove(diskId);
        using var zrusenie = new CancellationTokenSource();
        _beziace[diskId] = (zrusenie, new BeziaceCitanie(diskId, nazovDisku, default), false);
        Zmena?.Invoke();
        try
        {
            var uloha = await kniznica.Zapis.ZapisAsync(s =>
            {
                var id = UlohyKniznice.Naplanuj(s, DruhUlohy.Ffprobe, Pruhy.Disk(diskId), diskId, Cas.Teraz());
                UlohyKniznice.Zacni(s, id, Cas.Teraz());
                return id;
            }).ConfigureAwait(true);

            var priebeh = new Progress<PriebehCitania>(p =>
            {
                if (_beziace.TryGetValue(diskId, out var b))
                {
                    _beziace[diskId] = b with { Citanie = b.Citanie with { Priebeh = p } };
                    Zmena?.Invoke();
                }
            });
            var vysledok = await Task.Run(() => CitanieMedii.SpustiAsync(
                kniznica, diskId, koren, new Ffmpeg(nastroje), nahlady ? obrazky : null, priebeh, zrusenie.Token)).ConfigureAwait(true);

            var odPouzivatela = _beziace.TryGetValue(diskId, out var bezi) && bezi.Pouzivatel;
            // stopped because the program closes: the job waits (Bezi → Caka when the library opens next time)
            if (vysledok.Stav != StavUlohy.Zrusena || odPouzivatela)
            {
                var stav = vysledok.Stav == StavUlohy.CakaNaDisk ? StavUlohy.CakaNaDisk : vysledok.Stav;
                await kniznica.Zapis.ZapisAsync(s => UlohyKniznice.Skonci(s, uloha, stav, vysledok.Sprava, Cas.Teraz())).ConfigureAwait(true);
            }

            if (odPouzivatela)
            {
                _pozastavene.Add(diskId);
            }

            _beziace.Remove(diskId);
            Zmena?.Invoke();
            Skoncilo?.Invoke(diskId, vysledok);
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

    /// <summary>The user stops reading a disk; what was read stays.</summary>
    public void Zrus(long diskId)
    {
        if (_beziace.TryGetValue(diskId, out var b))
        {
            _beziace[diskId] = b with { Pouzivatel = true };
            b.Zrusenie.Cancel();
        }
    }

    /// <summary>The program closes: everything stops and waits for the next time.</summary>
    public void ZrusVsetky()
    {
        foreach (var b in _beziace.Values)
        {
            b.Zrusenie.Cancel();
        }
    }
}
