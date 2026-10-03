using System.Numerics;
using Plugview.Core.Brand;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;

namespace Plugview.Windows.Graphics;

/// <summary>A color with alpha, channels 0…1.</summary>
internal readonly record struct Rgba(float Red, float Green, float Blue, float Alpha = 1)
{
    public static Rgba From(string hex, float alpha = 1)
    {
        var color = BrandColor.Parse(hex);
        return new(color.Red, color.Green, color.Blue, alpha);
    }

    public static Rgba From(BrandColor color, float alpha = 1) => new(color.Red, color.Green, color.Blue, alpha);

    public Rgba WithAlpha(float alpha) => this with { Alpha = alpha };
}

/// <summary>A rectangle in DIPs.</summary>
internal readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    public Box Inset(float by) => new(X + by, Y + by, Width - (2 * by), Height - (2 * by));

    /// <summary>A <paramref name="width"/>×<paramref name="height"/> box centered in this one.</summary>
    public Box Center(float width, float height) => new(X + ((Width - width) / 2), Y + ((Height - height) / 2), width, height);
}

/// <summary>How text sits in its box.</summary>
internal enum TextAlignment
{
    Leading,
    Center,
    Trailing,
}

/// <summary>Direct2D drawing for one overlay frame, in DIPs, with the brand's shapes and fonts.</summary>
internal sealed unsafe class Canvas : IDisposable
{
    private readonly ID2D1DeviceContext* context;
    private readonly ID2D1Factory1* factory;
    private readonly FontSet fonts;
    private readonly D2D_MATRIX_3X2_F origin;
    private ComPtr<ID2D1SolidColorBrush> brush;
    private ComPtr<ID2D1StrokeStyle> roundJoin;

    public Canvas(ID2D1DeviceContext* context, ID2D1Factory1* factory, FontSet fonts, D2D_MATRIX_3X2_F origin, float width, float height)
    {
        this.context = context;
        this.factory = factory;
        this.fonts = fonts;
        this.origin = origin;
        Width = width;
        Height = height;
        var white = new D2D1_COLOR_F { r = 1, g = 1, b = 1, a = 1 };
        ID2D1SolidColorBrush* created;
        context->CreateSolidColorBrush(&white, null, &created);
        brush.Reset(created);
        roundJoin.Reset(StrokeStyle(null));
    }

    public float Width { get; }

    public float Height { get; }

    public Box Bounds => new(0, 0, Width, Height);

    public void FillRectangle(Box box, Rgba color)
    {
        var rect = Rect(box);
        context->FillRectangle(&rect, Brush(color));
    }

    public void FillRoundedRectangle(Box box, float radius, Rgba color)
    {
        var rounded = new D2D1_ROUNDED_RECT { rect = Rect(box), radiusX = radius, radiusY = radius };
        context->FillRoundedRectangle(&rounded, Brush(color));
    }

    public void StrokeRoundedRectangle(Box box, float radius, float width, Rgba color)
    {
        // Strokes straddle the edge: inset by half the width so the line stays inside the box.
        var rounded = new D2D1_ROUNDED_RECT { rect = Rect(box.Inset(width / 2)), radiusX = radius, radiusY = radius };
        context->DrawRoundedRectangle(&rounded, Brush(color), width, null);
    }

    public void FillEllipse(float centerX, float centerY, float radius, Rgba color)
    {
        var ellipse = new D2D1_ELLIPSE { point = new D2D_POINT_2F { x = centerX, y = centerY }, radiusX = radius, radiusY = radius };
        context->FillEllipse(&ellipse, Brush(color));
    }

    public void Line(float x1, float y1, float x2, float y2, float width, Rgba color) =>
        context->DrawLine(new D2D_POINT_2F { x = x1, y = y1 }, new D2D_POINT_2F { x = x2, y = y2 }, Brush(color), width, roundJoin.Get());

