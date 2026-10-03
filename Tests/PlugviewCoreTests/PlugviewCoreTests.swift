import Foundation
import PlugviewCore
import Testing

// Behavior shared with the other platforms is tested from `spec/fixtures` (see `Spec/`). This file keeps what only
// the Mac has: its wording and its bundle.

@Suite struct LatencyAdviceTextTests {
    let healthy = LatencyFacts(
        requestedFrameRate: 60, deliveredFrameRate: 60, displayRefreshRate: 120, outputLatencyMilliseconds: 16, isOutputBluetooth: false)

    @Test func wordsEveryIssueForTheMac() {
        let facts = LatencyFacts(
            requestedFrameRate: 60, deliveredFrameRate: 20, displayRefreshRate: 50, outputLatencyMilliseconds: 200, isOutputBluetooth: true)
        #expect(
            latencyAdvice(for: facts) == [
                "The card delivers 20 fps instead of 60. Plug it straight into a USB 3 port (no hub) and check the console's video output setting.",
                "Sound plays over Bluetooth, which adds 100–300 ms. Use the Mac speakers or wired headphones for games.",
                "The display runs at 50 Hz, not a multiple of 20 fps, so frames are shown for uneven times. A refresh rate of 60 or 120 Hz gives even motion.",
            ])
    }

    @Test func wordsASlowWiredOutput() {
        var facts = healthy
        facts.outputLatencyMilliseconds = 55
        #expect(latencyAdvice(for: facts) == ["The playback device adds 55 ms. Wired headphones or the Mac speakers are faster."])
    }
}

@Suite struct VersionTests {
    @Test func versionIsSemantic() {
        let parts = plugviewVersion.split(separator: ".")
        #expect(parts.count == 3 && parts.allSatisfy { Int($0) != nil })
    }

    /// The embedded Info.plist carries the version too; a release must not ship two different numbers.
    @Test func infoPlistMatchesTheVersionConstant() throws {
        let plistURL = URL(filePath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "Sources/plugview/Resources/Info.plist")
        let plist = try #require(NSDictionary(contentsOf: plistURL))
        #expect(plist["CFBundleShortVersionString"] as? String == plugviewVersion)
        #expect(plist["CFBundleVersion"] as? String == plugviewVersion)
    }
}
