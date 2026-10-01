import PlugviewCore
import SwiftUI

/// The floating bar over the video (roadmap mockup "Playing"): sound, volume, full screen, and device.
///
/// The window shows it on mouse movement and fades it out when idle, so it never covers the picture for long.
public struct ControlsView: View {
    @Bindable private var model: ControlsModel

    public init(model: ControlsModel) {
        self.model = model
    }

    public var body: some View {
        HStack(spacing: 6) {
            ControlButton(symbol: isSilent ? "speaker.slash.fill" : "speaker.wave.2.fill", label: isSilent ? "Unmute" : "Mute", action: model.toggleMute)
            HStack(spacing: 8) {
                Slider(value: volume, in: 0...100).frame(width: 96).tint(Palette.signal)
                Text("\(model.isMuted ? 0 : model.volumePercent)%").frame(width: 36, alignment: .trailing)
            }
            .controlChrome()
            if model.devices.count > 1 { deviceMenu }
            ControlButton(symbol: "arrow.up.left.and.arrow.down.right", label: "Full Screen", action: model.toggleFullScreen)
        }
        .font(BrandFonts.mono(11))
        .foregroundStyle(Palette.bone)
        .padding(6)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 12))
        .background(Palette.cathode.opacity(0.6), in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(.white.opacity(0.08)))
        .environment(\.colorScheme, .dark)
        .fixedSize()
    }

    private var isSilent: Bool { model.isMuted || model.volumePercent == 0 }

    private var volume: Binding<Double> {
        Binding(get: { Double(model.isMuted ? 0 : model.volumePercent) }, set: { model.setVolume(Int($0.rounded())) })
    }

    private var deviceMenu: some View {
        Menu {
            ForEach(model.devices, id: \.uniqueID) { device in
                Button {
                    model.selectDevice(device)
                } label: {
                    if device.uniqueID == model.playingDeviceID { Label(device.name, systemImage: "checkmark") } else { Text(device.name) }
                }
            }
        } label: {
            Image(systemName: "cable.connector.horizontal")
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .controlChrome()
        .help("Capture device")
        .accessibilityLabel("Capture device")
    }
}

private struct ControlButton: View {
    let symbol: String
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) { Image(systemName: symbol).frame(width: 18, height: 16) }
            .buttonStyle(.plain)
            .controlChrome()
            .help(label)
            .accessibilityLabel(label)
    }
}

extension View {
    /// One rounded cell of the controls bar, as in the mockup's HUD.
    fileprivate func controlChrome() -> some View {
        padding(.horizontal, 9)
            .padding(.vertical, 7)
            .background(.white.opacity(0.06), in: RoundedRectangle(cornerRadius: 7))
            .contentShape(RoundedRectangle(cornerRadius: 7))
    }
}
