using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Plugview.Windows.App;

/// <summary>
/// Runs work on the window's thread: <c>await</c> resumes there, and capture or device-watcher callbacks hop back to it
/// with <see cref="Post(Action)"/>. Messages go through the window's queue, so ordering with input is kept.
/// </summary>
internal sealed class UiThread : SynchronizationContext
{
    /// <summary>The message <see cref="AppWindow"/> answers by running <see cref="Drain"/>.</summary>
    public const uint WorkMessage = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
    private HWND window;

    /// <summary>Starts delivering to <paramref name="target"/>; work posted before this waits for it.</summary>
    public void Attach(HWND target)
    {
        window = target;
        if (!queue.IsEmpty) PInvoke.PostMessage(window, WorkMessage, default, default);
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        queue.Enqueue((d, state));
        if (window != HWND.Null) PInvoke.PostMessage(window, WorkMessage, default, default);
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("plugview never blocks on the window's thread");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs everything queued so far; called on the window's thread.</summary>
    public void Drain()
    {
        while (queue.TryDequeue(out var work))
        {
            try
            {
                work.Callback(work.State);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A failed callback must not take the message loop down with it.
                Console.Error.WriteLine($"plugview: {error.Message}");
            }
        }
    }

    /// <summary>Runs <paramref name="action"/> on the window's thread.</summary>
    public void Post(Action action) => Post(_ => action(), null);
}
