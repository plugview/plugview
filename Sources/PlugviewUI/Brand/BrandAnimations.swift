import SwiftUI

/// The static mark: HDMI port with the play triangle filled with SMPTE bars (plugview-mark.svg).
struct MarkView: View {
    var body: some View {
        Canvas { context, size in
            BrandGeometry.fit(&context, viewBoxWidth: 64, into: size)
            context.stroke(BrandGeometry.port, with: .color(Palette.bone), style: BrandGeometry.portStroke)
            BrandGeometry.drawTriangle(in: context)
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityHidden(true)
    }
}

/// The empty state: the connector hovers beside a dashed port with blinking prongs (plugview-waiting.svg).
struct WaitingAnimation: View {
    @Environment(\.accessibilityReduceMotion) private var isReduceMotionOn

    // Checked against `spec/fixtures/brand.json` ("waiting").
    static let viewBoxWidth = 128.0
    static let connectorTranslate = (x: -2.0, y: 9.0)
    static let connectorScale = 0.72
    static let hoverSeconds = 2.4
    static let hover = Keyframes([(0, 0), (0.5, 6), (1, 0)], easing: .easeInOut)
    static let blinkSeconds = 1.2
    static let dimFromSeconds = 0.6
    static let dimOpacity = 0.2
    static let portOffsetX = 64.0
    static let portOpacity = 0.35
    static let portDash: [CGFloat] = [3, 3]

    var body: some View {
        TimelineView(.animation(paused: isReduceMotionOn)) { timeline in
            let seconds = isReduceMotionOn ? 0 : timeline.date.timeIntervalSinceReferenceDate
            Canvas { context, size in
                BrandGeometry.fit(&context, viewBoxWidth: Self.viewBoxWidth, into: size)
                var connector = context
                connector.translateBy(x: Self.connectorTranslate.x, y: Self.connectorTranslate.y)
                connector.scaleBy(x: Self.connectorScale, y: Self.connectorScale)
                connector.translateBy(x: Self.hover.value(at: seconds.truncatingRemainder(dividingBy: Self.hoverSeconds) / Self.hoverSeconds), y: 0)
                // CSS steps(1): full brightness for the first half of each blink, dim for the second.
                let isProngDim = seconds.truncatingRemainder(dividingBy: Self.blinkSeconds) >= Self.dimFromSeconds
                BrandGeometry.drawConnector(in: connector, prongOpacity: isProngDim ? Self.dimOpacity : 1)

                var port = context
                port.translateBy(x: Self.portOffsetX, y: 0)
                port.opacity = Self.portOpacity
                port.stroke(
                    BrandGeometry.port, with: .color(Palette.bone),
                    style: StrokeStyle(lineWidth: BrandGeometry.portStroke.lineWidth, lineJoin: .round, dash: Self.portDash))
            }
        }
        .aspectRatio(2, contentMode: .fit)
        .accessibilityHidden(true)
    }
}

/// A card was found: the connector plugs into the port, the signal flashes and the triangle lights up
/// (plugview-intro.svg), then the bars keep scrolling (plugview-live.svg).
struct SignalAnimation: View {
    @Environment(\.accessibilityReduceMotion) private var isReduceMotionOn
    @State private var start = Date()

    /// The intro's CSS delay and duration; it ends at `Intro.length`.
    enum Intro {
        static let delay = 0.2
        static let duration = 2.6
        static let length = delay + duration
        static let flashRect = CGRect(x: 11, y: 31, width: 42, height: 2)
        static let flashCornerRadius = 1.0
        static let flashCenter = CGPoint(x: 32, y: 32)
        static let connectorCenter = CGPoint(x: 28.5, y: 32)
        static let triangleCenter = CGPoint(x: 34.25, y: 32)
    }

    /// The live loop: the bars scroll this far through the triangle in this long.
    enum Live {
        static let scrollSeconds = 2.8
        static let scrollDistance = 15.5
    }

    var body: some View {
        TimelineView(.animation(paused: isReduceMotionOn)) { timeline in
            let elapsed = isReduceMotionOn ? Intro.length : timeline.date.timeIntervalSince(start)
            Canvas { context, size in
                BrandGeometry.fit(&context, viewBoxWidth: 64, into: size)
                if elapsed < Intro.length {
                    Self.drawIntro(in: context, progress: (elapsed - Intro.delay) / Intro.duration)
                } else {
                    let scrollSeconds = (elapsed - Intro.length).truncatingRemainder(dividingBy: Live.scrollSeconds)
                    context.stroke(BrandGeometry.port, with: .color(Palette.bone), style: BrandGeometry.portStroke)
                    BrandGeometry.drawTriangle(in: context, barsOffset: scrollSeconds / Live.scrollSeconds * Live.scrollDistance)
                }
            }
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityHidden(true)
    }

    // Keyframes copied from plugview-intro.svg, checked against `spec/fixtures/brand.json` ("intro").
    static let portOpacity = Keyframes([(0, 0.28), (0.52, 0.28), (0.58, 1)], easing: .ease)
    static let flashScaleX = Keyframes([(0, 0), (0.5, 0), (0.55, 1)], easing: .ease)
    static let flashScaleY = Keyframes([(0, 1), (0.55, 1), (0.64, 7)], easing: .ease)
    static let flashOpacity = Keyframes([(0, 0), (0.5, 0), (0.55, 1), (0.64, 0)], easing: .ease)
    static let plugEasing = CubicBezier(x1: 0.65, y1: 0, x2: 0.35, y2: 1)
    static let connectorX = Keyframes([(0, -46), (0.44, -2), (0.52, 0)], easing: plugEasing)
    static let connectorScale = Keyframes([(0, 0.5), (0.44, 0.5), (0.52, 0.3), (0.58, 0.25)], easing: plugEasing)
    static let connectorOpacity = Keyframes([(0, 0), (0.12, 1), (0.52, 1), (0.58, 0)], easing: plugEasing)
    static let popEasing = CubicBezier(x1: 0.2, y1: 1.4, x2: 0.4, y2: 1)
    static let triangleScale = Keyframes([(0, 0.4), (0.56, 0.4), (0.78, 1)], easing: popEasing)
    static let triangleOpacity = Keyframes([(0, 0), (0.56, 0), (0.7, 1)], easing: popEasing)

    private static func drawIntro(in context: GraphicsContext, progress: Double) {
        var port = context
        port.opacity = portOpacity.value(at: progress)
        port.stroke(BrandGeometry.port, with: .color(Palette.bone), style: BrandGeometry.portStroke)

        var flash = context
        flash.opacity = flashOpacity.value(at: progress)
        BrandGeometry.scale(&flash, x: flashScaleX.value(at: progress), y: flashScaleY.value(at: progress), about: Intro.flashCenter)
        flash.fill(Path(roundedRect: Intro.flashRect, cornerRadius: Intro.flashCornerRadius), with: .color(Palette.signal))

        var connector = context
        connector.opacity = connectorOpacity.value(at: progress)
        connector.translateBy(x: connectorX.value(at: progress), y: 0)
        let connectorScaleValue = connectorScale.value(at: progress)
        BrandGeometry.scale(&connector, x: connectorScaleValue, y: connectorScaleValue, about: Intro.connectorCenter)
        BrandGeometry.drawConnector(in: connector, prongOpacity: 1)

        var triangle = context
        triangle.opacity = triangleOpacity.value(at: progress)
        let triangleScaleValue = triangleScale.value(at: progress)
        BrandGeometry.scale(&triangle, x: triangleScaleValue, y: triangleScaleValue, about: Intro.triangleCenter)
        BrandGeometry.drawTriangle(in: triangle)
    }
}
