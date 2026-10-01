import SwiftUI

/// The mark's parts in the brand kit's 64-unit space (plugview-design/*.svg), drawn with SwiftUI paths.
enum BrandGeometry {
    /// The HDMI port outline.
    static let port = polygon([(7, 17), (57, 17), (57, 37), (48, 47), (16, 47), (7, 37)])
    /// The play triangle inside the port.
    static let triangle = polygon([(26.5, 22), (42, 32), (26.5, 42)])
    /// The sideways HDMI connector body, and the window its color bars show through.
    static let connector = polygon([(18, 12), (35, 12), (53, 26.5), (53, 37.5), (35, 52), (18, 52)])
    static let connectorWindow = polygon([(23, 17.5), (33, 17.5), (46.5, 28.5), (46.5, 35.5), (33, 46.5), (23, 46.5)])
    static let prongs = [CGRect(x: 4, y: 22, width: 12, height: 5), CGRect(x: 4, y: 37, width: 12, height: 5)]

    static let portStroke = StrokeStyle(lineWidth: 4.5, lineJoin: .round)
    static let triangleStroke = StrokeStyle(lineWidth: 1.5, lineJoin: .round)

    /// Draws the seven SMPTE bars, 64 units wide before `scaleX`, starting at `x`.
    static func drawBars(in context: GraphicsContext, x: Double, scaleX: Double) {
        let barStarts = [0, 9.14, 18.28, 27.42, 36.56, 45.7, 54.84]
        for (start, color) in zip(barStarts, Palette.bars) {
            context.fill(Path(CGRect(x: x + start * scaleX, y: 0, width: 9.16 * scaleX, height: 64)), with: .color(color))
        }
    }

    /// The connector with its bars and amber prongs, as in the waiting loop and the intro.
    static func drawConnector(in context: GraphicsContext, prongOpacity: Double) {
        var bars = context
        bars.clip(to: connectorWindow)
        drawBars(in: bars, x: 23, scaleX: 0.37)
        context.stroke(connector, with: .color(Palette.bone), style: portStroke)
        var prongLayer = context
        prongLayer.opacity *= prongOpacity
        for prong in prongs {
            prongLayer.fill(Path(roundedRect: prong, cornerRadius: 2.5), with: .color(Palette.signal))
        }
    }

    /// The play triangle filled with the bars (`barsOffset` scrolls them, for the live loop) and its outline.
    static func drawTriangle(in context: GraphicsContext, barsOffset: Double? = nil) {
        var bars = context
        bars.clip(to: triangle)
        if let barsOffset {
            drawBars(in: bars, x: 11 + barsOffset, scaleX: 0.242)
            drawBars(in: bars, x: 26.5 + barsOffset, scaleX: 0.242)
        } else {
            drawBars(in: bars, x: 26.5, scaleX: 0.242)
        }
        context.stroke(triangle, with: .color(Palette.bone), style: triangleStroke)
    }

    /// Maps the kit's `width`×64 view box onto `size`, centered, keeping its proportions.
    static func fit(_ context: inout GraphicsContext, viewBoxWidth: Double, into size: CGSize) {
        let scale = min(size.width / viewBoxWidth, size.height / 64)
        context.translateBy(x: (size.width - viewBoxWidth * scale) / 2, y: (size.height - 64 * scale) / 2)
        context.scaleBy(x: scale, y: scale)
    }

    /// Scales about a point, like CSS `transform-origin` with `transform-box: fill-box`.
    static func scale(_ context: inout GraphicsContext, x: Double, y: Double, about center: CGPoint) {
        context.translateBy(x: center.x, y: center.y)
        context.scaleBy(x: x, y: y)
        context.translateBy(x: -center.x, y: -center.y)
    }

    private static func polygon(_ points: [(Double, Double)]) -> Path {
        Path { path in
            path.addLines(points.map { CGPoint(x: $0.0, y: $0.1) })
            path.closeSubpath()
        }
    }
}
