import Foundation
import PlugviewCore

/// Asks GitHub for the latest plugview release; the app's only network request, and it can be turned off.
enum UpdateChecker {
    struct Release: Sendable {
        let version: String
        let pageURL: URL
    }

    enum CheckError: Error {
        case badResponse
    }

    /// The latest release when it is newer than this build, otherwise `nil`.
    static func newerRelease() async throws -> Release? {
        var request = URLRequest(url: latestReleaseURL, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("plugview/\(plugviewVersion)", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw CheckError.badResponse }

        let latest = try JSONDecoder().decode(LatestRelease.self, from: data)
        // Only ever open a GitHub page from what the response says.
        guard let pageURL = URL(string: latest.htmlURL), pageURL.scheme == "https", pageURL.host() == "github.com" else {
            throw CheckError.badResponse
        }
        guard isVersion(latest.tagName, newerThan: plugviewVersion) else { return nil }
        return Release(version: latest.tagName.hasPrefix("v") ? String(latest.tagName.dropFirst()) : latest.tagName, pageURL: pageURL)
    }

    private struct LatestRelease: Decodable {
        let tagName: String
        let htmlURL: String

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case htmlURL = "html_url"
        }
    }

    private static let latestReleaseURL = URL(string: "https://api.github.com/repos/plugview/plugview/releases/latest") ?? URL(filePath: "/")
}
