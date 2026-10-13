
using System.Diagnostics;

namespace Engine;
internal static class AppClock
{
    private static readonly long T0 = Stopwatch.GetTimestamp();
    public static float Seconds => (float)Stopwatch.GetElapsedTime(T0).TotalSeconds;
}