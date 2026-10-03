import Foundation

/// The self-hosted release feed: `https://plugview.app/releases/latest.json`, one file for every platform.
///
/// `scripts/release.sh` writes it. Schema 1 may gain fields; a breaking change needs a new `schemaVersion` and a
/// new feed path, so builds already installed keep reading the one they know.
public enum ReleaseFeed {
    public static let url = URL(string: "https://plugview.app/releases/latest.json") ?? URL(filePath: "/")
    static let schemaVersion = 1
    /// Downloads are only ever opened from this host, whatever the feed says.
    static let host = "plugview.app"

    public struct Update: Sendable, Equatable {
        public let version: String
        public let downloadURL: URL
    }

    public enum FeedError: Error, Equatable {
        case unsupportedSchema(Int)
        case untrustedURL(String)
    }

    /// The release to offer this build: newer than `currentVersion` and with a `platform`/`kind` download, else `nil`.
    ///
    /// A release without a download for this platform (e.g. a Windows-only fix) is not offered.
    public static func update(from data: Data, currentVersion: String, platform: String, kind: String) throws -> Update? {
        let manifest = try JSONDecoder().decode(Manifest.self, from: data)
        guard manifest.schemaVersion == schemaVersion else { throw FeedError.unsupportedSchema(manifest.schemaVersion) }
        guard isVersion(manifest.version, newerThan: currentVersion),
            let artifact = manifest.artifacts.first(where: { $0.platform == platform && $0.kind == kind })
        else { return nil }

        guard let url = URL(string: artifact.url), url.scheme == "https", url.host() == host else { throw FeedError.untrustedURL(artifact.url) }
        return Update(version: manifest.version, downloadURL: url)
    }

    /// The fields this build reads.
    ///
    /// Platform and kind are open strings, so new values (`windows`, `linux`) decode in older builds and are simply
    /// not picked.
    struct Manifest: Decodable {
        let schemaVersion: Int
        let version: String
        let artifacts: [Artifact]

        struct Artifact: Decodable {
            let platform: String
            let kind: String
            let url: String
        }
    }
}
