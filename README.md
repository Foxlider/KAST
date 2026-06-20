
# Keelah Arma Server Tool (KAST)

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

## **INTRO**

After developing FASTER for a few years, I decided to restart the whole project to make a new Architecture from a clean slate.
This new Architecture should allow contributors to better participate in the project development.

Big up to all the devs, testers and users. Also, to BI for giving us an awesome game to break.

## **PREREQUISITES**

- Steam account with valid copy of Arma 3.
- Basic understanding of Arma 3 dedicated servers.


## **FEATURES**

- Steam Workshop Integration
  - Install and update Arma 3 Server (Stable, Dev, DLCs, Legacy)
  - Install, update and manage Arma 3 Workshop mods
  - Import Local Mods
  - Supports Steam Guard and Mobile Auth
  - Import mod presets from Arma 3 Launcher
  - Check for mod updates on app launch

- Multiple Server Profiles
  - Save and load multiple server presets
  - Supports all server config options
  - Supports all server command line options
  - Custom mission params
  - Custom difficulty
  - Headless Client support and auto launch
  - Correctly displays mods in Server Browser
  - Load Steam Mod Presets (html presets) to your profiles
  - Manually editable config files

- Local Mod Support
  - Reads local mods from server folder
  - Include additional folders to search


## **ISSUES and FEEDBACK**

As always, best place to report issues is on the [GitHub Repo](https://github.com/bluefield-creator/KAST/issues). As for general discussion I'll keep an eye on the BI forum thread but I'll be more active on [Discord](https://discord.gg/2BUuZa3).

## **DOCUMENTATION**
  
A complete Documentation is available on the [GitHub Wiki](https://github.com/bluefield-creator/KAST/wiki)

## **INSTALLATION**

KAST is distributed as a self-contained single-file executable — no .NET installation required on the host.

### **Stable releases**

Download the latest release for your platform from the [Releases page](https://github.com/bluefield-creator/KAST/releases/latest):

| Platform | Archive |
| --- | --- |
| Linux x64 | `kast-linux-x64-v*.tar.gz` |
| Linux arm64 | `kast-linux-arm64-v*.tar.gz` |
| Windows x64 | `kast-win-x64-v*.zip` |
| Docker | `ghcr.io/bluefield-creator/kast:latest` or `ghcr.io/bluefield-creator/kast:stable` |

Extract and run the `KAST.UI` executable. On Linux you may need to `chmod +x KAST.UI` first.

### **Nightly builds**

Automated builds from the `caster` branch are published as a rolling pre-release at  
[`releases/tag/nightly`](https://github.com/bluefield-creator/KAST/releases/tag/nightly).

The nightly tag always points to the latest development commit. Download URLs are stable:

| Platform | File |
| --- | --- |
| Linux x64 | `kast-linux-x64-nightly.tar.gz` |
| Linux arm64 | `kast-linux-arm64-nightly.tar.gz` |
| Windows x64 | `kast-win-x64-nightly.zip` |
| Docker | `ghcr.io/bluefield-creator/kast:nightly` |

> Nightly builds may be unstable. Use tagged releases for production.

### **Docker (Compose)**

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

### **Operations**

KAST exposes `/health` for basic web/database health and `/ready` for readiness
including the active mod download count. Bulk mod downloads are queued
durably; `/api/downloads/state` is the authoritative queue snapshot used by the
UI after reconnects.

The Settings page includes a Service tab for Windows hosts. It can install KAST
as a Windows service, set startup mode after reboot, configure crash restart
actions, and show recent crash reports. Service changes require running KAST as
Administrator. Non-Windows deployments should use their supervisor instead
(`systemd`, Docker restart policies, or the hosting platform restart policy).

When running behind Caddy or another reverse proxy, keep websocket proxying
enabled for Blazor Server and configure KAST to trust only the proxy IPs that
can reach it. For a local Caddy reverse proxy, the default trusted proxies are
`127.0.0.1` and `::1`; override with `ForwardedHeaders:KnownProxies` if the
proxy runs elsewhere.

Example Caddy route:

```caddyfile
panel.example.com {
    reverse_proxy 127.0.0.1:5000
}
```

If the KAST process exits during active downloads, configure Windows Service,
systemd, Docker, or your supervisor to restart it. On startup KAST reconciles
interrupted queued/running download records and resets mods left in
`Downloading` or `Updating` state so they can be retried safely.

### **Authentication and OIDC**

KAST uses local administrator accounts by default. OpenID Connect can be enabled
through configuration or environment variables and is compatible with Authentik
and other OIDC providers.

Existing KAST administrators can also enable system account sign-in from
**Settings -> Accounts**. Windows installs can allow local machine users or AD
domain users, depending on whether a domain is configured. Linux installs can
allow local Linux users from the running system. Docker installs use local users
inside the KAST container, not users from the Docker host; create or mount those
container accounts before selecting them in KAST.

Supported auth modes:

| Mode | Behavior |
| --- | --- |
| `Local` | Local KAST username/password sign-in only. |
| `Oidc` | OIDC sign-in only. The first allowed OIDC user bootstraps the first KAST administrator. |
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

Authentik application assignment alone is not enough for KAST access. The OIDC
user must also have at least one configured allowed group in the configured
group claim. KAST links external accounts by OIDC issuer plus `sub`, so email or
username changes in Authentik do not break the account link.

## **VERSIONING**

KAST uses [MinVer](https://github.com/adamralph/minver) to derive the version from git tags at build time.

| Scenario | Version format |
| --- | --- |
| Tagged release `v1.2.3` | `1.2.3` |
| Nightly (develop) | `1.2.3-nightly.20260522.abc1234` |
| Local dev build | `1.2.3-alpha.0.5` |

To create a stable release, run the **Promote Stable Release** workflow from GitHub Actions or push an annotated tag matching `vX.Y.Z` from a commit reachable from `caster`. The release pipeline runs tests, builds native binaries for all platforms, publishes checksum assets, pushes Docker image tags, and publishes a non-prerelease GitHub Release.

Maintainer release steps are documented in [docs/release-runbook.md](docs/release-runbook.md).
