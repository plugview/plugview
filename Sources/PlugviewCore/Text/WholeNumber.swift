/// The value of `text` when it is only ASCII digits: no sign, no spaces, no separators.
///
/// Every platform reads numbers typed by people (`--volume`, `--mode`, versions) this way, so `+40` or ` 40` are refused
/// everywhere instead of being accepted by some parsers and not others.
func wholeNumber(_ text: some StringProtocol) -> Int? {
    let digits = UInt8(ascii: "0")...UInt8(ascii: "9")
    guard !text.isEmpty, text.utf8.allSatisfy({ digits.contains($0) }) else { return nil }
    return Int(text)
}
