using System.Threading.Channels;
using Katalog.Jadro.Rozhrania;

namespace Katalog.Skenovanie;

/// <summary>One listed folder of a scan; <see cref="Chyba"/> is set when it could not be read.</summary>
/// <param name="Cesta">Path relative to the storage's root ("" is the root, parts joined by '/').</param>
public sealed record ObsahPriecinka(string Cesta, IReadOnlyList<PolozkaUloziska> Polozky, string? Chyba);

/// <summary>
/// Walks a storage on its own thread, with the low disk priority of Windows' background mode, so the computer stays usable
/// and the window never waits. Folders come depth-first, every folder before its subfolders; the walk stops at once when
/// cancelled. A folder that cannot be read is reported and its subtree is skipped (it stays in the library as it was).
/// </summary>
public static class Prechadzac
{
    public static ChannelReader<ObsahPriecinka> Spusti(IUlozisko ulozisko, CancellationToken zrusenie, int kapacita = 256)
    {
        var kanal = Channel.CreateBounded<ObsahPriecinka>(new BoundedChannelOptions(kapacita) { SingleReader = true, SingleWriter = true });
        var vlakno = new Thread(() => Prechadzaj(ulozisko, kanal.Writer, zrusenie))
        {
            IsBackground = true,
            Name = "Skenovanie " + ulozisko.Koren,
        };
        vlakno.Start();
        return kanal.Reader;
    }

    private static void Prechadzaj(IUlozisko ulozisko, ChannelWriter<ObsahPriecinka> vystup, CancellationToken zrusenie)
    {
        using var priorita = NizkaPriorita.Zapni();
        try
        {
            var naRade = new Stack<string>();
            naRade.Push("");
            while (naRade.Count > 0)
            {
                zrusenie.ThrowIfCancellationRequested();
                var cesta = naRade.Pop();
                ObsahPriecinka obsah;
                try
                {
                    obsah = new ObsahPriecinka(cesta, ulozisko.Polozky(cesta).ToList(), null);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    obsah = new ObsahPriecinka(cesta, [], ex.Message);
                }

                Zapis(vystup, obsah, zrusenie);
                // subfolders in reverse, so they come out in their natural order
                for (var i = obsah.Polozky.Count - 1; i >= 0; i--)
                {
                    if (obsah.Polozky[i].JePriecinok)
                    {
                        naRade.Push(cesta.Length == 0 ? obsah.Polozky[i].Nazov : cesta + "/" + obsah.Polozky[i].Nazov);
                    }
                }
            }

            vystup.TryComplete();
        }
        catch (OperationCanceledException ex)
        {
            vystup.TryComplete(ex);
        }
        catch (Exception ex)
        {
            vystup.TryComplete(ex);
        }
    }

    private static void Zapis(ChannelWriter<ObsahPriecinka> vystup, ObsahPriecinka obsah, CancellationToken zrusenie)
    {
        while (!vystup.TryWrite(obsah))
        {
            // the reader is busy (the library writes): wait on this thread, the walk has nothing else to do
            if (!vystup.WaitToWriteAsync(zrusenie).AsTask().GetAwaiter().GetResult())
            {
                throw new OperationCanceledException(zrusenie);
            }
        }
    }
}
