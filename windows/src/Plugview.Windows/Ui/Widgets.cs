using Plugview.Core.Brand;
using Plugview.Windows.Graphics;

namespace Plugview.Windows.Ui;

/// <summary>The palette from <c>brand.json</c>, as drawing colors.</summary>
internal static class Palette
{
    private static readonly BrandPalette Brand = BrandKit.Current.Palette;

    public static Rgba Cathode { get; } = Rgba.From(Brand.Cathode);

    public static Rgba Surface { get; } = Rgba.From(Brand.Surface);

    public static Rgba Raised { get; } = Rgba.From(Brand.Raised);

    public static Rgba Line { get; } = Rgba.From(Brand.Line);

    public static Rgba Bone { get; } = Rgba.From(Brand.Bone);

    public static Rgba Muted { get; } = Rgba.From(Brand.Muted);

    public static Rgba Signal { get; } = Rgba.From(Brand.Signal);

    public static Rgba Tally { get; } = Rgba.From(Brand.Tally);

    public static Rgba Ok { get; } = Rgba.From(Brand.Ok);

    public static Rgba Glow { get; } = Rgba.From(Brand.Glow);
}

/// <summary>Segoe Fluent Icons code points (the same glyphs are in Segoe MDL2 Assets on Windows 10).</summary>
internal static class Glyphs
{
    public const char Play = '';
    public const char Volume = '';
    public const char Mute = '';
    public const char Camera = '';
    public const char Video = '';
    public const char Speaker = '';
    public const char FullScreen = '';
    public const char BackToWindow = '';
    public const char Devices = '';
    public const char Settings = '';
    public const char Warning = '';
    public const char Minimize = '';
    public const char Maximize = '';
    public const char Restore = '';
    public const char Close = '';
    public const char Check = '';
    public const char ChevronDown = '';
}

/// <summary>The brand's widgets, drawn and registered in one call.</summary>
internal static class Widgets
{
    private static readonly TextStyle ButtonText = new(Typeface.Mono, 13, 500);

    /// <summary>
    /// Amber primary button or an outlined one, in Geist Mono, centered on <paramref name="centerX"/> with its top at
    /// <paramref name="top"/>; brightens on hover and sinks while pressed. Returns its height.
    /// </summary>
    public static float BrandButton(Canvas canvas, UiFrame frame, Interaction interaction, string id, string label, bool isPrimary, float centerX, float top, Action invoke, char? glyph = null)
    {
        const float horizontalPadding = 18, height = 40, glyphWidth = 20;
        var (textWidth, _) = canvas.Measure(label, ButtonText);
        var width = textWidth + (2 * horizontalPadding) + (glyph is null ? 0 : glyphWidth);
        var box = new Box(centerX - (width / 2), top, width, height);
        frame.Add(new UiElement(id, box, UiRole.Button, label, invoke));

        var isHovered = interaction.IsHovered(id);
        var drawn = interaction.IsPressed(id) ? box.Center(width * 0.96f, height * 0.96f) : box;
        if (isPrimary)
        {
            if (isHovered) canvas.FillRoundedRectangle(drawn.Inset(-3), 12, Palette.Signal.WithAlpha(0.18f));
            canvas.FillRoundedRectangle(drawn, 9, isHovered ? Lighter(Palette.Signal, 0.06f) : Palette.Signal);
        }
        else
        {
            if (isHovered) canvas.FillRoundedRectangle(drawn, 9, new Rgba(1, 1, 1, 0.06f));
            canvas.StrokeRoundedRectangle(drawn, 9, 1, isHovered ? Lighter(Palette.Line, 0.12f) : Palette.Line);
        }
        var color = isPrimary ? Palette.Cathode : Palette.Bone;
        var textBox = drawn;
        if (glyph is { } icon)
        {
            canvas.Icon(icon, 12, new Box(drawn.X + horizontalPadding - 2, drawn.Y, glyphWidth, drawn.Height), color);
            textBox = drawn with { X = drawn.X + glyphWidth, Width = drawn.Width - glyphWidth };
        }
        canvas.Text(label, ButtonText, textBox, color);
        FocusRing(canvas, interaction, id, drawn, 9);
        return height;
    }

    /// <summary>A found card: a green dot, its name and what it plays; pickable (with a play arrow) when there are several.</summary>
    public static float DeviceRow(Canvas canvas, UiFrame frame, Interaction interaction, string id, string name, string details, float centerX, float top, Action? play)
    {
        const float width = 340, height = 62;
        var box = new Box(centerX - (width / 2), top, width, height);
        frame.Add(new UiElement(id, box, play is null ? UiRole.Text : UiRole.Button, play is null ? name : $"Play {name}", play));
        var isHovered = play is not null && interaction.IsHovered(id);
        canvas.FillRoundedRectangle(box, 10, isHovered ? Palette.Raised.WithAlpha(0.6f) : Palette.Raised);
        if (isHovered) canvas.FillRoundedRectangle(box, 10, Palette.Signal.WithAlpha(0.08f));
        canvas.StrokeRoundedRectangle(box, 10, 1, isHovered ? Palette.Signal.WithAlpha(0.5f) : Palette.Line);

        canvas.FillEllipse(box.X + 18, box.Y + (height / 2), 7, Palette.Ok.WithAlpha(0.25f));
        canvas.FillEllipse(box.X + 18, box.Y + (height / 2), 4, Palette.Ok);
        var textX = box.X + 34;
        var textWidth = width - 34 - (play is null ? 14 : 40);
        var hasDetails = details.Length > 0;
        canvas.Text(name, new TextStyle(Typeface.Sans, 15, 600), new Box(textX, box.Y + (hasDetails ? 11 : 0), textWidth, hasDetails ? 20 : height), Palette.Bone, TextAlignment.Leading);
        if (hasDetails) canvas.Text(details, new TextStyle(Typeface.Mono, 12, 500), new Box(textX, box.Y + 33, textWidth, 18), Palette.Muted, TextAlignment.Leading);
        if (play is not null) canvas.Icon(Glyphs.Play, 12, new Box(box.Right - 36, box.Y, 24, height), Palette.Signal);
        FocusRing(canvas, interaction, id, box, 10);
        return height;
    }

    /// <summary>The keyboard focus ring, shown only once the keyboard is in use.</summary>
    public static void FocusRing(Canvas canvas, Interaction interaction, string id, Box box, float radius)
    {
        if (interaction.IsFocused(id)) canvas.StrokeRoundedRectangle(box.Inset(-4), radius + 4, 2, Palette.Bone);
    }

    public static Rgba Lighter(Rgba color, float amount) =>
        new(Math.Min(1, color.Red + amount), Math.Min(1, color.Green + amount), Math.Min(1, color.Blue + amount), color.Alpha);
}
