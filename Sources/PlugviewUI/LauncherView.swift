import PlugviewCore
import SwiftUI

/// Everything the window shows while no video is on screen, styled per the brand kit and the roadmap mockups.
public struct LauncherView: View {
    private let model: LauncherModel

    public init(model: LauncherModel) {
        self.model = model
    }

    public var body: some View {
        ZStack {
            // One identity per screen, so changing screens crossfades while updates within a screen do not.
            content
                .id(screenID)
                .transition(.asymmetric(insertion: .opacity.combined(with: .scale(scale: 0.97)), removal: .opacity))
        }
        .animation(.easeOut(duration: 0.32), value: screenID)
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
            Screen(art: .signal, title: "SIGNAL FOUND", message: readyMessage(for: devices)) {
                ForEach(devices, id: \.uniqueID) { device in
                    if let play = model.play, devices.count > 1 {
                        Button {
                            play(device)
                        } label: {
                            DeviceRow(name: device.name, details: model.readyDetails[device.uniqueID] ?? "", isPickable: true)
                        }
                        .buttonStyle(.plain)
                    } else {
                        DeviceRow(name: device.name, details: model.readyDetails[device.uniqueID] ?? "")
                    }
                }
                if let play = model.play, let first = devices.first, devices.count == 1 || model.isAutoPlayPending {
                    Button {
                        play(first)
                    } label: {
                        Label(devices.count == 1 ? "Play" : "Play \(first.name)", systemImage: "play.fill")
                    }
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

    private var screenID: String {
        switch model.state {
        case .permissionNeeded: model.continueToPermissions == nil ? "permission-prompt" : "onboarding"
        case .permissionDenied(let kind): "denied-\(kind)"
        case .waitingForSignal: "waiting"
        case .ready: "ready"
        case .playing: "playing"
        case .failed: "failed"
        }
    }

    private func readyMessage(for devices: [DeviceInfo]) -> String? {
        guard devices.count > 1, model.play != nil else { return nil }
        return model.isAutoPlayPending ? "Playing the one you used last, or pick another." : "Pick one. plugview remembers your choice."
    }

    private var waitingMessage: String {
        model.waitingTarget == "a capture device"
            ? "Plug a capture card into a USB port.\nYour console will show up here."
            : "Plug in \(model.waitingTarget).\nIt will show up here."
    }

    private let helpURL = Links.troubleshooting
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
    var isPickable = false
    @State private var isHovered = false

    var body: some View {
        HStack(spacing: 12) {
            Circle().fill(Palette.ok).frame(width: 8, height: 8).shadow(color: Palette.ok, radius: 4)
            VStack(alignment: .leading, spacing: 3) {
                Text(name).font(BrandFonts.sans(15, .semibold)).foregroundStyle(Palette.bone)
                if !details.isEmpty { Text(details).font(BrandFonts.mono(12, .medium)).foregroundStyle(Palette.muted) }
            }
            Spacer(minLength: 0)
            if isPickable { Image(systemName: "play.fill").font(.system(size: 12)).foregroundStyle(Palette.signal) }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(width: 340)
        .background(Palette.raised.opacity(isPickable && isHovered ? 0.6 : 1), in: RoundedRectangle(cornerRadius: 10))
        .background(isPickable && isHovered ? Palette.signal.opacity(0.08) : .clear, in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(isPickable && isHovered ? Palette.signal.opacity(0.5) : Palette.line))
        .contentShape(RoundedRectangle(cornerRadius: 10))
        .onHover { isHovered = $0 }
        .animation(.snappy(duration: 0.18), value: isHovered)
    }
}

/// Amber primary button, or an outlined one, in Geist Mono; brightens on hover and sinks while pressed.
struct BrandButtonStyle: ButtonStyle {
    let isPrimary: Bool

    func makeBody(configuration: Configuration) -> some View {
        BrandButton(isPrimary: isPrimary, isPressed: configuration.isPressed) { configuration.label }
    }
}

private struct BrandButton<Label: View>: View {
    let isPrimary: Bool
    let isPressed: Bool
    @ViewBuilder let label: () -> Label
    @State private var isHovered = false

    var body: some View {
        label()
            .font(BrandFonts.mono(13))
            .foregroundStyle(isPrimary ? Palette.cathode : Palette.bone)
            .padding(.horizontal, 18)
            .padding(.vertical, 11)
            .background(isPrimary ? Palette.signal : .white.opacity(isHovered ? 0.06 : 0), in: RoundedRectangle(cornerRadius: 9))
            .brightness(isPrimary && isHovered ? 0.06 : 0)
            .overlay(RoundedRectangle(cornerRadius: 9).stroke(isPrimary ? Color.clear : Palette.line.opacity(isHovered ? 2 : 1)))
            .shadow(color: isPrimary ? Palette.signal.opacity(isHovered ? 0.45 : 0.2) : .clear, radius: isHovered ? 14 : 8)
            .scaleEffect(isPressed ? 0.96 : 1)
            .contentShape(RoundedRectangle(cornerRadius: 9))
            .onHover { isHovered = $0 }
            .animation(.snappy(duration: 0.18), value: isHovered)
            .animation(.snappy(duration: 0.12), value: isPressed)
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
