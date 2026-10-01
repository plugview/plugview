import Foundation

/// Settings saved under the bundle id plugview used up to v0.1, before it moved to app.plugview.plugview.
public enum LegacyDefaults {
    public static let domainName = "io.github.mauricioleon54.plugview"

    /// The keys worth keeping: the window frame and the device picked in the app.
    public static let migratedKeys = ["NSWindow Frame PlayerWindow", "PreferredVideoDeviceID"]

    /// Set once the migration has run, so a value the user changed later is never overwritten.
    static let migrationMarkerKey = "DidMigrateLegacyDefaults"

    /// Copies the kept keys from the old domain into `defaults` the first time it runs; later calls do nothing.
    public static func migrate(from legacyDomain: [String: Any]?, into defaults: UserDefaults) {
        guard !defaults.bool(forKey: migrationMarkerKey) else { return }
        for key in migratedKeys where defaults.object(forKey: key) == nil {
            if let value = legacyDomain?[key] { defaults.set(value, forKey: key) }
        }
        defaults.set(true, forKey: migrationMarkerKey)
    }
}
