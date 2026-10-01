import SwiftUI

/// Brand colors from the plugview brand kit v1.0.
///
/// The launcher is a CRT surface, so it stays dark in light mode too.
enum Palette {
    static let cathode = Color(hex: 0x0A0A0C)
    static let raised = Color(hex: 0x17171B)
    static let line = Color(hex: 0x2A2A31)
    static let bone = Color(hex: 0xEDE8DD)
    static let muted = Color(hex: 0x8C887F)
    static let signal = Color(hex: 0xFFB000)
    static let tally = Color(hex: 0xFF3B2F)
    static let ok = Color(hex: 0x7BE0A0)
    /// The warm center of the launcher's radial glow.
    static let glow = Color(hex: 0x1B1912)
    static let surface = Color(hex: 0x0C0C0E)

    /// SMPTE color bars, left to right, as drawn inside the mark's play triangle.
    static let bars: [Color] = [0xD8D8D8, 0xE3C200, 0x00B8C4, 0x1FB24A, 0xB23AB8, 0xD2342A, 0x2B3FD0].map(Color.init(hex:))
}

extension Color {
    init(hex: UInt32) {
        self.init(
            .sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }
}
