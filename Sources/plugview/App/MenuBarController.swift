import AppKit
import PlugviewCore
import PlugviewUI
import SwiftUI

/// The menu bar icon and the panel it opens; only an app launch has one.
@MainActor
final class MenuBarController: NSObject {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let popover = NSPopover()
    private let onOpen: () -> Void

    init(content: MenuBarView, onOpen: @escaping () -> Void) {
        self.onOpen = onOpen
        super.init()
        let hostingController = NSHostingController(rootView: content)
        hostingController.sizingOptions = .preferredContentSize
        popover.contentViewController = hostingController
        popover.behavior = .transient
        popover.appearance = NSAppearance(named: .darkAqua)
        statusItem.button?.target = self
        statusItem.button?.action = #selector(togglePanel(_:))
        statusItem.button?.toolTip = "plugview"
    }

    func update(for state: LauncherState) {
        statusItem.button?.image = MenuBarIcon.image(for: state)
    }

    func showPanel() {
        guard let button = statusItem.button, !popover.isShown else { return }
        onOpen()
        NSApp.activate()
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
    }

    func closePanel() {
        popover.performClose(nil)
    }

    @objc private func togglePanel(_ sender: Any?) {
        if popover.isShown { closePanel() } else { showPanel() }
    }
}
