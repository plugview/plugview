import PlugviewCore
import SwiftUI

/// The floating bar over the video (roadmap mockup "Playing"): sound, volume, video mode, device, and full screen.
///
/// The window shows it on mouse movement and fades it out when idle, so it never covers the picture for long.
/// Every control is at least `ControlMetrics.height` tall, so it is easy to hit without aiming.
public struct ControlsView: View {
    @Bindable private var model: ControlsModel

    public init(model: ControlsModel) {
        self.model = model
    }

    public var body: some View {
        HStack(spacing: 4) {
            Button(action: model.toggleMute) {
                Image(systemName: speakerSymbol).contentTransition(.symbolEffect(.replace)).frame(width: 18)
            }
            .buttonStyle(ControlButtonStyle())
            .help(isSilent ? "Unmute (M)" : "Mute (M)")
            .accessibilityLabel(isSilent ? "Unmute" : "Mute")

            HStack(spacing: 10) {
                VolumeTrack(percent: model.effectiveVolumePercent, onChange: model.setVolume)
                Text("\(model.effectiveVolumePercent)%").monospacedDigit().frame(width: 34, alignment: .trailing)
            }
            .padding(.leading, 6)
            .padding(.trailing, 10)

            if !model.modes.isEmpty { modeMenu }
            if model.devices.count > 1 { deviceMenu }

            Button(action: model.toggleFullScreen) {
                Image(systemName: "arrow.up.left.and.arrow.down.right").frame(width: 18)
            }
            .buttonStyle(ControlButtonStyle())
            .help("Full Screen (F)")
            .accessibilityLabel("Full Screen")
        }
        .font(BrandFonts.mono(12))
        .foregroundStyle(Palette.bone)
        .padding(5)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 14))
        .background(Palette.cathode.opacity(0.6), in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(.white.opacity(0.1)))
        .shadow(color: .black.opacity(0.35), radius: 16, y: 6)
        .environment(\.colorScheme, .dark)
        .fixedSize()
    }

    private var isSilent: Bool { model.effectiveVolumePercent == 0 }

    private var speakerSymbol: String {
        switch model.effectiveVolumePercent {
        case 0: "speaker.slash.fill"
        case 1..<34: "speaker.wave.1.fill"
        case 34..<67: "speaker.wave.2.fill"
        default: "speaker.wave.3.fill"
        }
    }

    private var modeMenu: some View {
        Button {
            model.isPickerOpen.toggle()
        } label: {
            Text(model.activeMode?.shortTitle ?? "Auto")
        }
        .buttonStyle(ControlButtonStyle())
        .popover(isPresented: $model.isPickerOpen, arrowEdge: .top) {
            ModePicker(model: model) { model.isPickerOpen = false }
        }
        .help("Resolution and frame rate")
        .accessibilityLabel("Resolution and frame rate")
    }

    private var deviceMenu: some View {
        Menu {
            DeviceMenuItems(model: model)
        } label: {
            Image(systemName: "cable.connector.horizontal").frame(width: 18)
        }
        .menuStyle(.button)
        .buttonStyle(ControlButtonStyle())
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Capture device")
        .accessibilityLabel("Capture device")
    }
}

/// Every playable device, with a check on the one playing.
struct DeviceMenuItems: View {
    let model: ControlsModel

    var body: some View {
        ForEach(model.devices, id: \.uniqueID) { device in
            Button {
                model.selectDevice(device)
            } label: {
                checkedLabel(device.name, isChecked: device.uniqueID == model.playingDeviceID)
            }
        }
    }
}

@ViewBuilder func checkedLabel(_ title: String, isChecked: Bool) -> some View {
    if isChecked { Label(title, systemImage: "checkmark") } else { Text(title) }
}

enum ControlMetrics {
    /// The height of every control in the bar: a comfortable pointer target.
    static let height: CGFloat = 32
}

/// One rounded cell of the controls bar: lights up on hover and sinks a little while pressed.
struct ControlButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        ControlCell(isPressed: configuration.isPressed) { configuration.label }
    }
}

private struct ControlCell<Content: View>: View {
    let isPressed: Bool
    @ViewBuilder let content: () -> Content
    @State private var isHovered = false

    var body: some View {
        content()
            .padding(.horizontal, 10)
            .frame(minWidth: ControlMetrics.height, minHeight: ControlMetrics.height)
            .background(.white.opacity(isPressed ? 0.2 : isHovered ? 0.13 : 0.06), in: RoundedRectangle(cornerRadius: 9))
            .contentShape(RoundedRectangle(cornerRadius: 9))
            .scaleEffect(isPressed ? 0.94 : 1)
            .onHover { isHovered = $0 }
            .animation(.snappy(duration: 0.16), value: isHovered)
            .animation(.snappy(duration: 0.12), value: isPressed)
    }
}

/// The volume: click or drag anywhere on the full-height strip, not just on the thumb.
private struct VolumeTrack: View {
    let percent: Int
    let onChange: (Int) -> Void
    @State private var isHovered = false
    @State private var isDragging = false

    private static let width: CGFloat = 112

    var body: some View {
        let isActive = isHovered || isDragging
        let knob: CGFloat = isActive ? 14 : 11
        let fraction = CGFloat(percent) / 100
        ZStack(alignment: .leading) {
            Capsule().fill(.white.opacity(0.16)).frame(height: isActive ? 5 : 4)
            Capsule().fill(Palette.signal).frame(width: max(knob / 2, Self.width * fraction), height: isActive ? 5 : 4)
            Circle()
                .fill(Palette.bone)
                .frame(width: knob, height: knob)
                .shadow(color: .black.opacity(0.4), radius: 2, y: 1)
                .offset(x: fraction * (Self.width - knob))
        }
        .frame(width: Self.width, height: ControlMetrics.height)
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 0)
                .onChanged { drag in
                    isDragging = true
                    onChange(Int((drag.location.x / Self.width * 100).rounded()).clamped(to: 0...100))
                }
                .onEnded { _ in isDragging = false }
        )
        .onHover { isHovered = $0 }
        .animation(.snappy(duration: 0.18), value: isActive)
        .accessibilityElement()
        .accessibilityLabel("Volume")
        .accessibilityValue("\(percent)%")
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: onChange(min(100, percent + Tuning.volumeStepPercent))
            case .decrement: onChange(max(0, percent - Tuning.volumeStepPercent))
            @unknown default: break
            }
        }
    }
}

extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self { min(max(self, range.lowerBound), range.upperBound) }
}
