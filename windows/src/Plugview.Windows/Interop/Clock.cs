using Windows.Win32;

namespace Plugview.Windows.Interop;

/// <summary>The 100 ns clock Media Foundation timestamps capture samples with (QPC based).</summary>
internal static class Clock
{
    public static long Now100ns()
    {
        PInvoke.QueryPerformanceCounter(out var counter);
        PInvoke.QueryPerformanceFrequency(out var frequency);
        return (long)((Int128)counter * 10_000_000 / frequency);
    }
}
