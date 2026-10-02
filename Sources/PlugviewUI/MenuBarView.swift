import PlugviewCore
import SwiftUI

/// The panel under the menu bar icon: what plugview is doing, the video choices, settings, and updates.
public struct MenuBarView: View {
    private let launcher: LauncherModel
    private let controls: ControlsModel
    private let menuBar: MenuBarModel

    public init(launcher: LauncherModel, controls: ControlsModel, menuBar: MenuBarModel) {
        self.launcher = launcher
        self.controls = controls
        self.menuBar = menuBar
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            StatusCard(launcher: launcher, menuBar: menuBar)
            if !controls.modes.isEmpty || controls.devices.count > 1 { videoSection }
            generalSection
            updatesSection
            footer
        }
        .padding(16)
        .frame(width: 320)
        .font(BrandFonts.sans(13))
        .foregroundStyle(Palette.bone)
        .background(Palette.surface)
        .environment(\.colorScheme, .dark)
    }

    private var header: some View {
        HStack(spacing: 10) {
            MarkView().frame(width: 26)
            Text("plugview").font(BrandFonts.mono(15, .medium))
            Spacer()
            Text(menuBar.version).font(BrandFonts.mono(11)).foregroundStyle(Palette.muted)
        }
    }

    private var videoSection: some View {
        Section(title: "VIDEO") {
            if controls.devices.count > 1 {
                Row(title: "Device") {
                    PanelMenu(title: currentDeviceName) { DeviceMenuItems(model: controls) }
                }
            }
            if !controls.modes.isEmpty {
                Row(title: "Resolution") {
                    ModePickerButton(controls: controls)
                }
            }
        }
    }

    private var currentDeviceName: String {
        controls.devices.first { $0.uniqueID == controls.playingDeviceID }?.name ?? controls.devices.first?.name ?? "None"
    }

    private var generalSection: some View {
        Section(title: "GENERAL") {
            SettingToggle(
                title: "Open when a card is plugged in", isOn: menuBar.opensOnPlugIn, onChange: menuBar.setOpensOnPlugIn,
                help: "Needs plugview in the menu bar: keep it running, or launch it at login.")
            SettingToggle(
                title: "Stay in the menu bar when closed", isOn: menuBar.keepsRunningInMenuBar, onChange: menuBar.setKeepsRunningInMenuBar,
                help: "Closing the window releases the card; plugview keeps watching for it here.")
            if menuBar.canLaunchAtLogin {
                SettingToggle(title: "Launch at login", isOn: menuBar.launchesAtLogin, onChange: menuBar.setLaunchesAtLogin, help: nil)
                if let note = menuBar.launchAtLoginNote {
                    Text(note).font(BrandFonts.sans(11)).foregroundStyle(Palette.signal).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private var updatesSection: some View {
        Section(title: "UPDATES") {
            SettingToggle(title: "Check for updates daily", isOn: menuBar.checksForUpdates, onChange: menuBar.setChecksForUpdates, help: nil)
            HStack {
                updateText
                Spacer()
                updateAction
            }
        }
    }

    @ViewBuilder private var updateText: some View {
        switch menuBar.updateStatus {
        case .notChecked: Text("Not checked yet").foregroundStyle(Palette.muted)
        case .checking: Text("Checking…").foregroundStyle(Palette.muted)
        case .upToDate: Label("Up to date", systemImage: "checkmark.circle").foregroundStyle(Palette.ok)
        case .available(let version, _): Text("\(version) is available").foregroundStyle(Palette.signal)
        case .failed: Text("Could not check").foregroundStyle(Palette.tally)
        }
    }

    @ViewBuilder private var updateAction: some View {
        switch menuBar.updateStatus {
        case .available(_, let url):
            Link(destination: url) { Text("Download") }.buttonStyle(PanelButtonStyle(isPrimary: true))
        case .checking:
            ProgressView().controlSize(.small)
        case .notChecked, .upToDate, .failed:
            Button("Check now", action: menuBar.checkForUpdates).buttonStyle(PanelButtonStyle(isPrimary: false))
        }
    }

    private var footer: some View {
        HStack(spacing: 6) {
            Link(destination: Links.guide) { Label("Guide", systemImage: "book") }
            Link(destination: Links.issues) { Label("Report a problem", systemImage: "exclamationmark.bubble") }
            Spacer()
            Button("Quit", action: menuBar.quit).keyboardShortcut("q")
        }
        .buttonStyle(PanelButtonStyle(isPrimary: false))
        .labelStyle(.titleOnly)
        .padding(.top, 2)
    }
}

enum Links {
    static let guide = URL(string: "https://plugview.app/guide/") ?? URL(filePath: "/")
    static let troubleshooting = URL(string: "https://plugview.app/guide/#troubleshooting") ?? URL(filePath: "/")
    static let issues = URL(string: "https://github.com/plugview/plugview/issues") ?? URL(filePath: "/")
}

/// What plugview is doing right now, with the one action that makes sense next.
private struct StatusCard: View {
    let launcher: LauncherModel
    let menuBar: MenuBarModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Circle().fill(status.color).frame(width: 8, height: 8).shadow(color: status.color, radius: 4)
                Text(status.title).font(BrandFonts.mono(11, .medium)).tracking(1.6).foregroundStyle(status.color)
            }
            if let name = status.name { Text(name).font(BrandFonts.sans(15, .semibold)) }
            if let detail = status.detail {
                Text(detail).font(BrandFonts.mono(11)).foregroundStyle(Palette.muted).fixedSize(horizontal: false, vertical: true)
            }
            Button(action: menuBar.openWindow) {
                Label(actionTitle, systemImage: isPlayAction ? "play.fill" : "macwindow").frame(maxWidth: .infinity)
            }
            .buttonStyle(PanelButtonStyle(isPrimary: isPlayAction))
            .padding(.top, 6)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Palette.raised, in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Palette.line))
        .animation(.easeOut(duration: 0.2), value: launcher.state)
    }

    private var isPlayAction: Bool {
        if case .ready = launcher.state { return !menuBar.isWindowOpen }
        return false
    }

    private var actionTitle: String {
        if isPlayAction { return "Play" }
        return menuBar.isWindowOpen ? "Show Window" : "Open Window"
    }

    private var status: (title: String, color: Color, name: String?, detail: String?) {
        switch launcher.state {
        case .permissionNeeded: ("SETUP", Palette.signal, nil, "Open the window to allow camera and microphone access.")
        case .permissionDenied: ("CAMERA ACCESS IS OFF", Palette.tally, nil, "Turn it on in System Settings › Privacy & Security › Camera.")
        case .waitingForSignal: ("WAITING FOR SIGNAL", Palette.muted, nil, "Plug a capture card into a USB port.")
        case .ready(let devices): ("SIGNAL FOUND", Palette.ok, devices.first?.name, devices.first.flatMap { launcher.readyDetails[$0.uniqueID] })
        case .playing(let device): ("LIVE", Palette.tally, device.name, menuBar.playingDetails)
        case .failed(let failure): ("COULD NOT START", Palette.tally, failure.deviceName, failure.reason)
        }
    }
}

