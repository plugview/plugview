import SwiftUI

/// Brand colors from the plugview brand kit v1.0.
///
/// The launcher is a CRT surface, so it stays dark in light mode too.
enum Palette {
    static let cathode = Color(hex: BrandColors.cathode)
    static let raised = Color(hex: BrandColors.raised)
    static let line = Color(hex: BrandColors.line)
    static let bone = Color(hex: BrandColors.bone)
    static let muted = Color(hex: BrandColors.muted)
    static let signal = Color(hex: BrandColors.signal)
    static let tally = Color(hex: BrandColors.tally)
    static let ok = Color(hex: BrandColors.ok)
    /// The warm center of the launcher's radial glow.
    static let glow = Color(hex: BrandColors.glow)
    static let surface = Color(hex: BrandColors.surface)

    /// SMPTE color bars, left to right, as drawn inside the mark's play triangle.
    static let bars: [Color] = BrandColors.bars.map(Color.init(hex:))
}

/// The brand colors as RGB values, checked against `spec/fixtures/brand.json` so every platform uses the same ones.
enum BrandColors {
    static let cathode: UInt32 = 0x0A0A0C
    static let surface: UInt32 = 0x0C0C0E
    static let raised: UInt32 = 0x17171B
    static let line: UInt32 = 0x2A2A31
    static let bone: UInt32 = 0xEDE8DD
    static let muted: UInt32 = 0x8C887F
    static let signal: UInt32 = 0xFFB000
    static let tally: UInt32 = 0xFF3B2F
    static let ok: UInt32 = 0x7BE0A0
    static let glow: UInt32 = 0x1B1912
    static let bars: [UInt32] = [0xD8D8D8, 0xE3C200, 0x00B8C4, 0x1FB24A, 0xB23AB8, 0xD2342A, 0x2B3FD0]
}

extension Color {
    init(hex: UInt32) {
        self.init(
            .sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }
}
