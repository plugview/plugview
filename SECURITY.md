# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please report it privately through
[GitHub's private vulnerability reporting](https://github.com/plugview/plugview/security/advisories/new),
not in a public issue. You will get a reply within 7 days.

plugview reads camera and microphone input locally. It never records, stores, or sends audio or video. Its only
network request is the app's update check: at most once a day it asks `api.github.com` for the latest release,
sending plugview's version in the user agent and nothing else. Turn it off in the menu bar panel
(**Check for updates daily**). The terminal command makes no network connections.
