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
                    $0.readyDetails = ["card": "1920×1080 · 60 fps · audio on"]
                    $0.play = { _ in }
                }
            ),
            (
                "5b-pick", .ready([card, DeviceInfo(uniqueID: "camlink", name: "Cam Link 4K", modelID: "", transport: .usb)]),
                {
                    $0.readyDetails = ["card": "1920×1080 · 60 fps · audio on", "camlink": "3840×2160 · 30 fps · no audio"]
                    $0.play = { _ in }
                }
            ),
            ("6-failed", .failed(CaptureFailure(deviceName: "UGREEN 25773", reason: "cannot be added (is another app using it?)")), { _ in }),
        ]
        for (name, state, configure) in states {
            let model = LauncherModel()
            model.state = state
            configure(model)
            try render(LauncherView(model: model).frame(width: 1280, height: 720), to: outputDirectory.appending(path: "\(name).png"))
        }

        let controls = ControlsModel()
        controls.volumePercent = 36
        controls.devices = [card, card]
        controls.modes = [ModeChoice(width: 1920, height: 1080, frameRate: 60), ModeChoice(width: 1280, height: 720, frameRate: 60)]
        controls.activeMode = controls.modes.first
        try render(ControlsView(model: controls).padding(20).background(Color.gray), to: outputDirectory.appending(path: "7-controls.png"))

        let cardModes = [(2560, 1440, 30), (1920, 1080, 60), (1600, 1200, 60), (1280, 1024, 60), (1280, 720, 60), (720, 480, 60)]
        controls.modes = cardModes.map { ModeChoice(width: $0.0, height: $0.1, frameRate: $0.2) }
        controls.modeChoice = controls.modes[4]
        try render(ModePicker(model: controls) {}, to: outputDirectory.appending(path: "9-mode-picker.png"))

        let launcher = LauncherModel()
        launcher.state = .playing(card)
        let menuBar = MenuBarModel(version: "0.3.0")
        menuBar.playingDetails = "1920×1080 @ 60 fps · audio from UGREEN 25773"
        menuBar.canLaunchAtLogin = true
        menuBar.updateStatus = .available(version: "0.3.1", url: URL(filePath: "/"), canInstall: true)
        try render(MenuBarView(launcher: launcher, controls: controls, menuBar: menuBar), to: outputDirectory.appending(path: "8-menu-bar.png"))

        let notices: [(String, SignalNotice)] = [
            ("10-no-signal", .noSignal(deviceName: "UGREEN 25773", showsHelp: false)),
            ("10b-no-signal-help", .noSignal(deviceName: "UGREEN 25773", showsHelp: true)),
            ("11-black-picture", .blackPicture),
            ("12-no-game-sound", .noGameSound),
        ]
        for (name, notice) in notices {
            controls.signalNotice = notice
            try render(
                SignalNoticeView(model: controls).frame(width: 1280, height: 720).background(Color(white: 0.35)),
                to: outputDirectory.appending(path: "\(name).png"))
        }
    }

    private func render(_ view: some View, to url: URL) throws {
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        let image = try #require(renderer.nsImage?.tiffRepresentation.flatMap(NSBitmapImageRep.init(data:)))
        try #require(image.representation(using: .png, properties: [:])).write(to: url)
    }
}
