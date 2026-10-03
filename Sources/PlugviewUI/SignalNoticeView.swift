import SwiftUI

/// What the signal watch tells the user over the video (`spec/README.md`, "Signal watch and picture help").
public enum SignalNotice: Equatable, Sendable {
    /// Frames stopped while the card is connected; plugview is reopening it. `showsHelp` after a few tries.
    case noSignal(deviceName: String, showsHelp: Bool)
    /// Black frames since the start: usually HDCP.
    case blackPicture
    /// Digital silence since the start, with a picture: the console's audio format, or a headset on the controller.
    case noGameSound
}

/// The console checklists, worded as in the spec so every platform says the same.
enum SignalHelp {
    static let picture = [
        "PlayStation 5: Settings › System › HDMI › turn off Enable HDCP. PlayStation 4: Settings › System › turn off Enable HDCP.",
        "Set the console to 1080p, and turn off 120 Hz and VRR output.",
        "Nintendo Switch: it must sit in its dock. Switch Lite has no video output.",
        "Unplug the HDMI cable from the card and plug it back in.",
    ]
    static let sound = [
        "PlayStation 5: Settings › Sound › Audio Output › Audio Format (Priority) › Linear PCM.",
        "Xbox: Settings › General › Volume & audio output › HDMI audio › Stereo uncompressed.",
        "Nintendo Switch 2: set TV sound to Stereo.",
        "Unplug headphones from the controller or the console: game sound then goes back to HDMI.",
    ]
}

/// The notice over the video: "No signal" covers the picture (never a frozen frame); the hints are a dismissible panel.
public struct SignalNoticeView: View {
    @Bindable private var model: ControlsModel

    public init(model: ControlsModel) {
        self.model = model
    }

    public var body: some View {
        ZStack {
            if let notice = model.signalNotice {
                content(for: notice)
                    .transition(.opacity)
            }
        }
        .animation(.easeOut(duration: 0.25), value: model.signalNotice)
        .environment(\.colorScheme, .dark)
    }

    @ViewBuilder private func content(for notice: SignalNotice) -> some View {
        switch notice {
        case .noSignal(let deviceName, let showsHelp):
            ZStack {
                Palette.cathode.opacity(0.92)
                panel(title: "NO SIGNAL", message: "\(deviceName) stopped sending a picture. Reconnecting…", checklist: showsHelp ? SignalHelp.picture : [])
            }
        case .blackPicture:
            panel(
                title: "BLACK PICTURE", message: "The card sends a black picture. Usually the console's HDCP protection:", checklist: SignalHelp.picture,
                isDismissible: true)
        case .noGameSound:
            panel(
                title: "NO GAME SOUND", message: "The card sends silence. The console may be sending surround or Dolby sound:", checklist: SignalHelp.sound,
                isDismissible: true)
        }
    }

    private func panel(title: String, message: String, checklist: [String], isDismissible: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(BrandFonts.mono(14)).tracking(1.5).foregroundStyle(Palette.signal)
            Text(message).font(BrandFonts.sans(15)).foregroundStyle(Palette.bone).fixedSize(horizontal: false, vertical: true)
            ForEach(checklist, id: \.self) { line in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Circle().fill(Palette.signal).frame(width: 5, height: 5).alignmentGuide(.firstTextBaseline) { $0[.bottom] + 4 }
                    Text(line).font(BrandFonts.sans(13)).foregroundStyle(Palette.muted).fixedSize(horizontal: false, vertical: true)
                }
            }
            if isDismissible {
                Button("Close") { model.signalNotice = nil }
                    .buttonStyle(BrandButtonStyle(isPrimary: false))
                    .keyboardShortcut(.cancelAction)
                    .frame(maxWidth: .infinity, alignment: .trailing)
            }
        }
        .padding(22)
        .frame(maxWidth: 520)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 14))
        .background(Palette.cathode.opacity(0.7), in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(.white.opacity(0.1)))
        .padding(24)
    }
}
