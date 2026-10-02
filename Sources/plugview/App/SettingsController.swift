import Foundation
import PlugviewCore
import PlugviewUI
import ServiceManagement

/// Backs the menu bar panel's settings: saves each toggle, registers the login item, and checks for updates.
@MainActor
final class SettingsController {
    private let model: MenuBarModel
    private let settings: AppSettings
    private var updateTask: Task<Void, Never>?

    init(model: MenuBarModel, settings: AppSettings) {
        self.model = model
        self.settings = settings
        model.opensOnPlugIn = settings.opensOnPlugIn
        model.keepsRunningInMenuBar = settings.keepsRunningInMenuBar
        model.checksForUpdates = settings.checksForUpdates
        // SMAppService registers the bundle that contains this binary; a bare command has none.
        model.canLaunchAtLogin = Bundle.main.bundleURL.pathExtension == "app"
        refreshLoginItem()

        model.setOpensOnPlugIn = { [weak self] isOn in
            self?.settings.opensOnPlugIn = isOn
            self?.model.opensOnPlugIn = isOn
        }
        model.setKeepsRunningInMenuBar = { [weak self] isOn in
            self?.settings.keepsRunningInMenuBar = isOn
            self?.model.keepsRunningInMenuBar = isOn
        }
        model.setChecksForUpdates = { [weak self] isOn in
            self?.settings.checksForUpdates = isOn
            self?.model.checksForUpdates = isOn
            if isOn { self?.checkForUpdatesIfDue() }
        }
        model.setLaunchesAtLogin = { [weak self] isOn in self?.setLaunchesAtLogin(isOn) }
        model.checkForUpdates = { [weak self] in self?.checkForUpdates() }
    }

    /// Reads the login item again; the user can also change it in System Settings › General › Login Items.
    func refreshLoginItem() {
        guard model.canLaunchAtLogin else { return }
        let status = SMAppService.mainApp.status
        model.launchesAtLogin = status == .enabled || status == .requiresApproval
        model.launchAtLoginNote =
            status == .requiresApproval ? "Allow plugview in System Settings › General › Login Items." : nil
    }

    private func setLaunchesAtLogin(_ isOn: Bool) {
        do {
            if isOn { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            refreshLoginItem()
        } catch {
            refreshLoginItem()
            model.launchAtLoginNote = "macOS refused: \(error.localizedDescription)"
        }
    }

    /// Checks once a day while automatic checks are on.
    func checkForUpdatesIfDue() {
        guard settings.checksForUpdates else { return }
        if let last = settings.lastUpdateCheck, Date.now.timeIntervalSince(last) < Tuning.updateCheckIntervalSeconds { return }
        checkForUpdates()
    }

    func checkForUpdates() {
        guard updateTask == nil else { return }
        model.updateStatus = .checking
        updateTask = Task { [weak self] in
            let status: UpdateStatus
            do {
                status = try await UpdateChecker.newerRelease().map { .available(version: $0.version, url: $0.pageURL) } ?? .upToDate
            } catch {
                status = .failed
            }
            guard let self else { return }
            if status != .failed { self.settings.lastUpdateCheck = .now }
            self.model.updateStatus = status
            self.updateTask = nil
        }
    }
}
