import Foundation
import Testing

@testable import PlugviewUI

/// Checks the Mac's brand against `spec/fixtures/brand.json`, which the Windows and Linux apps draw from too, so no
/// platform drifts from the brand kit.
@MainActor
@Suite struct BrandSpecTests {
    struct Spec: Decodable {
        let palette: [String: String]
        let bars: [String]
        let geometry: Geometry
        let animations: Animations
    }

    struct Geometry: Decodable {
        let port: [[Double]]
        let triangle: [[Double]]
        let connector: [[Double]]
        let connectorWindow: [[Double]]
        let prongs: [[Double]]
        let prongCornerRadius: Double
        let portStrokeWidth: Double
        let triangleStrokeWidth: Double
        let barStarts: [Double]
        let barWidth: Double
        let connectorBars: Bars
        let triangleBars: Bars
    }

    struct Bars: Decodable {
        let x: Double
        let scaleX: Double
        let liveSecondCopyX: Double?
    }

    struct Animations: Decodable {
        let waiting: Waiting
        let intro: Intro
        let live: Live
    }

    struct Track: Decodable {
        let frames: [[Double]]
        let easing: [Double]
    }

    struct Waiting: Decodable {
        let viewBoxWidth: Double
        let connectorTranslate: [Double]
        let connectorScale: Double
        let hoverSeconds: Double
        let hover: Track
        let blinkSeconds: Double
        let dimFromSeconds: Double
        let dimOpacity: Double
        let portOffsetX: Double
        let portOpacity: Double
        let portDash: [Double]
    }

    struct Intro: Decodable {
        let delaySeconds: Double
        let durationSeconds: Double
        let flashRect: [Double]
        let flashCornerRadius: Double
        let flashCenter: [Double]
        let connectorCenter: [Double]
        let triangleCenter: [Double]
        let tracks: [String: Track]
    }

    struct Live: Decodable {
        let scrollSeconds: Double
        let scrollDistance: Double
    }

    let spec: Spec

    init() throws {
        let url = URL(filePath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "spec/fixtures/brand.json")
        spec = try JSONDecoder().decode(Spec.self, from: Data(contentsOf: url))
    }

    @Test func colorsMatchTheBrandKit() {
        let mac: [String: UInt32] = [
            "cathode": BrandColors.cathode, "surface": BrandColors.surface, "raised": BrandColors.raised, "line": BrandColors.line,
            "bone": BrandColors.bone, "muted": BrandColors.muted, "signal": BrandColors.signal, "tally": BrandColors.tally, "ok": BrandColors.ok,
            "glow": BrandColors.glow,
        ]
        #expect(mac.mapValues(Self.hex) == spec.palette)
        #expect(BrandColors.bars.map(Self.hex) == spec.bars)
    }

    @Test func geometryMatchesTheBrandKit() {
        let geometry = spec.geometry
        #expect(Self.points(BrandGeometry.portPoints) == geometry.port)
        #expect(Self.points(BrandGeometry.trianglePoints) == geometry.triangle)
        #expect(Self.points(BrandGeometry.connectorPoints) == geometry.connector)
        #expect(Self.points(BrandGeometry.connectorWindowPoints) == geometry.connectorWindow)
        #expect(BrandGeometry.prongs.map(Self.rect) == geometry.prongs)
        #expect(BrandGeometry.prongCornerRadius == geometry.prongCornerRadius)
        #expect(Double(BrandGeometry.portStroke.lineWidth) == geometry.portStrokeWidth)
        #expect(Double(BrandGeometry.triangleStroke.lineWidth) == geometry.triangleStrokeWidth)
        #expect(BrandGeometry.barStarts == geometry.barStarts)
        #expect(BrandGeometry.barWidth == geometry.barWidth)
        #expect(BrandGeometry.connectorBars.x == geometry.connectorBars.x && BrandGeometry.connectorBars.scaleX == geometry.connectorBars.scaleX)
        #expect(BrandGeometry.triangleBars.x == geometry.triangleBars.x && BrandGeometry.triangleBars.scaleX == geometry.triangleBars.scaleX)
        #expect(BrandGeometry.triangleBars.liveSecondCopyX == geometry.triangleBars.liveSecondCopyX)
    }