    /// <summary>Draws <paramref name="text"/> wrapped to <paramref name="box"/>'s width, vertically centered in it.</summary>
    public void Text(string text, TextStyle style, Box box, Rgba color, TextAlignment alignment = TextAlignment.Center)
    {
        using var layout = Layout(text, style, box.Width, box.Height, alignment);
        context->DrawTextLayout(new D2D_POINT_2F { x = box.X, y = box.Y }, (IDWriteTextLayout*)layout.Get(), Brush(color), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
    }

    /// <summary>The size <paramref name="text"/> takes, wrapped at <paramref name="maxWidth"/>.</summary>
    public (float Width, float Height) Measure(string text, TextStyle style, float maxWidth = float.MaxValue)
    {
        using var layout = Layout(text, style, maxWidth, float.MaxValue, TextAlignment.Leading);
        DWRITE_TEXT_METRICS metrics;
        layout.Get()->GetMetrics(&metrics);
        return (metrics.widthIncludingTrailingWhitespace, metrics.height);
    }

    /// <summary>Fills <paramref name="box"/> with a radial gradient from <paramref name="inner"/> at the center to <paramref name="outer"/>.</summary>
    public void FillRadialGradient(Box box, float centerX, float centerY, float radius, Rgba inner, Rgba outer)
    {
        var stops = stackalloc D2D1_GRADIENT_STOP[2];
        stops[0] = new D2D1_GRADIENT_STOP { position = 0, color = Color(inner) };
        stops[1] = new D2D1_GRADIENT_STOP { position = 1, color = Color(outer) };
        ID2D1GradientStopCollection* collection;
        ((ID2D1RenderTarget*)context)->CreateGradientStopCollection(stops, 2, D2D1_GAMMA.D2D1_GAMMA_2_2, D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, &collection);
        using var stopCollection = new ComPtr<ID2D1GradientStopCollection>(collection);
        var properties = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
        {
            center = new D2D_POINT_2F { x = centerX, y = centerY },
            radiusX = radius,
            radiusY = radius,
        };
        ID2D1RadialGradientBrush* gradient;
        ((ID2D1RenderTarget*)context)->CreateRadialGradientBrush(&properties, null, stopCollection.Get(), &gradient);
        using var gradientBrush = new ComPtr<ID2D1RadialGradientBrush>(gradient);
        var rect = Rect(box);
        context->FillRectangle(&rect, (ID2D1Brush*)gradientBrush.Get());
    }

    /// <summary>Draws <paramref name="text"/> with <paramref name="tracking"/> DIPs between letters (the Mac's <c>.tracking</c>).</summary>
    public void TrackedText(string text, TextStyle style, Box box, Rgba color, float tracking)
    {
        using var layout = Layout(text, style, box.Width, box.Height, TextAlignment.Center);
        using var spaced = layout.TryAs<IDWriteTextLayout1>();
        if (!spaced.IsNull)
        {
            // The spacing follows every letter, the last one too; shifting by half of it keeps the word centered.
            spaced.Get()->SetCharacterSpacing(0, tracking, 0, new DWRITE_TEXT_RANGE { startPosition = 0, length = (uint)text.Length });
        }
        context->DrawTextLayout(new D2D_POINT_2F { x = box.X + (tracking / 2), y = box.Y }, (IDWriteTextLayout*)layout.Get(), Brush(color), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
    }

    /// <summary>One icon glyph (Segoe Fluent Icons code point) centered in <paramref name="box"/>.</summary>
    public void Icon(char glyph, float size, Box box, Rgba color) => Text(glyph.ToString(), new TextStyle(Typeface.Icons, size), box, color);

    /// <summary>Draws the rest of the frame, until <see cref="PopLayer"/>, at <paramref name="opacity"/>.</summary>
    public void PushOpacity(float opacity)
    {
        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = new D2D_RECT_F { left = float.MinValue, top = float.MinValue, right = float.MaxValue, bottom = float.MaxValue },
            maskTransform = Identity,
            opacity = Math.Clamp(opacity, 0, 1),
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };
        context->PushLayer(&parameters, null);
    }

    public void PopLayer() => context->PopLayer();

    /// <summary>Draws a brand artwork frame fitted (centered, proportions kept) into <paramref name="box"/>.</summary>
    public void Art(ArtFrame frame, Box box)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var scale = Math.Min(box.Width / frame.ViewBoxWidth, box.Height / ArtFrame.ViewBoxHeight);
        var placement = Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateTranslation(box.X + ((box.Width - (frame.ViewBoxWidth * scale)) / 2), box.Y + ((box.Height - (ArtFrame.ViewBoxHeight * scale)) / 2));
        DrawNode(frame.Root, placement);
        SetTransform(Matrix3x2.Identity);
    }

    private void DrawNode(ArtNode node, Matrix3x2 transform)
    {
        switch (node)
        {
            case ArtGroup group:
                DrawGroup(group, group.Transform * transform);
                break;
            case ArtPolygon polygon:
                SetTransform(transform);
                using (var geometry = Polygon(polygon.Points))
                {
                    if (polygon.StrokeWidth is not { } width)
                    {
                        context->FillGeometry((ID2D1Geometry*)geometry.Get(), Brush(Rgba.From(polygon.Color)), null);
                    }
                    else if (polygon.Dash is { } dash)
                    {
                        // Direct2D measures dashes in stroke widths; the kit (CSS) measures them in units.
                        using var dashed = new ComPtr<ID2D1StrokeStyle>(StrokeStyle([.. dash.Select(length => length / width)]));
                        context->DrawGeometry((ID2D1Geometry*)geometry.Get(), Brush(Rgba.From(polygon.Color)), width, dashed.Get());
                    }
                    else
                    {
                        context->DrawGeometry((ID2D1Geometry*)geometry.Get(), Brush(Rgba.From(polygon.Color)), width, roundJoin.Get());
                    }
                }
                break;
            case ArtRectangle rectangle:
                SetTransform(transform);
                var box = new Box(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                if (rectangle.CornerRadius > 0) FillRoundedRectangle(box, rectangle.CornerRadius, Rgba.From(rectangle.Color));
                else FillRectangle(box, Rgba.From(rectangle.Color));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(node), node, "Unhandled art node");
        }
    }