private struct Section<Content: View>: View {
    let title: String
    @ViewBuilder let content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(BrandFonts.mono(10, .medium)).tracking(1.6).foregroundStyle(Palette.muted)
            content()
        }
    }
}

private struct Row<Control: View>: View {
    let title: String
    @ViewBuilder let control: () -> Control

    var body: some View {
        HStack {
            Text(title)
            Spacer()
            control()
        }
    }
}

/// A pop-up menu drawn like the panel's buttons, with a chevron, instead of the system's accent-colored one.
private struct PanelMenu<Items: View>: View {
    let title: String
    @ViewBuilder let items: () -> Items

    var body: some View {
        Menu {
            items()
        } label: {
            HStack(spacing: 6) {
                Text(title).lineLimit(1)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 9, weight: .semibold)).foregroundStyle(Palette.muted)
            }
        }
        .menuStyle(.button)
        .buttonStyle(PanelButtonStyle(isPrimary: false))
        .menuIndicator(.hidden)
        .fixedSize()
    }
}

/// The resolution row's button; opens the same panel as the controls bar.
private struct ModePickerButton: View {
    let controls: ControlsModel
    @State private var isOpen = false

    var body: some View {
        Button {
            isOpen.toggle()
        } label: {
            HStack(spacing: 6) {
                Text(controls.modeChoice?.title ?? "Automatic").lineLimit(1)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 9, weight: .semibold)).foregroundStyle(Palette.muted)
            }
        }
        .buttonStyle(PanelButtonStyle(isPrimary: false))
        .popover(isPresented: $isOpen, arrowEdge: .trailing) { ModePicker(model: controls) { isOpen = false } }
    }
}

private struct SettingToggle: View {
    let title: String
    let isOn: Bool
    let onChange: (Bool) -> Void
    let help: String?

    var body: some View {
        Toggle(isOn: Binding(get: { isOn }, set: { onChange($0) })) {
            Text(title).frame(maxWidth: .infinity, alignment: .leading)
        }
        .toggleStyle(.switch)
        .controlSize(.mini)
        .tint(Palette.signal)
        .help(help ?? "")
    }
}

/// The panel's compact buttons: amber for the main action, quiet otherwise; both light up on hover.
private struct PanelButtonStyle: ButtonStyle {
    let isPrimary: Bool

    func makeBody(configuration: Configuration) -> some View {
        PanelButton(isPrimary: isPrimary, isPressed: configuration.isPressed) { configuration.label }
    }
}

private struct PanelButton<Label: View>: View {
    let isPrimary: Bool
    let isPressed: Bool
    @ViewBuilder let label: () -> Label
    @State private var isHovered = false

    var body: some View {
        label()
            .font(BrandFonts.mono(12))
            .foregroundStyle(isPrimary ? Palette.cathode : Palette.bone)
            .padding(.horizontal, 10)
            .frame(minHeight: 28)
            .background(isPrimary ? Palette.signal : .white.opacity(isHovered ? 0.12 : 0.06), in: RoundedRectangle(cornerRadius: 7))
            .brightness(isPrimary && isHovered ? 0.06 : 0)
            .scaleEffect(isPressed ? 0.96 : 1)
            .contentShape(RoundedRectangle(cornerRadius: 7))
            .onHover { isHovered = $0 }
            .animation(.snappy(duration: 0.16), value: isHovered)
            .animation(.snappy(duration: 0.12), value: isPressed)
    }
}
