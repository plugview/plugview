import SwiftUI

/// The message at the top of the video: slides down, stays a few seconds (longer with an action), and slides away.
public struct ToastView: View {
    @Bindable private var model: ControlsModel

    public init(model: ControlsModel) {
        self.model = model
    }

    public var body: some View {
        ZStack {
            if let toast = model.toast {
                content(for: toast)
                    .id(toast.id)
                    .transition(.move(edge: .top).combined(with: .opacity))
                    .task(id: toast.id) {
                        try? await Task.sleep(for: .seconds(toast.action == nil ? 2.5 : 6))
                        if model.toast?.id == toast.id { model.toast = nil }
                    }
            }
        }
        .animation(.snappy(duration: 0.3), value: model.toast?.id)
        .environment(\.colorScheme, .dark)
        .fixedSize()
    }

    private func content(for toast: Toast) -> some View {
        HStack(spacing: 10) {
            Image(systemName: toast.symbol).foregroundStyle(toast.isWarning ? Palette.signal : Palette.ok)
            Text(toast.message).font(BrandFonts.sans(13)).foregroundStyle(Palette.bone)
            if let actionTitle = toast.actionTitle, let action = toast.action {
                Button(actionTitle) {
                    model.toast = nil
                    action()
                }
                .buttonStyle(ControlButtonStyle())
                .font(BrandFonts.mono(12))
                .foregroundStyle(Palette.bone)
            }
        }
        .padding(.leading, 14)
        .padding(.trailing, toast.action == nil ? 14 : 5)
        .padding(.vertical, toast.action == nil ? 10 : 5)
        .background(.ultraThinMaterial, in: Capsule())
        .background(Palette.cathode.opacity(0.6), in: Capsule())
        .overlay(Capsule().stroke(.white.opacity(0.1)))
        .shadow(color: .black.opacity(0.35), radius: 16, y: 6)
        .padding(.top, 14)
        .padding(.horizontal, 20)
    }
}