    private void DrawGroup(ArtGroup group, Matrix3x2 transform)
    {
        if (group.Opacity <= 0) return;
        var needsLayer = group.Opacity < 1 || group.Clip is not null;
        ComPtr<ID2D1PathGeometry> clip = default;
        if (needsLayer)
        {
            if (group.Clip is { } points) clip = Polygon(points);
            var parameters = new D2D1_LAYER_PARAMETERS1
            {
                contentBounds = new D2D_RECT_F { left = float.MinValue, top = float.MinValue, right = float.MaxValue, bottom = float.MaxValue },
                geometricMask = (ID2D1Geometry*)clip.Get(),
                maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
                maskTransform = Matrix(transform),
                opacity = Math.Min(group.Opacity, 1),
                layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
            };
            SetTransform(Matrix3x2.Identity);
            context->PushLayer(&parameters, null);
        }
        try
        {
            foreach (var child in group.Children) DrawNode(child, transform);
        }
        finally
        {
            if (needsLayer) context->PopLayer();
            clip.Dispose();
        }
    }

    private ComPtr<ID2D1PathGeometry> Polygon(IReadOnlyList<Vector2> points)
    {
        ID2D1PathGeometry* geometry;
        factory->CreatePathGeometry(&geometry);
        var path = new ComPtr<ID2D1PathGeometry>(geometry);
        ID2D1GeometrySink* sink;
        geometry->Open(&sink);
        using var sinkReference = new ComPtr<ID2D1GeometrySink>(sink);
        sink->BeginFigure(new D2D_POINT_2F { x = points[0].X, y = points[0].Y }, D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
        for (var index = 1; index < points.Count; index++) sink->AddLine(new D2D_POINT_2F { x = points[index].X, y = points[index].Y });
        sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
        sink->Close();
        return path;
    }

    private ID2D1StrokeStyle* StrokeStyle(float[]? dashes)
    {
        var properties = new D2D1_STROKE_STYLE_PROPERTIES
        {
            startCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_FLAT,
            endCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_FLAT,
            dashCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_FLAT,
            lineJoin = D2D1_LINE_JOIN.D2D1_LINE_JOIN_ROUND,
            miterLimit = 10,
            dashStyle = dashes is null ? D2D1_DASH_STYLE.D2D1_DASH_STYLE_SOLID : D2D1_DASH_STYLE.D2D1_DASH_STYLE_CUSTOM,
        };
        ID2D1StrokeStyle* style;
        fixed (float* dashLengths = dashes)
        {
            ((ID2D1Factory*)factory)->CreateStrokeStyle(&properties, dashLengths, dashes is null ? 0 : (uint)dashes.Length, &style);
        }
        return style;
    }

    private ComPtr<IDWriteTextLayout> Layout(string text, TextStyle style, float width, float height, TextAlignment alignment)
    {
        var format = fonts.Format(style);
        IDWriteTextLayout* layout;
        fixed (char* characters = text)
        {
            fonts.Factory->CreateTextLayout(characters, (uint)text.Length, format, width, height, &layout);
        }
        layout->SetTextAlignment(alignment switch
        {
            TextAlignment.Leading => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            TextAlignment.Trailing => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unhandled alignment"),
        });
        if (height < float.MaxValue) layout->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        return new ComPtr<IDWriteTextLayout>(layout);
    }

    private static D2D1_COLOR_F Color(Rgba color) => new() { r = color.Red, g = color.Green, b = color.Blue, a = color.Alpha };

    private ID2D1Brush* Brush(Rgba color)
    {
        var value = Color(color);
        brush.Get()->SetColor(&value);
        return (ID2D1Brush*)brush.Get();
    }

    private void SetTransform(Matrix3x2 transform)
    {
        var matrix = Combine(Matrix(transform), origin);
        context->SetTransform(&matrix);
    }

    private static D2D_MATRIX_3X2_F Identity => new() { _11 = 1, _22 = 1 };

    private static D2D_MATRIX_3X2_F Matrix(Matrix3x2 transform) =>
        new() { _11 = transform.M11, _12 = transform.M12, _21 = transform.M21, _22 = transform.M22, _31 = transform.M31, _32 = transform.M32 };

    private static D2D_MATRIX_3X2_F Combine(D2D_MATRIX_3X2_F first, D2D_MATRIX_3X2_F then) => new()
    {
        _11 = (first._11 * then._11) + (first._12 * then._21),
        _12 = (first._11 * then._12) + (first._12 * then._22),
        _21 = (first._21 * then._11) + (first._22 * then._21),
        _22 = (first._21 * then._12) + (first._22 * then._22),
        _31 = (first._31 * then._11) + (first._32 * then._21) + then._31,
        _32 = (first._31 * then._12) + (first._32 * then._22) + then._32,
    };

    private static D2D_RECT_F Rect(Box box) => new() { left = box.X, top = box.Y, right = box.Right, bottom = box.Bottom };

    public void Dispose()
    {
        roundJoin.Dispose();
        brush.Dispose();
    }
}
