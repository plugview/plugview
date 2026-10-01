import CoreText
import Foundation
import SwiftUI

/// Geist and Geist Mono (SIL Open Font License, see Fonts/OFL.txt), shipped in the PlugviewUI resource bundle.
///
/// A terminal-only install copies just the binary, without the bundle; text then falls back to the system font.
public enum BrandFonts {
    /// Makes the bundled fonts available to this process; call once at launch.
    public static func register() {
        guard let fontsDirectory else { return }
        let fontURLs = (try? FileManager.default.contentsOfDirectory(at: fontsDirectory, includingPropertiesForKeys: nil)) ?? []
        for url in fontURLs where url.pathExtension == "ttf" {
            CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
        }
    }

    static func sans(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .custom(weight == .regular ? "Geist-Regular" : weight == .medium ? "Geist-Medium" : "Geist-SemiBold", fixedSize: size)
    }

    static func mono(_ size: CGFloat, _ weight: Font.Weight = .semibold) -> Font {
        .custom(weight == .medium ? "GeistMono-Medium" : "GeistMono-SemiBold", fixedSize: size)
    }

    /// Looked up by hand: SwiftPM's `Bundle.module` stops the process when the bundle is missing.
    private static var fontsDirectory: URL? {
        let bundleName = "plugview_PlugviewUI.bundle"
        let searchDirectories = [Bundle.main.resourceURL, Bundle.main.executableURL?.deletingLastPathComponent()]
        return searchDirectories.lazy
            .compactMap { $0.flatMap { Bundle(url: $0.appending(path: bundleName)) } }
            .compactMap { $0.url(forResource: "Fonts", withExtension: nil) }
            .first
    }
}
