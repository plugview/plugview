import AppKit
import CryptoKit
import Foundation
import PlugviewCore

/// Installs a newer release in place and relaunches: download, verify, unpack, check, swap, reopen.
///
/// The download comes from `https://plugview.app` only (`ReleaseFeed` refuses any other host) and must match the
/// feed's SHA-256 and size; the unpacked app must carry this app's bundle id, the offered version, and a valid code
/// signature. The swap is atomic (`replaceItemAt` in the same volume), so a failure at any step leaves the running
/// app untouched. Files downloaded by URLSession carry no quarantine flag, so macOS does not ask to approve it again.
///
/// ponytail: trust rests on HTTPS to plugview.app plus the feed's checksum, the same as a download from the site.
/// With a Developer ID (phase 3.1), also require the new app's signature to name the same team before swapping.
enum UpdateInstaller {
    enum InstallError: Error, CustomStringConvertible {
        case notInstallable
        case tooLarge
        case checksumMismatch
        case unpackFailed
        case notPlugview
        case wrongVersion(String)
        case invalidSignature

        var description: String {
            switch self {
            case .notInstallable: "plugview cannot replace itself here"
            case .tooLarge: "the download is larger than expected"
            case .checksumMismatch: "the download does not match the release"
            case .unpackFailed: "the download could not be unpacked"
            case .notPlugview: "the download is not plugview"
            case .wrongVersion(let version): "the download is version \(version)"
            case .invalidSignature: "the download's signature is not valid"
            }
        }
    }

    /// Releases are about 1.5 MB; anything far larger is not ours.
    private static let maximumBytes = 50 * 1024 * 1024

    /// The running app's bundle when it can be replaced: an app (not the bare command) in a folder it may write to.
    static var installableBundle: URL? {
        let bundle = Bundle.main.bundleURL
        guard bundle.pathExtension == "app", FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path) else { return nil }
        return bundle
    }

    /// Whether `update` can be installed in place (it has a checksum and this app can replace itself).
    static func canInstall(_ update: ReleaseFeed.Update) -> Bool {
        update.sha256 != nil && installableBundle != nil
    }

    /// Downloads, verifies and swaps in `update`; returns the installed app's location. `progress` gets 0...1.
    static func install(_ update: ReleaseFeed.Update, progress: @escaping @MainActor (Double) -> Void) async throws -> URL {
        guard let bundle = installableBundle, let expectedChecksum = update.sha256 else { throw InstallError.notInstallable }
        let fileManager = FileManager.default
        // A scratch folder on the app's own volume, so the final swap is a rename.
        let workspace = try fileManager.url(for: .itemReplacementDirectory, in: .userDomainMask, appropriateFor: bundle, create: true)
        defer { try? fileManager.removeItem(at: workspace) }

        let archive = workspace.appending(path: "plugview.zip")
        try await download(update, to: archive, progress: progress)
        guard try sha256(of: archive) == expectedChecksum else { throw InstallError.checksumMismatch }

        let unpacked = workspace.appending(path: "unpacked", directoryHint: .isDirectory)
        guard run("/usr/bin/ditto", ["-x", "-k", archive.path, unpacked.path]) else { throw InstallError.unpackFailed }
        let newApp = unpacked.appending(path: "plugview.app", directoryHint: .isDirectory)
        try check(newApp, isVersion: update.version)

        await progress(1)
        _ = try fileManager.replaceItemAt(bundle, withItemAt: newApp)
        return bundle
    }

    /// Quits and opens the app at `bundle` once this process has exited.
    @MainActor
    static func relaunch(_ bundle: URL) {
        let reopen = Process()
        reopen.executableURL = URL(filePath: "/bin/sh")
        // The pid and path are arguments, never part of the script text.
        reopen.arguments = ["-c", "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; /usr/bin/open \"$2\"", "sh", "\(getpid())", bundle.path]
        do {
            try reopen.run()
        } catch {
            printError("could not schedule the relaunch: \(error)")
        }
        NSApp.terminate(nil)
    }

    private static func download(_ update: ReleaseFeed.Update, to file: URL, progress: @escaping @MainActor (Double) -> Void) async throws {
        var request = URLRequest(url: update.downloadURL, timeoutInterval: 30)
        request.setValue("plugview/\(plugviewVersion) (macOS)", forHTTPHeaderField: "User-Agent")
        let (bytes, response) = try await URLSession.shared.bytes(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else {
            throw UpdateChecker.CheckError.badResponse(statusCode: (response as? HTTPURLResponse)?.statusCode)
        }
        let expected = update.size ?? Int(response.expectedContentLength)
        guard expected <= maximumBytes else { throw InstallError.tooLarge }

        FileManager.default.createFile(atPath: file.path, contents: nil)
        let handle = try FileHandle(forWritingTo: file)
        defer { try? handle.close() }
        var buffer = Data()
        var received = 0
        for try await byte in bytes {
            buffer.append(byte)
            guard buffer.count >= 64 * 1024 else { continue }
            try handle.write(contentsOf: buffer)
            received += buffer.count
            buffer.removeAll(keepingCapacity: true)
            guard received <= maximumBytes else { throw InstallError.tooLarge }
            if expected > 0 { await progress(min(0.99, Double(received) / Double(expected))) }
        }
        try handle.write(contentsOf: buffer)
    }

    private static func sha256(of file: URL) throws -> String {
        SHA256.hash(data: try Data(contentsOf: file, options: .mappedIfSafe)).map { String(format: "%02x", $0) }.joined()
    }

    private static func check(_ app: URL, isVersion version: String) throws {
        guard let new = Bundle(url: app), new.bundleIdentifier == Bundle.main.bundleIdentifier else { throw InstallError.notPlugview }
        let newVersion = new.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown"
        guard newVersion == version else { throw InstallError.wrongVersion(newVersion) }
        guard run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path]) else { throw InstallError.invalidSignature }
    }

    /// Runs a system tool with plain arguments; true when it exits with 0.
    private static func run(_ tool: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(filePath: tool)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            return false
        }
        process.waitUntilExit()
        return process.terminationStatus == 0
    }
}
