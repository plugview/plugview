# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please report it privately through
[GitHub's private vulnerability reporting](https://github.com/plugview/plugview/security/advisories/new),
not in a public issue. You will get a reply within 7 days.

plugview reads camera and microphone input locally. It never records, stores, or sends audio or video. Its only
network request is the app's update check: at most once a day it reads
`https://plugview.app/releases/latest.json` (0.3.0 asked `api.github.com`), sending plugview's version in the user agent and nothing else. Turn it off in the menu bar panel
(**Check for updates daily**). The terminal command makes no network connections.
