using Windows.Devices.Enumeration;

namespace Plugview.Windows.Devices;

/// <summary>
/// Hot-plug: Windows' device watchers for video and audio capture report cards coming and going; a burst of changes
/// (a card's video, then its audio a moment later) becomes one <see cref="Changed"/> on the given context.
/// </summary>
internal sealed class DeviceWatch : IDisposable
{
    /// <summary>A replugged card shows its video a moment before its audio (Tuning.CardAudioWaitSeconds).</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    private readonly DeviceWatcher[] watchers;
    private readonly SynchronizationContext context;
    private readonly Lock gate = new();
    private bool isPending;
    private bool hasEnumerated;

    public DeviceWatch(SynchronizationContext context)
    {
        this.context = context;
        watchers = [DeviceInformation.CreateWatcher(DeviceClass.VideoCapture), DeviceInformation.CreateWatcher(DeviceClass.AudioCapture)];
        foreach (var watcher in watchers)
        {
            watcher.Added += (_, _) => Schedule();
            watcher.Removed += (_, _) => Schedule();
            watcher.Updated += (_, _) => Schedule();
            watcher.EnumerationCompleted += (_, _) =>
            {
                // The initial "Added" burst describes devices the app already listed when it started.
                lock (gate) hasEnumerated = true;
            };
            watcher.Start();
        }
    }

    /// <summary>The devices changed; raised on the context given at construction.</summary>
    public event Action? Changed;

    private void Schedule()
    {
        lock (gate)
        {
            if (!hasEnumerated || isPending) return;
            isPending = true;
        }
        _ = Task.Delay(Settle).ContinueWith(
            _ =>
            {
                lock (gate) isPending = false;
                context.Post(_ => Changed?.Invoke(), null);
            },
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
        }
    }
}
