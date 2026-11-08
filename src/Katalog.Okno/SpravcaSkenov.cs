using Katalog.Data;
using Katalog.Jadro.Rozhrania;
using Katalog.Ulohy;

namespace Katalog.Okno;

/// <summary>A running scan as the window shows it.</summary>
internal sealed record BeziaciSken(long DiskId, string NazovDisku, PriebehSkenu? Priebeh);

/// <summary>
/// Runs scans: one at a time per disk, different disks side by side (each has its own lane, so a slow USB disk never
/// holds up another). Progress arrives on the window's thread.
/// </summary>
internal sealed class SpravcaSkenov(Kniznica kniznica)
{
    private readonly Dictionary<long, (CancellationTokenSource Zrusenie, BeziaciSken Sken)> _beziace = [];

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
        _beziace[diskId] = (zrusenie, new BeziaciSken(diskId, nazovDisku, null));
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
            var vysledok = await SkenovanieDisku.SpustiAsync(kniznica, diskId, ulozisko, priebeh, zrusenie.Token).ConfigureAwait(true);
            _beziace.Remove(diskId);
            Zmena?.Invoke();
            Skoncil?.Invoke(diskId, vysledok);
            return vysledok;
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
