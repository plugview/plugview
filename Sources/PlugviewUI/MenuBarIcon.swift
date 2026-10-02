import AppKit
import PlugviewCore

/// The menu bar icon: the mark's port, with the play triangle outlined once a card has signal and filled while it plays.
///
/// A template image, so macOS tints it for light, dark, and highlighted menu bars.
public enum MenuBarIcon {
    public static func image(for state: LauncherState) -> NSImage {
        let triangle: Triangle =
            switch state {
            case .playing: .filled
            case .ready: .outlined
            case .permissionNeeded, .permissionDenied, .waitingForSignal, .failed: .none
            }
        let image = NSImage(size: size, flipped: true) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            draw(triangle, in: context)
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "plugview"
        return image
    }

    private enum Triangle {
        case none
        case outlined
        case filled
    }

    /// The port spans 50×30 of the kit's 64 units; a little margin keeps its stroke inside the image.
    private static let viewBox = CGRect(x: 5, y: 15, width: 54, height: 34)
    private static let size = NSSize(width: 21, height: 13.2)

    private static func draw(_ triangle: Triangle, in context: CGContext) {
        let scale = size.width / viewBox.width
        context.scaleBy(x: scale, y: scale)
        context.translateBy(x: -viewBox.minX, y: -viewBox.minY)
        context.setStrokeColor(NSColor.black.cgColor)
        context.setFillColor(NSColor.black.cgColor)
        context.setLineJoin(.round)

        context.setLineWidth(4.2)
        context.addPath(BrandGeometry.port.cgPath)
        context.strokePath()

        context.setLineWidth(2.6)
        context.addPath(BrandGeometry.triangle.cgPath)
        switch triangle {
        case .none: context.beginPath()
        case .outlined: context.strokePath()
        case .filled: context.drawPath(using: .fillStroke)
        }
    }
}
