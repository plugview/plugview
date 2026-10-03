namespace Plugview.Core.Brand;

/// <summary>A CSS <c>cubic-bezier()</c> timing function, so the native animations follow the brand SVGs exactly.</summary>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    /// <summary>CSS <c>ease</c>.</summary>
    public static readonly CubicBezier Ease = new(0.25, 0.1, 0.25, 1);

    /// <summary>CSS <c>ease-in-out</c>.</summary>
    public static readonly CubicBezier EaseInOut = new(0.42, 0, 0.58, 1);

    /// <summary>CSS <c>linear</c>.</summary>
    public static readonly CubicBezier Linear = new(0, 0, 1, 1);

    /// <summary>Eased progress for linear progress <paramref name="x"/> in 0...1.</summary>
    public double Evaluate(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        // Bisection on the curve parameter: x(t) is monotonic for valid CSS curves, and 30 steps is far below a pixel.
        double low = 0, high = 1, t = x;
        for (var step = 0; step < 30; step++)
        {
            t = (low + high) / 2;
            if (Coordinate(t, X1, X2) < x) low = t;
            else high = t;
        }
        return Coordinate(t, Y1, Y2);
    }

    private static double Coordinate(double t, double p1, double p2)
    {
        var u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }
}

/// <summary>One animated property: values at points of the animation's progress, like a CSS <c>@keyframes</c> rule.</summary>
/// <param name="Frames">(progress, value) pairs in increasing progress.</param>
/// <param name="Easing">The curve applied within each segment.</param>
public sealed record Keyframes(IReadOnlyList<(double At, double Value)> Frames, CubicBezier Easing)
{
    /// <summary>The value at <paramref name="progress"/>; before the first and after the last keyframe it holds (CSS <c>fill-mode: both</c>).</summary>
    public double ValueAt(double progress)
    {
        if (Frames.Count == 0) return 0;
        if (progress <= Frames[0].At) return Frames[0].Value;
        for (var index = 1; index < Frames.Count; index++)
        {
            var (fromAt, fromValue) = Frames[index - 1];
            var (toAt, toValue) = Frames[index];
            if (progress <= toAt) return fromValue + ((toValue - fromValue) * Easing.Evaluate((progress - fromAt) / (toAt - fromAt)));
        }
        return Frames[^1].Value;
    }
}
