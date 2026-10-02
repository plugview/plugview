import AppKit
import PlugviewUI

/// The app launch's window life cycle and its menu bar icon.
extension AppDelegate {
    func setUpMenuBar() {
        let view = MenuBarView(launcher: launcher, controls: controls, menuBar: menuBar)
        menuBarController = MenuBarController(content: view) { [weak self] in self?.settingsController.refreshLoginItem() }
        menuBarController?.update(for: launcher.state)
        settingsController.checkForUpdatesIfDue()
    }

    /// Shows the window again from the menu bar; with `isPlaying`, a ready card starts at once (no intro).
    func openWindow(isPlaying: Bool) {
        menuBarController?.closePanel()
        if !menuBar.isWindowOpen {
            menuBar.isWindowOpen = true
            NSApp.setActivationPolicy(.regular)
            beginDisplayActivity()
            windowController.showLauncher()
        }
        windowController.showWindow(nil)
        NSApp.activate()
        Task {
            await attachIfPossible()
            if isPlaying, case .ready(let devices) = launcher.state, let first = devices.first { await play(first, isUserChoice: false) }
        }
    }

    /// App launch only: releases the card so its camera light goes off, and stays in the menu bar (unless the user
    /// turned that off, in which case AppKit quits right after this).
    func windowDidClose() {
        guard launchMode == .app else { return }
        menuBar.isWindowOpen = false
        autoPlayTask?.cancel()
        autoPlayTask = nil
        launcher.play = nil
        launcher.isAutoPlayPending = false
        detach(reason: "Window closed; plugview stays in the menu bar.")
        endDisplayActivity()
        NSApp.setActivationPolicy(.accessory)
        Task { await attachIfPossible() }
    }

    /// Settings… in the app menu.
    @objc func showSettings(_ sender: Any?) {
        menuBarController?.showPanel()
    }

    /// Check for Updates… in the app menu: shows the result in the menu bar panel.
    @objc func checkForUpdates(_ sender: Any?) {
        settingsController.checkForUpdates()
        menuBarController?.showPanel()
    }

    /// A game controller is not keyboard/mouse input, so without this the display would sleep mid-game.
    /// latencyCritical also keeps App Nap and timer coalescing away from the capture pipeline.
    func beginDisplayActivity() {
        guard displaySleepActivity == nil else { return }
        displaySleepActivity = ProcessInfo.processInfo.beginActivity(
            options: [.idleDisplaySleepDisabled, .userInitiated, .latencyCritical],
            reason: "Showing live capture video"
        )
    }

    func endDisplayActivity() {
        if let displaySleepActivity { ProcessInfo.processInfo.endActivity(displaySleepActivity) }
        displaySleepActivity = nil
    }

    /// Whether macOS opened plugview as a login item (the open-application event says so).
    static var isLaunchedAsLoginItem: Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent, event.eventID == kAEOpenApplication else { return false }
        return event.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }
}
