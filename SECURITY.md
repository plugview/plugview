# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please report it privately through
[GitHub's private vulnerability reporting](https://github.com/plugview/plugview/security/advisories/new),
not in a public issue. You will get a reply within 7 days.

plugview reads camera and microphone input locally. It never records, stores, or sends audio or video. Its only
network requests are about updates: at most once a day it reads `https://plugview.app/releases/latest.json`
(0.3.0 asked `api.github.com`), sending plugview's version in the user agent and nothing else. Turn it off in the
menu bar panel (**Check for updates daily**). The terminal command makes no network connections.

**Install and relaunch** (0.4.2 and later) downloads the new app only from `https://plugview.app`, and installs it
only if its SHA-256 and size match the release feed, its bundle id and version are plugview's and the offered ones,
and its code signature verifies. The swap is atomic: if any check fails, the installed app is left as it was and the
panel offers a manual download instead. Until plugview is signed with an Apple Developer ID, the checksum comes from
the same HTTPS feed as the download, so an update is exactly as trustworthy as downloading it from plugview.app; with
a Developer ID, installs will also require the new app's signature to come from the same developer.
