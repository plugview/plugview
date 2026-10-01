import PlugviewCore
import SwiftUI

/// Everything the window shows while no video is on screen.
///
/// Plain for now; styled in Phase 1.4.
public struct LauncherView: View {
    private let model: LauncherModel

    public init(model: LauncherModel) {
        self.model = model
    }

    public var body: some View {
        content
            .multilineTextAlignment(.center)
            .padding(24)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            // The video behind is always black, so the launcher keeps dark-mode colors in light mode too.
            .environment(\.colorScheme, .dark)
    }

    @ViewBuilder private var content: some View {
        switch model.state {
        case .permissionNeeded(let kind):
            if let continueToPermissions = model.continueToPermissions {
                PermissionsOnboarding(onContinue: continueToPermissions)
            } else {
                StatusText(title: "Waiting for \(kind.name) permission…", detail: "Allow it in the macOS prompt if one appears.")
            }
        case .permissionDenied(let kind):
            PermissionDenied(kind: kind)
        case .waitingForSignal:
            StatusText(title: "Waiting for \(model.waitingTarget)…", detail: "Plug it in and it will appear here.")
        case .ready(let devices):
            StatusText(title: "Starting \(devices.first?.name ?? "the capture device")…", detail: nil)
        case .playing:
            EmptyView()
        case .failed(let failure):
            StatusText(title: "Could not start \(failure.deviceName).", detail: failure.reason)
        }
    }
}

private struct StatusText: View {
    let title: String
    let detail: String?

    var body: some View {
        VStack(spacing: 6) {
            Text(title).font(.system(size: 18, weight: .medium))
            if let detail { Text(detail).foregroundStyle(.secondary) }
        }
    }
}

/// First run from Finder: says why each permission is needed before macOS asks.
private struct PermissionsOnboarding: View {
    let onContinue: () -> Void

    var body: some View {
        VStack(spacing: 20) {
            Text("plugview needs two permissions").font(.system(size: 20, weight: .semibold))
            VStack(alignment: .leading, spacing: 14) {
                PermissionReason(
                    symbol: "video", title: "Camera",
                    detail: "To show the picture from your capture card. plugview never turns on your Mac's camera.")
                PermissionReason(
                    symbol: "speaker.wave.2", title: "Microphone",
                    detail: "To play the sound from your console. plugview never listens to your Mac's microphone.")
            }
            .frame(maxWidth: 420)
            Button("Continue", action: onContinue)
                .keyboardShortcut(.defaultAction)
                .controlSize(.large)
            Text("macOS asks for each one next.").font(.callout).foregroundStyle(.secondary)
        }
    }
}

private struct PermissionReason: View {
    let symbol: String
    let title: String
    let detail: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Image(systemName: symbol).frame(width: 24)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline)
                Text(detail).foregroundStyle(.secondary).multilineTextAlignment(.leading)
            }
        }
    }
}

/// A refused permission plugview cannot play without, with a way to turn it back on.
private struct PermissionDenied: View {
    let kind: PermissionKind
    @Environment(\.openURL) private var openURL

    var body: some View {
        VStack(spacing: 16) {
            StatusText(
                title: "\(kind.name.capitalized) access is off",
                // macOS applies the change only after a restart and offers "Quit & Reopen" itself.
                detail: "Turn on plugview in System Settings › Privacy & Security › \(kind.name.capitalized), then choose Quit & Reopen.")
            Button("Open System Settings") {
                if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(kind.settingsAnchor)") { openURL(url) }
            }
            .controlSize(.large)
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