    @Test func waitingLoopMatchesTheBrandKit() {
        let waiting = spec.animations.waiting
        #expect(WaitingAnimation.viewBoxWidth == waiting.viewBoxWidth)
        #expect([WaitingAnimation.connectorTranslate.x, WaitingAnimation.connectorTranslate.y] == waiting.connectorTranslate)
        #expect(WaitingAnimation.connectorScale == waiting.connectorScale)
        #expect(WaitingAnimation.hoverSeconds == waiting.hoverSeconds)
        #expect(Self.track(WaitingAnimation.hover) == waiting.hover)
        #expect(WaitingAnimation.blinkSeconds == waiting.blinkSeconds)
        #expect(WaitingAnimation.dimFromSeconds == waiting.dimFromSeconds)
        #expect(WaitingAnimation.dimOpacity == waiting.dimOpacity)
        #expect(WaitingAnimation.portOffsetX == waiting.portOffsetX)
        #expect(WaitingAnimation.portOpacity == waiting.portOpacity)
        #expect(WaitingAnimation.portDash.map(Double.init) == waiting.portDash)
    }

    @Test func introAndLiveLoopMatchTheBrandKit() {
        let intro = spec.animations.intro
        #expect(SignalAnimation.Intro.delay == intro.delaySeconds)
        #expect(SignalAnimation.Intro.duration == intro.durationSeconds)
        #expect(Self.rect(SignalAnimation.Intro.flashRect) == intro.flashRect)
        #expect(SignalAnimation.Intro.flashCornerRadius == intro.flashCornerRadius)
        #expect(Self.point(SignalAnimation.Intro.flashCenter) == intro.flashCenter)
        #expect(Self.point(SignalAnimation.Intro.connectorCenter) == intro.connectorCenter)
        #expect(Self.point(SignalAnimation.Intro.triangleCenter) == intro.triangleCenter)
        let mac: [String: Keyframes] = [
            "portOpacity": SignalAnimation.portOpacity, "flashScaleX": SignalAnimation.flashScaleX, "flashScaleY": SignalAnimation.flashScaleY,
            "flashOpacity": SignalAnimation.flashOpacity, "connectorX": SignalAnimation.connectorX, "connectorScale": SignalAnimation.connectorScale,
            "connectorOpacity": SignalAnimation.connectorOpacity, "triangleScale": SignalAnimation.triangleScale,
            "triangleOpacity": SignalAnimation.triangleOpacity,
        ]
        #expect(mac.mapValues(Self.track) == intro.tracks)
        #expect(SignalAnimation.Live.scrollSeconds == spec.animations.live.scrollSeconds)
        #expect(SignalAnimation.Live.scrollDistance == spec.animations.live.scrollDistance)
    }

    private static func hex(_ value: UInt32) -> String { String(format: "#%06X", value) }

    private static func points(_ points: [(Double, Double)]) -> [[Double]] { points.map { [$0.0, $0.1] } }

    // CoreGraphics values become Double first: Swift Testing compares CGFloat and Double as different types.
    private static func point(_ point: CGPoint) -> [Double] { [Double(point.x), Double(point.y)] }

    private static func rect(_ rect: CGRect) -> [Double] { [rect.minX, rect.minY, rect.width, rect.height].map { Double($0) } }

    private static func track(_ keyframes: Keyframes) -> Track {
        let easing = keyframes.easing
        return Track(frames: keyframes.frames.map { [$0.at, $0.value] }, easing: [easing.x1, easing.y1, easing.x2, easing.y2])
    }
}

extension BrandSpecTests.Track: Equatable {}
