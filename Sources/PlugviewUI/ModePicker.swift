import PlugviewCore
import SwiftUI

/// The resolution and frame rate panel: Automatic on top, widescreen modes as named rows, other shapes folded away.
///
/// Opens from the controls bar's mode chip and the menu bar panel; picking closes it.
struct ModePicker: View {
    let model: ControlsModel
    let onPick: () -> Void
    @State private var isShowingOtherSizes = false

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("RESOLUTION")
                .font(BrandFonts.mono(10, .medium)).tracking(1.6).foregroundStyle(Palette.muted)
                .padding(.horizontal, 8).padding(.bottom, 4)
            ModeRow(
                title: "Automatic", detail: model.modeChoice == nil ? model.activeMode.map(\.title) : "The smoothest mode the card offers",
                badge: nil, isSelected: model.modeChoice == nil
            ) { pick(nil) }
            Divider().overlay(Palette.line).padding(.vertical, 4)
            ForEach(widescreenModes, id: \.self) { row(for: $0) }
            if !otherModes.isEmpty {
                Button {
                    withAnimation(.snappy(duration: 0.2)) { isShowingOtherSizes.toggle() }
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "chevron.right").font(.system(size: 9, weight: .semibold))
                            .rotationEffect(.degrees(isShowingOtherSizes ? 90 : 0))
                        Text("Other sizes (\(otherModes.count))")
                        Spacer()
                    }
                    .font(BrandFonts.mono(11)).foregroundStyle(Palette.muted)
                    .padding(.horizontal, 8).frame(height: 28).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                if isShowingOtherSizes {
                    ForEach(otherModes, id: \.self) { row(for: $0) }
                }
            }
        }
        .padding(10)
        .frame(width: 280)
        .foregroundStyle(Palette.bone)
        .background(Palette.surface)
        .environment(\.colorScheme, .dark)
        .onAppear { isShowingOtherSizes = model.modeChoice.map { !$0.isWidescreen } ?? false }
    }

    private var widescreenModes: [ModeChoice] { model.modes.filter(\.isWidescreen) }
    private var otherModes: [ModeChoice] { model.modes.filter { !$0.isWidescreen } }

    private func row(for mode: ModeChoice) -> some View {
        ModeRow(title: mode.name, detail: "\(mode.width)×\(mode.height)", badge: mode, isSelected: model.modeChoice == mode) { pick(mode) }
    }

    private func pick(_ mode: ModeChoice?) {
        onPick()
        if mode != model.modeChoice { model.selectMode(mode) }
    }
}

private struct ModeRow: View {
    let title: String
    let detail: String?
    let badge: ModeChoice?
    let isSelected: Bool
    let action: () -> Void
    @State private var isHovered = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(BrandFonts.sans(13, .semibold))
                    if let detail { Text(detail).font(BrandFonts.mono(10)).foregroundStyle(Palette.muted) }
                }
                Spacer(minLength: 8)
                if let badge {
                    let isSmooth = Double(badge.frameRate) >= Tuning.smoothFrameRate
                    Text("\(badge.frameRate) fps")
                        .font(BrandFonts.mono(10, .medium))
                        .foregroundStyle(isSmooth ? Palette.signal : Palette.muted)
                        .padding(.horizontal, 6).padding(.vertical, 3)
                        .background((isSmooth ? Palette.signal : Palette.muted).opacity(0.12), in: RoundedRectangle(cornerRadius: 5))
                }
                Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(Palette.signal)
                    .opacity(isSelected ? 1 : 0).frame(width: 14)
            }
            .padding(.horizontal, 8)
            .frame(minHeight: 40)
            .background(isSelected ? Palette.signal.opacity(0.1) : .white.opacity(isHovered ? 0.07 : 0), in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).stroke(isSelected ? Palette.signal.opacity(0.45) : .clear))
            .contentShape(RoundedRectangle(cornerRadius: 8))
        }
        .buttonStyle(.plain)
        .onHover { isHovered = $0 }
        .animation(.snappy(duration: 0.15), value: isHovered)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

extension ModeChoice {
    /// 16:9 (within rounding), the shape consoles send; 4:3 and 5:4 sizes are folded under "Other sizes".
    var isWidescreen: Bool { abs(width * 9 - height * 16) <= width / 50 }

    /// e.g. "1080p Full HD"; the vertical size with its common name when it has one.
    var name: String {
        let label: String? =
            switch height {
            case 2160: "4K"
            case 1440: "QHD"
            case 1080: "Full HD"
            case 720: "HD"
            case 480, 576: "SD"
            default: nil
            }
        return label.map { "\(height)p \($0)" } ?? "\(height)p"
    }
}
