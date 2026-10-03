import Foundation
import PlugviewCore

/// Reads plugview's own release feed (`ReleaseFeed.url`); the app's only network request, and it can be turned off.
enum UpdateChecker {
    enum CheckError: Error {
        case badResponse(statusCode: Int?)
    }

    /// The latest release when it is newer than this build and has a Mac app, otherwise `nil`.
    static func newerRelease() async throws -> ReleaseFeed.Update? {
        var request = URLRequest(url: ReleaseFeed.url, timeoutInterval: 10)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.setValue("plugview/\(plugviewVersion) (macOS)", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        let statusCode = (response as? HTTPURLResponse)?.statusCode
        guard statusCode == 200 else { throw CheckError.badResponse(statusCode: statusCode) }

        return try ReleaseFeed.update(from: data, currentVersion: plugviewVersion, platform: "macos", kind: "app")
    }
}
