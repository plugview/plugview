/// Whether `candidate` (a release tag such as `v0.3.0`) is a later version than `current` (such as `0.2.0`).
///
/// Compares dot-separated numbers; a missing part counts as 0. Anything that is not a plain version is never newer.
public func isVersion(_ candidate: String, newerThan current: String) -> Bool {
    guard let candidateParts = versionParts(candidate), let currentParts = versionParts(current) else { return false }
    let length = max(candidateParts.count, currentParts.count)
    let padded = { (parts: [Int]) in parts + Array(repeating: 0, count: length - parts.count) }
    return padded(currentParts).lexicographicallyPrecedes(padded(candidateParts))
}

private func versionParts(_ version: String) -> [Int]? {
    let trimmed = version.hasPrefix("v") ? version.dropFirst() : Substring(version)
    let parts = trimmed.split(separator: ".", omittingEmptySubsequences: false).map { Int($0) }
    guard !parts.isEmpty, parts.allSatisfy({ ($0 ?? -1) >= 0 }) else { return nil }
    return parts.compactMap { $0 }
}
