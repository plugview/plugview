using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugview.Windows.App;

/// <summary>
/// What the app remembers between launches, in <c>%LOCALAPPDATA%\plugview\settings.json</c> (the packaged app's own
/// folder under MSIX). The command remembers nothing.
/// </summary>
internal sealed class Settings
{
    /// <summary>The card the user picked last, by unique ID.</summary>
    public string? PreferredDeviceId { get; set; }

    /// <summary>The mode picked per card (<c>ModeChoice</c> keys such as <c>1920x1080@60</c>); absent is Automatic.</summary>
    public Dictionary<string, string> ModeChoices { get; set; } = [];

    /// <summary>Full-range colors per card.</summary>
    public Dictionary<string, bool> ExpandsLimitedRange { get; set; } = [];

    /// <summary>The window's restored position and size in pixels.</summary>
    public int[]? WindowFrame { get; set; }

    public bool KeepsOnTop { get; set; }

    /// <summary>Opens the window when a card is plugged in while plugview waits in the tray.</summary>
    public bool OpensOnPlugIn { get; set; } = true;

    /// <summary>Closing the window keeps plugview in the tray (and releases the card).</summary>
    public bool KeepsRunningInTray { get; set; } = true;

    /// <summary>The packaged app's first run explained the permissions already.</summary>
    public bool HasSeenPermissions { get; set; }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "plugview", "settings.json");

    /// <summary>The saved settings; defaults when there are none or the file cannot be read.</summary>
    public static Settings Load()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.Settings) ?? new Settings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or damaged file is a first launch: start from the defaults.
            return new Settings();
        }
    }

    /// <summary>Writes the settings; a failure is reported, never fatal (the next change tries again).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary)) JsonSerializer.Serialize(stream, this, SettingsJsonContext.Default.Settings);
            // Replace in one step, so a crash mid-write never leaves half a file.
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"plugview: settings not saved: {error.Message}");
        }
    }
}

/// <summary>Source-generated JSON for <see cref="Settings"/> (NativeAOT has no reflection).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
