import Foundation
import PlugviewCore

/// The app's saved choices, in the `app.plugview.plugview` defaults domain.
///
/// A terminal launch reads only the device and mode picks, and only when opened as an app (see `AppDelegate`).
@MainActor
struct AppSettings {
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        defaults.register(defaults: [
            Key.opensOnPlugIn: true,
            Key.keepsRunningInMenuBar: true,
            Key.checksForUpdates: true,
        ])
    }

    /// The device the user picked last (key kept from 0.2.0).
    var preferredDeviceID: String? {
        get { defaults.string(forKey: Key.preferredDeviceID) }
        nonmutating set { defaults.set(newValue, forKey: Key.preferredDeviceID) }
    }

    var opensOnPlugIn: Bool {
        get { defaults.bool(forKey: Key.opensOnPlugIn) }
        nonmutating set { defaults.set(newValue, forKey: Key.opensOnPlugIn) }
    }

    var keepsRunningInMenuBar: Bool {
        get { defaults.bool(forKey: Key.keepsRunningInMenuBar) }
        nonmutating set { defaults.set(newValue, forKey: Key.keepsRunningInMenuBar) }
    }

    var checksForUpdates: Bool {
        get { defaults.bool(forKey: Key.checksForUpdates) }
        nonmutating set { defaults.set(newValue, forKey: Key.checksForUpdates) }
    }

    var lastUpdateCheck: Date? {
        get { defaults.object(forKey: Key.lastUpdateCheck) as? Date }
        nonmutating set { defaults.set(newValue, forKey: Key.lastUpdateCheck) }
    }

    /// The mode picked for a device; `nil` means automatic.
    func modeChoice(forDevice uniqueID: String) -> ModeChoice? {
        (defaults.dictionary(forKey: Key.modeChoices)?[uniqueID] as? String).flatMap(ModeChoice.init(key:))
    }

    func setModeChoice(_ choice: ModeChoice?, forDevice uniqueID: String) {
        var choices = defaults.dictionary(forKey: Key.modeChoices) ?? [:]
        choices[uniqueID] = choice?.key
        defaults.set(choices, forKey: Key.modeChoices)
    }

    /// Whether the color-range fix is on for a device.
    func expandsLimitedRange(forDevice uniqueID: String) -> Bool {
        defaults.dictionary(forKey: Key.expandsLimitedRange)?[uniqueID] as? Bool ?? false
    }

    func setExpandsLimitedRange(_ isOn: Bool, forDevice uniqueID: String) {
        var values = defaults.dictionary(forKey: Key.expandsLimitedRange) ?? [:]
        values[uniqueID] = isOn ? true : nil
        defaults.set(values, forKey: Key.expandsLimitedRange)
    }

    private enum Key {
        static let preferredDeviceID = "PreferredVideoDeviceID"
        static let modeChoices = "VideoModeChoices"
        static let expandsLimitedRange = "ExpandsLimitedRange"
        static let opensOnPlugIn = "OpensOnPlugIn"
        static let keepsRunningInMenuBar = "KeepsRunningInMenuBar"
        static let checksForUpdates = "ChecksForUpdates"
        static let lastUpdateCheck = "LastUpdateCheck"
    }
}
