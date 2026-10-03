using Plugview.Core.CommandLine;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Plugview.Windows.App;

/// <summary>
/// Starts plugview's window: from <c>plugview.exe</c> (the command) or <c>plugview-app.exe</c> (the Start menu entry,
/// spec "Launch mode": two executables). The window lives on its own STA thread, so COM and UI Automation calls reach it
/// in order through its message loop.
/// </summary>
public static class AppHost
{
    /// <summary>A second app launch posts this to the running one, which shows its window.</summary>
    private const uint ActivateMessage = PInvoke.WM_APP + 3;
    private const string AppMutexName = @"Local\app.plugview.plugview";

    /// <summary>The command's window: plays per <paramref name="options"/>, closing it quits; the exit code.</summary>
    public static int RunCommand(RunOptions options) => RunOnWindowThread(options, LaunchMode.Command, startsHidden: false);

    /// <summary>
    /// The app: one instance (a second launch shows the first one's window), with the tray. With
    /// <paramref name="startsHidden"/> (launch at login) it waits in the tray without a window.
    /// </summary>
    public static int RunApp(bool startsHidden)
    {
        using var single = new Mutex(initiallyOwned: true, AppMutexName, out var isFirst);
        if (!isFirst)
        {
            if (!startsHidden) ActivateRunningApp();
            return 0;
        }
        return RunOnWindowThread(CommandParser.Parse([]) is Command.Run run ? run.Options : throw new InvalidOperationException("no default options"), LaunchMode.App, startsHidden);
    }

    private static int RunOnWindowThread(RunOptions options, LaunchMode launchMode, bool startsHidden)
    {
        var exitCode = 1;
        var thread = new Thread(() => exitCode = RunWindow(options, launchMode, startsHidden)) { Name = "plugview window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private static int RunWindow(RunOptions options, LaunchMode launchMode, bool startsHidden)
    {
        var uiThread = new UiThread();
        SynchronizationContext.SetSynchronizationContext(uiThread);
        try
        {
            using var controller = new AppController(uiThread, options, launchMode);
            controller.Start(startsHidden);
            return AppController.Run();
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"plugview: cannot open the window: {error.Message}");
            return 1;
        }
    }

    private static void ActivateRunningApp()
    {
        var window = PInvoke.FindWindow(AppWindow.AppClassName, null);
        if (window != HWND.Null) PInvoke.PostMessage(window, ActivateMessage, default, default);
    }

    /// <summary>Whether <paramref name="message"/> is a second launch asking the running app to show itself.</summary>
    internal static bool IsActivateMessage(uint message) => message == ActivateMessage;
}
