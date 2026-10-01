import PlugviewCore
import SwiftUI

/// Everything the window shows while no video is on screen, styled per the brand kit and the roadmap mockups.
public struct LauncherView: View {
    private let model: LauncherModel

    public init(model: LauncherModel) {
        self.model = model
    }

    public var body: some View {
        content
            .multilineTextAlignment(.center)
            .padding(32)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(RadialGradient(colors: [Palette.glow, Palette.surface], center: UnitPoint(x: 0.5, y: 0.4), startRadius: 0, endRadius: 700))
            // A CRT surface: dark in light mode too, so system controls must use dark-mode colors.
            .environment(\.colorScheme, .dark)
    }

    @ViewBuilder private var content: some View {
        switch model.state {
        case .permissionNeeded(let kind):
            if let continueToPermissions = model.continueToPermissions {
                PermissionsOnboarding(onContinue: continueToPermissions)
            } else {
                Screen(art: .mark, title: "WAITING FOR PERMISSION", message: "Allow \(kind.name) access in the macOS prompt.")
            }
        case .permissionDenied(let kind):
            PermissionDenied(kind: kind)
        case .waitingForSignal:
            Screen(art: .waiting, title: "WAITING FOR SIGNAL", message: waitingMessage) {
                Link(destination: helpURL) { Text("Help me connect") }.buttonStyle(BrandButtonStyle(isPrimary: false))
            }
        case .ready(let devices):
            Screen(art: .signal, title: "SIGNAL FOUND", message: nil) {
                DeviceRow(name: devices.first?.name ?? "Capture device", details: model.readyDetails)
                if let playNow = model.playNow {
                    Button(action: playNow) { Label("Play", systemImage: "play.fill") }
                        .buttonStyle(BrandButtonStyle(isPrimary: true))
                        .keyboardShortcut(.defaultAction)
                }
            }
        case .playing:
            EmptyView()
        case .failed(let failure):
            Screen(art: .mark, title: "COULD NOT START", titleColor: Palette.tally, message: "\(failure.deviceName): \(failure.reason)") {
                Link(destination: helpURL) { Text("Help me connect") }.buttonStyle(BrandButtonStyle(isPrimary: false))
            }
        }
    }

    private var waitingMessage: String {
        model.waitingTarget == "a capture device"
            ? "Plug a capture card into a USB port.\nYour console will show up here."
            : "Plug in \(model.waitingTarget).\nIt will show up here."
    }

    private let helpURL = URL(string: "https://github.com/plugview/plugview#troubleshooting") ?? URL(filePath: "/")
}

/// The artwork above a screen's title.
private enum Art {
    case mark
    case waiting
    case signal
}

/// The launcher's one layout: artwork, a mono amber title with a soft glow, a message, then actions.
private struct Screen<Actions: View>: View {
    let art: Art
    let title: String
    var titleColor = Palette.signal
    let message: String?
    @ViewBuilder var actions: () -> Actions

    var body: some View {
        VStack(spacing: 0) {
            artwork
            Text(title)
                .font(BrandFonts.mono(14))
                .tracking(2.8)
                .foregroundStyle(titleColor)
                .shadow(color: titleColor.opacity(0.5), radius: 6)
                .padding(.top, 20)
            if let message {
                Text(message).font(BrandFonts.sans(15)).foregroundStyle(Palette.muted).lineSpacing(3).padding(.top, 8)
            }
            VStack(spacing: 14) { actions() }.padding(.top, 18)
        }
        .frame(maxWidth: 440)
    }

    @ViewBuilder private var artwork: some View {
        switch art {
        case .mark: MarkView().frame(width: 96)
        case .waiting: WaitingAnimation().frame(width: 300)
        case .signal: SignalAnimation().frame(width: 150)
        }
    }
}

extension Screen where Actions == EmptyView {
    init(art: Art, title: String, titleColor: Color = Palette.signal, message: String?) {
        self.init(art: art, title: title, titleColor: titleColor, message: message) { EmptyView() }
    }
}

/// The found card, as in the roadmap's "Signal found" mockup.
private struct DeviceRow: View {
    let name: String
    let details: String

    var body: some View {
        HStack(spacing: 12) {
            Circle().fill(Palette.ok).frame(width: 8, height: 8).shadow(color: Palette.ok, radius: 4)
            VStack(alignment: .leading, spacing: 3) {
                Text(name).font(BrandFonts.sans(15, .semibold)).foregroundStyle(Palette.bone)
                if !details.isEmpty { Text(details).font(BrandFonts.mono(12, .medium)).foregroundStyle(Palette.muted) }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(width: 340)
        .background(Palette.raised, in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.line))
    }
}

/// Amber primary button, or an outlined one, in Geist Mono.
private struct BrandButtonStyle: ButtonStyle {
    let isPrimary: Bool

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(BrandFonts.mono(13))
            .foregroundStyle(isPrimary ? Palette.cathode : Palette.bone)
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(isPrimary ? Palette.signal : Color.clear, in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(isPrimary ? Color.clear : Palette.line))
            .opacity(configuration.isPressed ? 0.75 : 1)
            .contentShape(RoundedRectangle(cornerRadius: 8))
    }
}

/// First run from Finder: says why each permission is needed before macOS asks.
private struct PermissionsOnboarding: View {
    let onContinue: () -> Void

    var body: some View {
        Screen(art: .mark, title: "TWO PERMISSIONS", message: "macOS asks for each one when you continue.") {
            VStack(alignment: .leading, spacing: 14) {
                PermissionReason(
                    symbol: "video", title: "Camera",
                    detail: "Shows the picture from your capture card. plugview never turns on your Mac's camera.")
                PermissionReason(
                    symbol: "speaker.wave.2", title: "Microphone",
                    detail: "Plays the sound from your console. plugview never listens to your Mac's microphone.")
            }
            .padding(16)
            .frame(width: 400)
            .background(Palette.raised, in: RoundedRectangle(cornerRadius: 10))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.line))
            Button("Continue", action: onContinue)
                .buttonStyle(BrandButtonStyle(isPrimary: true))
                .keyboardShortcut(.defaultAction)
        }
    }
}

private struct PermissionReason: View {
    let symbol: String
    let title: String
    let detail: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Image(systemName: symbol).foregroundStyle(Palette.signal).frame(width: 22)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(BrandFonts.sans(15, .semibold)).foregroundStyle(Palette.bone)
                Text(detail).font(BrandFonts.sans(13)).foregroundStyle(Palette.muted).multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// A refused permission plugview cannot play without, with a way to turn it back on.
private struct PermissionDenied: View {
    let kind: PermissionKind
    @Environment(\.openURL) private var openURL

    var body: some View {
        // macOS applies the change only after a restart and offers "Quit & Reopen" itself.
        Screen(
            art: .mark, title: "\(kind.name.uppercased()) ACCESS IS OFF", titleColor: Palette.tally,
            message: "Turn on plugview in System Settings › Privacy & Security › \(kind.name.capitalized), then choose Quit & Reopen."
        ) {
            Button("Open System Settings") {
                if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(kind.settingsAnchor)") { openURL(url) }
            }
            .buttonStyle(BrandButtonStyle(isPrimary: true))
        }
    }
}

extension PermissionKind {
    fileprivate var name: String {
        switch self {
        case .camera: "camera"
        case .microphone: "microphone"
        }
    }

    /// The System Settings › Privacy & Security section for this permission.
    fileprivate var settingsAnchor: String {
        switch self {
        case .camera: "Privacy_Camera"
        case .microphone: "Privacy_Microphone"
        }
    }
}
