import Foundation
import PlugviewCore
import Testing

@Suite struct ReleaseSpecTests {
    struct FeedCase: Decodable {
        let description: String
        let currentVersion: String
        let platform: String
        let kind: String
        let expected: Expected
    }

    struct Expected: Decodable {
        let update: Update?
        let error: String?
        let url: String?
        let schemaVersion: Int?
    }

    struct Update: Decodable, Equatable {
        let version: String
        let url: String
        var sha256: String? = nil
        var size: Int? = nil
    }

    struct Versions: Decodable {
        let newer: [Pair]
        let notNewer: [Pair]
    }

    struct Pair: Decodable {
        let candidate: String
        let current: String
    }

    @Test func feedReadingMatchesTheSpec() throws {
        struct Spec: Decodable { let cases: [FeedCase] }
        let cases = try Fixture.decode(Spec.self, from: "release-feed.json").cases
        guard let rawCases = try Fixture.object("release-feed.json")["cases"] as? [[String: Any]] else { throw FixtureError.missingField("cases") }

        for (testCase, rawCase) in zip(cases, rawCases) {
            let feed = try feedData(rawCase)
            do {
                let update = try ReleaseFeed.update(from: feed, currentVersion: testCase.currentVersion, platform: testCase.platform, kind: testCase.kind)
                #expect(testCase.expected.error == nil, "\(testCase.description): read, expected an error")
                #expect(
                    update.map { Update(version: $0.version, url: $0.downloadURL.absoluteString, sha256: $0.sha256, size: $0.size) }
                        == testCase.expected.update,
                    "\(testCase.description)")
            } catch ReleaseFeed.FeedError.unsupportedSchema(let schemaVersion) {
                #expect(testCase.expected.error == "unsupportedSchema" && testCase.expected.schemaVersion == schemaVersion, "\(testCase.description)")
            } catch ReleaseFeed.FeedError.untrustedURL(let url) {
                #expect(testCase.expected.error == "untrustedURL" && testCase.expected.url == url, "\(testCase.description)")
            } catch is DecodingError {
                #expect(testCase.expected.error == "malformed", "\(testCase.description)")
            }
        }
    }

    @Test func versionComparisonMatchesTheSpec() throws {
        let versions = try Fixture.decode(Versions.self, from: "versions.json")
        for pair in versions.newer {
            #expect(isVersion(pair.candidate, newerThan: pair.current), "\(pair.candidate) > \(pair.current)")
        }
        for pair in versions.notNewer {
            #expect(!isVersion(pair.candidate, newerThan: pair.current), "\(pair.candidate) is not > \(pair.current)")
        }
    }

    /// The feed as served: `feedText` verbatim, or `feed` written back as JSON.
    private func feedData(_ rawCase: [String: Any]) throws -> Data {
        if let text = rawCase["feedText"] as? String { return Data(text.utf8) }
        guard let feed = rawCase["feed"] else { throw FixtureError.missingField("feed") }
        return try JSONSerialization.data(withJSONObject: feed)
    }
}
