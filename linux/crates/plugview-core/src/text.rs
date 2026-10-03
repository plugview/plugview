/// The value of `text` when it is only ASCII digits: no sign, no spaces, no separators (spec: typed numbers).
pub(crate) fn whole_number(text: &str) -> Option<i64> {
    if text.is_empty() || !text.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }
    text.parse().ok()
}

/// Rounds half away from zero, as the spec requires, to a whole number.
pub(crate) fn rounded(value: f64) -> i64 {
    value.round() as i64
}
