import Foundation
import Testing

@testable import PlugviewCore

@Suite struct LegacyDefaultsTests {
    private let frameKey = "NSWindow Frame PlayerWindow"
    private let deviceKey = "PreferredVideoDeviceID"

    private func makeDefaults() throws -> UserDefaults {
        let suiteName = "plugview-tests-\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suiteName))
        defaults.removePersistentDomain(forName: suiteName)
        return defaults
    }

    @Test func copiesTheKeptKeysOnly() throws {
        let defaults = try makeDefaults()
        LegacyDefaults.migrate(from: [frameKey: "787 500 1280 720 0 0 3440 1410 ", deviceKey: "card", "Other": 1], into: defaults)
        #expect(defaults.string(forKey: frameKey) == "787 500 1280 720 0 0 3440 1410 ")
        #expect(defaults.string(forKey: deviceKey) == "card")
        #expect(defaults.object(forKey: "Other") == nil)
    }

    @Test func runsOnceAndNeverOverwritesNewerValues() throws {
        let defaults = try makeDefaults()
        defaults.set("new", forKey: deviceKey)
        LegacyDefaults.migrate(from: [deviceKey: "old"], into: defaults)
        #expect(defaults.string(forKey: deviceKey) == "new")

        defaults.removeObject(forKey: frameKey)
        LegacyDefaults.migrate(from: [frameKey: "old frame"], into: defaults)
        #expect(defaults.object(forKey: frameKey) == nil)
    }

    @Test func toleratesAMissingLegacyDomain() throws {
        let defaults = try makeDefaults()
        LegacyDefaults.migrate(from: nil, into: defaults)
        #expect(defaults.bool(forKey: LegacyDefaults.migrationMarkerKey))
    }
}
