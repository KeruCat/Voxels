using System.Diagnostics;

namespace Engine.Core.Time;
internal sealed class FramePacer(int fps)
{
    private readonly Stopwatch sw = Stopwatch.StartNew();
    private readonly double target = fps > 0 ? 1.0 / fps : 0;

    public void Wait()                     
    {
        if (target <= 0) return;
        double rest = target - sw.Elapsed.TotalSeconds;
        if (rest > 0.003) Thread.Sleep(Math.Max(1, (int)((rest - 0.002) * 1000)));
        while (sw.Elapsed.TotalSeconds < target) Thread.SpinWait(50);
        sw.Restart();
    }
}
