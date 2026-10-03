use serde::Deserialize;

use crate::text::whole_number;

const SCHEMA_VERSION: i64 = 1;
/// Downloads are only ever opened from this host, whatever the feed says.
const HOST: &str = "plugview.app";

/// A newer release with a download for this platform and kind.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Update {
    pub version: String,
    pub download_url: String,
    /// The download's SHA-256, lowercase hex; `None` when the feed has none (then it can only be downloaded).
    pub sha256: Option<String>,
    /// The download's size in bytes.
    pub size: Option<u64>,
}

/// Why the release feed was not used.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FeedError {
    UnsupportedSchema(i64),
    UntrustedUrl(String),
    /// Not JSON, or a field this build reads is missing.
    Malformed(String),
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Manifest {
    schema_version: i64,
    version: String,
    artifacts: Vec<Artifact>,
}

#[derive(Deserialize)]
struct Artifact {
    platform: String,
    kind: String,
    url: String,
    sha256: Option<String>,
    size: Option<i64>,
}

/// The release to offer: newer than `current_version` and with a `platform`/`kind` download, else `None`.
///
/// # Errors
/// An unknown schema, a download outside `https://plugview.app`, or a malformed feed.
pub fn update_from_feed(feed: &[u8], current_version: &str, platform: &str, kind: &str) -> Result<Option<Update>, FeedError> {
    let manifest: Manifest = serde_json::from_slice(feed).map_err(|error| FeedError::Malformed(error.to_string()))?;
    if manifest.schema_version != SCHEMA_VERSION {
        return Err(FeedError::UnsupportedSchema(manifest.schema_version));
    }
    if !is_version_newer(&manifest.version, current_version) {
        return Ok(None);
    }
    let Some(artifact) = manifest.artifacts.iter().find(|artifact| artifact.platform == platform && artifact.kind == kind) else {
        return Ok(None);
    };
    if !is_trusted(&artifact.url) {
        return Err(FeedError::UntrustedUrl(artifact.url.clone()));
    }
    Ok(Some(Update {
        version: manifest.version,
        download_url: artifact.url.clone(),
        sha256: checksum(artifact.sha256.as_deref()),
        size: artifact.size.filter(|size| *size > 0).map(|size| size as u64),
    }))
}

/// A SHA-256 as 64 hexadecimal characters, lowercased; anything else is not trusted for installing.
fn checksum(text: Option<&str>) -> Option<String> {
    text.filter(|text| text.len() == 64 && text.bytes().all(|byte| byte.is_ascii_hexdigit())).map(str::to_ascii_lowercase)
}

fn is_trusted(address: &str) -> bool {
    url::Url::parse(address).is_ok_and(|url| url.scheme() == "https" && url.host_str().is_some_and(|host| host.eq_ignore_ascii_case(HOST)))
}

/// Whether `candidate` (a tag such as `v0.3.0`) is a later version than `current`; malformed versions never are.
#[must_use]
pub fn is_version_newer(candidate: &str, current: &str) -> bool {
    let (Some(candidate), Some(current)) = (version_parts(candidate), version_parts(current)) else { return false };
    let length = candidate.len().max(current.len());
    let padded = |parts: Vec<i64>| parts.into_iter().chain(std::iter::repeat(0)).take(length).collect::<Vec<_>>();
    padded(current) < padded(candidate)
}

fn version_parts(version: &str) -> Option<Vec<i64>> {
    version.strip_prefix('v').unwrap_or(version).split('.').map(whole_number).collect()
}
