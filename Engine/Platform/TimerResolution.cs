using System.Runtime.InteropServices;

namespace Engine.Platform;
internal sealed partial class TimerResolution : IDisposable
{
    [LibraryImport("winmm.dll")] private static partial uint timeBeginPeriod(uint ms);
    [LibraryImport("winmm.dll")] private static partial uint timeEndPeriod(uint ms);
    private readonly bool active;

    public TimerResolution()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = timeBeginPeriod(1); active = true;
    }
    public void Dispose() { if (active) _ = timeEndPeriod(1); }
}
