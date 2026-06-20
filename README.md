
# Kheela Arma Server Tool (Enterprise Edition)

### CASTER

---

## Badges

***GitHub***  
[![GitHub issues](https://img.shields.io/github/issues/bluefield-creator/KAST.svg?logo=github&style=flat-square)](https://github.com/bluefield-creator/KAST/issues)
![GitHub](https://img.shields.io/github/license/bluefield-creator/KAST.svg?style=flat-square)
[![GitHub release](https://img.shields.io/github/release/bluefield-creator/KAST.svg?logo=github&style=flat-square)](https://GitHub.com/bluefield-creator/KAST/releases/)  
[![Github total downloads](https://img.shields.io/github/downloads/bluefield-creator/KAST/total.svg?logo=github&style=flat-square)](https://GitHub.com/bluefield-creator/KAST/releases/)
[![Github latest downloads](https://img.shields.io/github/downloads/bluefield-creator/KAST/latest/total.svg?logo=github&style=flat-square)](https://GitHub.com/bluefield-creator/KAST/releases/)

***CI / Quality***  
[![CI](https://github.com/bluefield-creator/KAST/actions/workflows/ci.yml/badge.svg?branch=caster)](https://github.com/bluefield-creator/KAST/actions/workflows/ci.yml)
[![CodeQL](https://github.com/bluefield-creator/KAST/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/bluefield-creator/KAST/actions/workflows/codeql-analysis.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=bluefield-creator_KAST&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=bluefield-creator_KAST)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=bluefield-creator_KAST&metric=coverage)](https://sonarcloud.io/summary/new_code?id=bluefield-creator_KAST)

***Docker***  
[![Docker Image](https://ghcr-badge.egpl.dev/bluefield-creator/kast/latest_tag?trim=major&label=nightly&style=flat-square)](https://github.com/bluefield-creator/KAST/pkgs/container/kast)

[![Discord](https://img.shields.io/discord/366955806777671681?label=Discord&logo=discord&logoColor=white&style=for-the-badge)](https://discord.gg/2BUuZa3)

## Intro

Our unit originally adopted KAST because we needed a management panel that
was accessible to administrators who did not necessarily want to conduct every
server operation through scripts, configuration files, and ritual sacrifice.

As we began using it more extensively, however, we encountered a number of
limitations. Our deployment required features more commonly associated with
enterprise software, including OIDC and LDAP authentication, support for
multiple administrator accounts, native service installation, more reliable
update workflows, and a more robust system for downloading and managing mods.

Rather than continuing to build increasingly elaborate workarounds around
the existing application, I forked KAST and began developing an expanded
edition designed around those requirements.

The result is **KASTED**, although we generally prefer the considerably more
pronounceable name **CASTER**.

CASTER is the enterprise-focused edition of KAST, built for communities and
organisations that require stronger authentication, multi-user administration,
automated deployment, and more dependable server management.

## Features

### Authentication & Security

| Feature | Description |
| --- | --- |
| Multi-admin accounts | Multiple administrator accounts with individual credentials |
| OIDC / OpenID Connect | Compatible with Authentik and other OIDC providers |
| Three auth modes | Local-only, OIDC-only, or Local+OIDC fallback login |
| Group-claim access control | Restrict OIDC access to specific groups (e.g. `KAST Admins`) |
| System account sign-in | Windows local users, AD domain users, or Linux local users |
| API keys | Generate, revoke, and track API keys for automation and integrations |
| First-run bootstrapping | Initial administrator or OIDC provisioning on fresh install |

### Deployment & Operations

| Feature | Description |
| --- | --- |
| Windows Service | Install, configure, start, stop, and remove CASTER as a Windows service |
| Service crash recovery | Configure restart-on-crash with delay and failure reset window |
| Auto-update | Check GitHub releases, download, stage, and apply updates from within the UI |
| Update channels | Stable and nightly channels with configurable auto-check |
| Service-aware updater | Stops the Windows service before overwriting files during updates |
| Docker | Pre-built multi-arch images with `latest`, `stable`, and `nightly` tags |
| Health endpoints | `/health` (web/database) and `/ready` (includes download queue status) |
| Reverse proxy support | Caddy, nginx — websocket proxying for Blazor Server |
| Forwarded headers | Configurable trusted proxy IPs for reverse proxy deployments |

### Server Management

| Feature | Description |
| --- | --- |
| Multiple server profiles | Create, edit, and delete individual server configurations |
| Headless Client support | Configurable count with auto-launch |
| Restart policies | None, On Crash, or Always — with configurable max attempts |
| Scheduled start/stop | Per-instance auto-start and auto-stop times (HH:mm) |
| Process watchdog | Automatic crash detection and restart |
| Process history | Track start, stop, restart, and crash events with duration |
| Performance tuning | Hyper-threading toggle, max memory override, CPU count override, ranking toggle |
| Creator DLC toggles | Individual toggle for all 8 official CDLCs |
| Additional launch parameters | Custom command-line arguments per instance (e.g. `-hugepages`) |
| Live server status | Stopped, Starting, Running, Stopping, Crashed, Restarting indicators |

### Server Configuration

| Feature | Description |
| --- | --- |
| `server.cfg` editor | Structured editor with hostname, password, max players, MOTD, voting, and more |
| `basic.cfg` editor | Bandwidth, MinBandwidth, MaxMsgSend, and other performance settings |
| Arma 3 Profile editor | Difficulty settings with raw profile text fallback |
| Raw config editing | Direct text editing with bidirectional sync to structured fields |
| Auto-generated configs | Configuration files written to disk on save and launch |
| Unsaved changes guard | Navigation confirmation when unsaved edits exist |

### Steam Integration

| Feature | Description |
| --- | --- |
| Arma 3 Server install | Install and update Arma 3 Dedicated Server (Stable, Development, DLCs, Legacy) |
| Workshop mod management | Install, update, and manage Arma 3 Steam Workshop mods |
| Steam credentials | Username + password + Steam Guard login |
| Steam QR login | Log in via Steam mobile app QR code scan |
| Mod preset import | Import mod presets from Arma 3 Launcher HTML exports |
| Mod update check | Check for mod updates on application launch |
| Steam profile display | Avatar, persona name, and SteamID in settings |

### Mod Management

| Feature | Description |
| --- | --- |
| Centralized mod page | Table and card views for browsing all installed mods |
| Parallel downloads | Configurable parallel Steam download workers with independent concurrency limit |
| Durable download queue | Queued and running downloads survive process restarts |
| Per-mod actions | Download, update, verify, and cancel individual mods |
| Update All | Batch-update all outdated mods in one action |
| Per-instance mods | Drag-and-drop load ordering with client-side / server-side flags |
| Local mods | Import mods from local folders with custom search paths |
| Manifest tracking | SteamManifestId and InstalledManifestId for reliable update detection |
| Mod comment field | Add notes to individual mods |
| Speed benchmark | Built-in tool for finding optimal download parallelism |

### Mission Management

| Feature | Description |
| --- | --- |
| Mission upload | Upload PBO files via the web UI or API |
| HTTP mission downloads | Player-facing download endpoint — alternative to Steam Workshop |
| Integrity verification | CRC32/BZip2 hashing for mission files |
| Conditional requests | ETag and If-Modified-Since support (304 Not Modified) |
| Byte-range support | Partial and resumable downloads via HTTP range requests |
| Download logging | Track player name, Steam ID, server address, and user agent per download |
| Tagging system | Create, delete, assign, and remove tags from missions |
| Mission search | Filter by text query, tags, and map name |
| Campaign management | Create campaigns, add/remove missions, reorder mission sequence |
| Mission sets | Group missions into named sets |
| Mod preset linking | Bind a mission to a specific mod preset for easy deployment |

### Monitoring & Observability

| Feature | Description |
| --- | --- |
| Host metrics dashboard | Real-time CPU, memory, and disk usage with live time-series charts |
| Per-instance metrics | CPU, memory, and player count charts for each server |
| Live console tailing | Color-coded server console output with log level filtering |
| RPT event detection | Detect mission starts, Steam connections, admin activity, and crashes from RPT logs |
| Server events panel | Recent events with severity levels in the Monitor tab |
| External process detection | Detect and display all running Arma processes, including unmanaged ones |
| Process kill | Kill external Arma processes from the monitoring page |
| SignalR broadcasting | Real-time server status changes and metrics pushed to the UI |
| Configurable metrics interval | Adjust how often metrics are collected |

### REST API

| Feature | Description |
| --- | --- |
| Server CRUD | Full API for server instances (`/api/servers`) |
| Mod CRUD | Full API for mods, downloads, updates, and presets (`/api/mods`) |
| Mission API | Upload, download, CRUD, search, tags, campaigns, and sets (`/api/missions`) |
| Monitoring API | Host and instance metrics (`/api/monitoring`) |
| Process API | List running processes and kill by PID (`/api/processes`) |
| Settings API | Read and update settings, manage API keys (`/api/settings`) |
| Download state snapshot | Authoritative queue state for UI recovery after reconnects |
| OpenAPI | Endpoint tags and Swagger support |

### UI / UX

| Feature | Description |
| --- | --- |
| Web-based UI | Blazor Server — accessible from any browser, no client install |
| Dark / Light theme | Toggle with persistent preference |
| Custom accent color | Color picker with preset swatches |
| Responsive navigation | Server listing with live status icons and filtering |
| Live status updates | Server status pushed to the navigation in real time via SignalR |
| Unsaved changes guard | Confirm-before-leave dialog when navigating away from unsaved edits |
| Reconnect handling | Modal and state recovery when SignalR disconnects |

## Prerequisites

- Steam account with a valid copy of Arma 3.
- Basic understanding of Arma 3 dedicated servers.

## Issues and Feedback

Report issues on the [GitHub repository](https://github.com/bluefield-creator/KAST/issues).
For general discussion, join us on [Discord](https://discord.gg/2BUuZa3).

## Documentation

A complete documentation is available on the [GitHub Wiki](https://github.com/bluefield-creator/KAST/wiki).

## Installation

CASTER is distributed as a self-contained single-file executable — no .NET
installation required on the host.

### Stable releases

Download the latest release for your platform from the [Releases page](https://github.com/bluefield-creator/KAST/releases/latest):

| Platform | Archive |
| --- | --- |
| Linux x64 | `kast-linux-x64-v*.tar.gz` |
| Linux arm64 | `kast-linux-arm64-v*.tar.gz` |
| Windows x64 | `kast-win-x64-v*.zip` |
| Docker | `ghcr.io/bluefield-creator/kast:latest` or `ghcr.io/bluefield-creator/kast:stable` |

Extract and run the `KAST.UI` executable. On Linux you may need to
`chmod +x KAST.UI` first.

### Nightly builds

Automated builds from the `caster` branch are published as a rolling pre-release
at [`releases/tag/nightly`](https://github.com/bluefield-creator/KAST/releases/tag/nightly).

The nightly tag always points to the latest development commit. Download URLs
are stable:

| Platform | File |
| --- | --- |
| Linux x64 | `kast-linux-x64-nightly.tar.gz` |
| Linux arm64 | `kast-linux-arm64-nightly.tar.gz` |
| Windows x64 | `kast-win-x64-nightly.zip` |
| Docker | `ghcr.io/bluefield-creator/kast:nightly` |

> Nightly builds may be unstable. Use tagged releases for production.

### Docker (Compose)

```yaml
services:
  kast:
    image: ghcr.io/bluefield-creator/kast:latest
    ports:
      - "8080:8080"
    volumes:
      - kast-data:/app/data
volumes:
  kast-data:
```

### Operations

CASTER exposes `/health` for basic web/database health and `/ready` for
readiness including the active mod download count. Bulk mod downloads are
queued durably; `/api/downloads/state` is the authoritative queue snapshot
used by the UI after reconnects.

The Settings page includes a Service tab for Windows hosts. It can install
CASTER as a Windows service, set startup mode after reboot, configure crash
restart actions, and show recent crash reports. Service changes require
running CASTER as Administrator. Non-Windows deployments should use their
supervisor instead (`systemd`, Docker restart policies, or the hosting
platform restart policy).

When running behind Caddy or another reverse proxy, keep websocket proxying
enabled for Blazor Server and configure CASTER to trust only the proxy IPs
that can reach it. For a local Caddy reverse proxy, the default trusted
proxies are `127.0.0.1` and `::1`; override with `ForwardedHeaders:KnownProxies`
if the proxy runs elsewhere.

Example Caddy configuration:

```caddyfile
panel.3rdshock.army {
    encode zstd gzip

    reverse_proxy 127.0.0.1:5000 {
        flush_interval -1
    }
}
```

If the CASTER process exits during active downloads, configure Windows
Service, systemd, Docker, or your supervisor to restart it. On startup
CASTER reconciles interrupted queued/running download records and resets
mods left in `Downloading` or `Updating` state so they can be retried safely.

### Authentication and OIDC

CASTER uses local administrator accounts by default. OpenID Connect can be
enabled through configuration or environment variables and is compatible with
Authentik and other OIDC providers.

Existing CASTER administrators can also enable system account sign-in from
**Settings -> Accounts**. Windows installs can allow local machine users or AD
domain users. Linux installs can allow local Linux users from the running
system. Docker installs use local users inside the CASTER container, not users
from the Docker host; create or mount those container accounts before selecting
them in CASTER.

Supported auth modes:

| Mode | Behavior |
| --- | --- |
| `Local` | Local CASTER username/password sign-in only. |
| `Oidc` | OIDC sign-in only. The first allowed OIDC user bootstraps the first CASTER administrator. |
| `LocalAndOidc` | OIDC sign-in with local administrator passwords kept as a fallback. |

Example Docker environment:

```yaml
environment:
  - Auth__Mode=LocalAndOidc
  - Auth__Oidc__Authority=https://auth.example.com/application/o/kast/
  - Auth__Oidc__ClientId=kast
  - Auth__Oidc__ClientSecret=replace-with-provider-secret
  - Auth__Oidc__DisplayName=OpenID Connect
  - Auth__Oidc__GroupClaim=groups
  - Auth__Oidc__NameClaim=preferred_username
  - Auth__Oidc__AllowedGroups__0=KAST Admins
```

For Authentik, configure the application/provider with:

| Setting | Value |
| --- | --- |
| Redirect URI | `https://<kast-host>/auth/oidc/callback` |
| Logout/signed-out URI | `https://<kast-host>/auth/oidc/signed-out` |
| Scopes | `openid profile email` |
| Group claim | `groups` |
| Required group value | `KAST Admins` |

Authentik application assignment alone is not enough for CASTER access. The
OIDC user must also have at least one configured allowed group in the
configured group claim. CASTER links external accounts by OIDC issuer plus
`sub`, so email or username changes in Authentik do not break the account link.

## Versioning

CASTER uses [MinVer](https://github.com/adamralph/minver) to derive the version
from git tags at build time.

| Scenario | Version format |
| --- | --- |
| Tagged release `v1.2.3` | `1.2.3` |
| Nightly (caster) | `1.2.3-nightly.20260522.abc1234` |
| Local dev build | `1.2.3-alpha.0.5` |

To create a stable release, run the **Promote Stable Release** workflow from
GitHub Actions or push an annotated tag matching `vX.Y.Z` from a commit
reachable from `caster`. The release pipeline runs tests, builds native
binaries for all platforms, publishes checksum assets, pushes Docker image
tags, and publishes a non-prerelease GitHub Release.

Maintainer release steps are documented in
[docs/release-runbook.md](docs/release-runbook.md).
