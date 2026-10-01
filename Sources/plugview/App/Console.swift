import Foundation

/// Writes a `plugview:`-prefixed message to standard error.
func printError(_ message: String) {
    FileHandle.standardError.write(Data("plugview: \(message)\n".utf8))
}
