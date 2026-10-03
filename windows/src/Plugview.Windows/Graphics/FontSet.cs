using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.System.Com;

namespace Plugview.Windows.Graphics;

/// <summary>The typefaces plugview draws with.</summary>
internal enum Typeface
{
    /// <summary>Geist: text.</summary>
    Sans,

    /// <summary>Geist Mono: labels, status lines, the controls' numbers.</summary>
    Mono,

    /// <summary>Segoe Fluent Icons (Windows 11), else Segoe MDL2 Assets: the controls' line icons.</summary>
    Icons,
}

/// <summary>A text style: typeface, size in DIPs and weight (400 regular, 500 medium, 600 semibold).</summary>
internal readonly record struct TextStyle(Typeface Typeface, float Size, int Weight = 400);

/// <summary>
/// Geist and Geist Mono, embedded in the app (SIL OFL) and given to DirectWrite from memory, plus the system icon
/// font. Nothing is installed on the PC.
/// </summary>
internal sealed unsafe class FontSet : IDisposable
{
    private static readonly string[] EmbeddedFonts = ["Geist-Regular.ttf", "Geist-Medium.ttf", "Geist-SemiBold.ttf", "GeistMono-Medium.ttf", "GeistMono-SemiBold.ttf"];

    private readonly Dictionary<TextStyle, ComPtr<IDWriteTextFormat>> formats = [];
    private ComPtr<IDWriteFactory5> factory;
    private ComPtr<IDWriteInMemoryFontFileLoader> loader;
    private ComPtr<IDWriteFontCollection1> brandFonts;
    private readonly string iconFamily;

    public FontSet()
    {
        var factoryId = IDWriteFactory5.IID_Guid;
        void* created;
        PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &factoryId, &created).ThrowOnFailure();
        factory.Reset((IDWriteFactory5*)created);
        LoadBrandFonts();
        iconFamily = HasSystemFamily("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public IDWriteFactory5* Factory => factory.Get();

    /// <summary>A text format for <paramref name="style"/>, made once and kept.</summary>
    public IDWriteTextFormat* Format(TextStyle style)
    {
        if (formats.TryGetValue(style, out var existing)) return existing.Get();
        var family = style.Typeface switch
        {
            Typeface.Sans => "Geist",
            Typeface.Mono => "Geist Mono",
            Typeface.Icons => iconFamily,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style.Typeface, "Unhandled typeface"),
        };
        // The icon font is a system font; Geist comes from the embedded set.
        var collection = style.Typeface == Typeface.Icons ? null : (IDWriteFontCollection*)brandFonts.Get();
        IDWriteTextFormat* format;
        fixed (char* familyName = family)
        fixed (char* locale = "en-us")
        {
            factory.Get()->CreateTextFormat(
                familyName, collection, (DWRITE_FONT_WEIGHT)style.Weight, DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                style.Size, locale, &format);
        }
        formats[style] = new ComPtr<IDWriteTextFormat>(format);
        return format;
    }

    /// <summary>Hands the embedded TTFs to DirectWrite; a missing resource leaves the system's fallback font in charge.</summary>
    private void LoadBrandFonts()
    {
        IDWriteInMemoryFontFileLoader* createdLoader;
        factory.Get()->CreateInMemoryFontFileLoader(&createdLoader);
        loader.Reset(createdLoader);
        factory.Get()->RegisterFontFileLoader((IDWriteFontFileLoader*)loader.Get());

        IDWriteFontSetBuilder1* createdBuilder;
        factory.Get()->CreateFontSetBuilder(&createdBuilder);
        using var builder = new ComPtr<IDWriteFontSetBuilder1>(createdBuilder);
        var assembly = typeof(FontSet).Assembly;
        foreach (var name in EmbeddedFonts)
        {
            using var stream = assembly.GetManifestResourceStream($"Plugview.Windows.Fonts.{name}");
            if (stream is null) continue;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            IDWriteFontFile* file;
            fixed (byte* data = bytes)
            {
                // No owner object: DirectWrite keeps its own copy of the bytes.
                loader.Get()->CreateInMemoryFontFileReference((IDWriteFactory*)factory.Get(), data, (uint)bytes.Length, null, &file);
            }
            using var fontFile = new ComPtr<IDWriteFontFile>(file);
            builder.Get()->AddFontFile(fontFile.Get());
        }
        IDWriteFontSet* set;
        builder.Get()->CreateFontSet(&set);
        using var fontSet = new ComPtr<IDWriteFontSet>(set);
        IDWriteFontCollection1* collection;
        factory.Get()->CreateFontCollectionFromFontSet(fontSet.Get(), &collection);
        brandFonts.Reset(collection);
    }

    private bool HasSystemFamily(string family)
    {
        IDWriteFontCollection* system;
        factory.Get()->GetSystemFontCollection(&system, false);
        using var collection = new ComPtr<IDWriteFontCollection>(system);
        uint index;
        BOOL exists;
        fixed (char* name = family) collection.Get()->FindFamilyName(name, &index, &exists);
        return exists;
    }

    public void Dispose()
    {
        foreach (var format in formats.Values) format.Dispose();
        formats.Clear();
        brandFonts.Dispose();
        if (!loader.IsNull) factory.Get()->UnregisterFontFileLoader((IDWriteFontFileLoader*)loader.Get());
        loader.Dispose();
        factory.Dispose();
    }
}
