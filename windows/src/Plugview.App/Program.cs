using Plugview.Windows.App;

// Started from the Start menu, or at login with --background (wait in the tray, no window).
return AppHost.RunApp(startsHidden: args.Contains("--background"));
