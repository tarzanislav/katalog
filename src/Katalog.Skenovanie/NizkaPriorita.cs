using System.Runtime.InteropServices;

namespace Katalog.Skenovanie;

/// <summary>
/// Windows' background mode for the current thread: low disk and processor priority, so a scan of a big disk never slows
/// down what the user is doing. Elsewhere (tests on Linux) it does nothing.
/// </summary>
internal sealed partial class NizkaPriorita : IDisposable
{
    private const int ThreadModeBackgroundBegin = 0x00010000;
    private const int ThreadModeBackgroundEnd = 0x00020000;

    private readonly bool _zapnuta;

    private NizkaPriorita(bool zapnuta) => _zapnuta = zapnuta;

    public static NizkaPriorita Zapni() =>
        new(OperatingSystem.IsWindows() && SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundBegin));

    public void Dispose()
    {
        if (_zapnuta && OperatingSystem.IsWindows())
        {
            SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundEnd);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadPriority(IntPtr vlakno, int priorita);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
}
