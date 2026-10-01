import Foundation
import PlugviewCore
import SwiftUI
import Testing

@testable import PlugviewUI

/// Renders every launcher state to PNG for visual review against the roadmap mockups.
///
/// Runs only when `PLUGVIEW_RENDER_DIR` names an output folder, e.g.
/// `PLUGVIEW_RENDER_DIR=/tmp/launcher swift test --filter LauncherSnapshots`.
@MainActor
@Suite struct LauncherSnapshots {
    nonisolated static let outputDirectory = ProcessInfo.processInfo.environment["PLUGVIEW_RENDER_DIR"].map { URL(filePath: $0) }

    @Test(.enabled(if: outputDirectory != nil)) func renderEveryState() throws {
        let outputDirectory = try #require(Self.outputDirectory)
        BrandFonts.register()
        let card = DeviceInfo(uniqueID: "card", name: "UGREEN 25773", modelID: "", transport: .usb)
        let states: [(String, LauncherState, (LauncherModel) -> Void)] = [
            ("1-onboarding", .permissionNeeded(.camera), { $0.continueToPermissions = {} }),
            ("2-permission-prompt", .permissionNeeded(.camera), { _ in }),
            ("3-camera-denied", .permissionDenied(.camera), { _ in }),
            ("4-waiting", .waitingForSignal, { _ in }),
            (
                "5-ready", .ready([card]),
                {
                    $0.readyDetails = "1920×1080 · 60 fps · audio on"; $0.playNow = {}
                }
            ),
            ("6-failed", .failed(CaptureFailure(deviceName: "UGREEN 25773", reason: "cannot be added (is another app using it?)")), { _ in }),
        ]
        for (name, state, configure) in states {
            let model = LauncherModel()
            model.state = state
            configure(model)
            let renderer = ImageRenderer(content: LauncherView(model: model).frame(width: 1280, height: 720))
            let image = try #require(renderer.nsImage?.tiffRepresentation.flatMap(NSBitmapImageRep.init(data:)))
            try #require(image.representation(using: .png, properties: [:])).write(to: outputDirectory.appending(path: "\(name).png"))
        }
    }
}
