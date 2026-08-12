
# Keelah Arma Server Tool (KAST)

---

## Badges

***GitHub***  
[![GitHub issues](https://img.shields.io/github/issues/Foxlider/KAST.svg?logo=github&style=flat-square)](https://github.com/Foxlider/KAST/issues)
![GitHub](https://img.shields.io/github/license/Foxlider/KAST.svg?style=flat-square)
[![GitHub release](https://img.shields.io/github/release/Foxlider/KAST.svg?logo=github&style=flat-square)](https://GitHub.com/Foxlider/KAST/releases/)  
[![Github total downloads](https://img.shields.io/github/downloads/Foxlider/KAST/total.svg?logo=github&style=flat-square)](https://GitHub.com/Foxlider/KAST/releases/)
[![Github latest downloads](https://img.shields.io/github/downloads/Foxlider/KAST/latest/total.svg?logo=github&style=flat-square)](https://GitHub.com/Foxlider/KAST/releases/)

***CI / Quality***  
[![CI](https://github.com/Foxlider/KAST/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/Foxlider/KAST/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Foxlider/KAST/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/Foxlider/KAST/actions/workflows/codeql-analysis.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Foxlider_KAST&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Foxlider_KAST)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Foxlider_KAST&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Foxlider_KAST)

***Docker***  
[![Docker Image](https://ghcr-badge.egpl.dev/foxlider/kast/latest_tag?trim=major&label=nightly&style=flat-square)](https://github.com/Foxlider/KAST/pkgs/container/kast)

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

As always, best place to report issues is on the [GitHub Repo](https://github.com/Foxlider/KAST/issues). As for general discussion I'll keep an eye on the BI forum thread but I'll be more active on [Discord](https://discord.gg/2BUuZa3).

## **DOCUMENTATION**
  
A complete Documentation is available on the [GitHub Wiki](https://github.com/Foxlider/KAST/wiki)

## **INSTALLATION**

KAST is distributed as a self-contained single-file executable — no .NET installation required on the host.

### **Stable releases**

Download the latest release for your platform from the [Releases page](https://github.com/Foxlider/KAST/releases/latest):

| Platform | Archive |
| --- | --- |
| Linux x64 | `kast-linux-x64-v*.tar.gz` |
| Linux arm64 | `kast-linux-arm64-v*.tar.gz` |
| Windows x64 | `kast-win-x64-v*.zip` |
| Docker | `ghcr.io/foxlider/kast:latest` |

Extract and run the `KAST.UI` executable. On Linux you may need to `chmod +x KAST.UI` first.

### **Nightly builds**

Automated builds from the `develop` branch are published as a rolling pre-release at  
[`releases/tag/nightly`](https://github.com/Foxlider/KAST/releases/tag/nightly).

The nightly tag always points to the latest development commit. Download URLs are stable:

| Platform | File |
| --- | --- |
| Linux x64 | `kast-linux-x64-nightly.tar.gz` |
| Linux arm64 | `kast-linux-arm64-nightly.tar.gz` |
| Windows x64 | `kast-win-x64-nightly.zip` |
| Docker | `ghcr.io/foxlider/kast:nightly` |

> Nightly builds may be unstable. Use tagged releases for production.

### **Docker (Compose)**

Create a `docker-compose.yml` file:

```yaml
services:
  kast:
    image: ghcr.io/foxlider/kast:latest
    ports:
      - "5000:5000"                # KAST Web UI and API
      - "2302-2322:2302-2322/udp"  # Arma 3 server ports
    volumes:
      - kast-data:/app/data
      - kast-mods:/app/mods
      - kast-servers:/app/servers
    restart: unless-stopped

volumes:
  kast-data:
  kast-mods:
  kast-servers:
```

Start KAST and open `http://localhost:5000`:

```bash
docker compose up -d
```

#### **Arma 3 ports**

- Arma 3 server traffic is UDP. Do not add a matching TCP range.
- Allow `2302-2322/udp` through the Docker host firewall. For access from the internet, forward the same UDP range from the router or NAT gateway to the Docker host.
- To use another range, update both sides of the mapping and configure the server instances to use ports in that range, for example `2402-2422:2402-2422/udp`.

Each Arma 3 instance reserves a block of five consecutive UDP ports, from its base game port `n` through `n+4`:

| Port | Purpose |
| --- | --- |
| `n` | Game traffic and BattlEye RCon |
| `n+1` | Steam query |
| `n+2` | Steam traffic |
| `n+3` | Voice over network (VoN) |
| `n+4` | BattlEye traffic |

The number of instances a published range can hold is `floor((last port - first port + 1) / 5)`. The default `2302-2322` range contains 21 ports, so it supports four non-overlapping server blocks with one port left unused:

| Server | Base port | Reserved UDP block |
| --- | --- | --- |
| 1 | `2302` | `2302-2306` |
| 2 | `2307` | `2307-2311` |
| 3 | `2312` | `2312-2316` |
| 4 | `2317` | `2317-2321` |

Port `2322` is spare. KAST does not currently allocate or validate these blocks automatically, so assign a unique base port to every instance and check that its complete `n` through `n+4` block is published.

Port `5000/tcp` also serves KAST's management API. Do not expose it directly to the public internet; restrict it with a firewall, VPN, or authenticated reverse proxy.

## **VERSIONING**

KAST uses [MinVer](https://github.com/adamralph/minver) to derive the version from git tags at build time.

| Scenario | Version format |
| --- | --- |
| Tagged release `v1.2.3` | `1.2.3` |
| Nightly (develop) | `1.2.3-nightly.20260522.abc1234` |
| Local dev build | `1.2.3-alpha.0.5` |

To create a release, push a tag matching `v*` (e.g. `git tag v1.2.3 && git push --tags`).  
The CI pipeline will run tests, build native binaries for all platforms, push the Docker image, and publish a GitHub Release with all assets attached.
