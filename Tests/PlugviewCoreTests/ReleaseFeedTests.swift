import Foundation
import Testing

@testable import PlugviewCore

@Suite struct ReleaseFeedTests {
    private let macAppURL = "https://plugview.app/releases/0.4.0/plugview-0.4.0-macos-universal-app.zip"

    private func feed(schema: Int = 1, version: String = "0.4.0", artifacts: String? = nil) -> Data {
        let macApp = #"{"platform":"macos","arch":"universal","kind":"app","url":"\#(macAppURL)","sha256":"ab","size":1}"#
        let linux = #"{"platform":"linux","arch":"x86_64","kind":"cli","url":"https://plugview.app/l.tar.gz","sha256":"cd","size":1}"#
        let json = #"{"schemaVersion":\#(schema),"version":"\#(version)","releasedAt":"2026-10-03","artifacts":[\#(artifacts ?? "\(macApp),\(linux)")]}"#
        return Data(json.utf8)
    }

    private func update(_ data: Data, current: String = "0.3.0") throws -> ReleaseFeed.Update? {
        try ReleaseFeed.update(from: data, currentVersion: current, platform: "macos", kind: "app")
    }

    @Test func offersTheNewerMacAppAndIgnoresOtherPlatforms() throws {
        let update = try update(feed())
        #expect(update?.version == "0.4.0")
        #expect(update?.downloadURL.absoluteString == macAppURL)
    }

    @Test func offersNothingWhenUpToDate() throws {
        #expect(try update(feed(), current: "0.4.0") == nil)
    }

    @Test func skipsAReleaseWithoutADownloadForThisPlatform() throws {
        let windowsOnly = #"{"platform":"windows","arch":"x86_64","kind":"app","url":"https://plugview.app/x.zip"}"#
        #expect(try update(feed(artifacts: windowsOnly)) == nil)
    }

    @Test(arguments: ["http://plugview.app/x.zip", "https://evil.example/x.zip", "https://plugview.app.evil.example/x.zip"])
    func rejectsDownloadsFromAnywhereElse(url: String) {
        let artifact = #"{"platform":"macos","arch":"universal","kind":"app","url":"\#(url)"}"#
        #expect(throws: ReleaseFeed.FeedError.untrustedURL(url)) { try update(feed(artifacts: artifact)) }
    }

    @Test func rejectsAnUnknownSchema() {
        #expect(throws: ReleaseFeed.FeedError.unsupportedSchema(2)) { try update(feed(schema: 2)) }
    }

    @Test func rejectsMalformedJSON() {
        #expect(throws: DecodingError.self) { try update(Data("<html>404</html>".utf8)) }
    }
}
