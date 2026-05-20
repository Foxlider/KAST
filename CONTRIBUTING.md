# Contributing to KAST

Thank you for taking the time to contribute! This document explains how the project is structured, how to get started, and the rules we follow to keep history clean and releases automated.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Branching Strategy](#branching-strategy)
- [Commit Messages — Conventional Commits](#commit-messages--conventional-commits)
- [Versioning](#versioning)
- [Development Setup](#development-setup)
- [Submitting Changes](#submitting-changes)
  - [Issues](#issues)
  - [Pull Requests](#pull-requests)
- [Release Process](#release-process)
- [Getting Help](#getting-help)

---

## Code of Conduct

By participating in this project you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md). We hold ourselves and contributors to high standards of communication.

---

## Branching Strategy

| Branch | Purpose |
|---|---|
| `main` | Stable releases only. Never commit directly. Tagged with `vMAJOR.MINOR.PATCH`. |
| `develop` | Integration branch. Every push triggers a nightly Docker image on GHCR. |
| `feature/<name>` | New features. Branch from `develop`, PR back into `develop`. |
| `hotfix/<name>` | Urgent production fixes. Branch from `main`, PR into **both** `main` and `develop`. |

**Rules:**
- `main` and `develop` are protected. All changes go through a Pull Request.
- Never push directly to `main` or `develop`.
- Branch names must follow the patterns above — they are used by the changelog generator to categorise commits when a conventional commit prefix is absent.

---

## Commit Messages — Conventional Commits

All commits must follow the [Conventional Commits](https://www.conventionalcommits.org/) specification. This drives automatic changelog generation and semantic versioning.

### Format

```
<type>[optional scope]: <short description>

[optional body]

[optional footer(s)]
```

### Types

| Type | When to use | Version bump |
|---|---|---|
| `feat` | A new feature | Minor (`0.x.0`) |
| `fix` | A bug fix | Patch (`0.0.x`) |
| `perf` | Performance improvement | Patch |
| `refactor` | Code change that neither fixes a bug nor adds a feature | — |
| `docs` | Documentation only | — |
| `test` | Adding or updating tests | — |
| `chore` | Build process, dependencies, tooling | — |
| `ci` | CI/CD configuration | — |

Append `!` after the type for a **breaking change** — this bumps the Major version:
```
feat!: remove legacy mod import API
```

### Examples

```
feat(mods): add Steam Workshop search by collection
fix(steam): reconnect loop on session expiry
perf(db): index ServerInstance by status
docs: document API key creation flow
chore: bump SteamKit2 to 3.1.0
```

### PR Titles

When merging via Pull Request, use the PR **title** as the conventional commit message — GitHub uses it as the squash-merge commit message. Individual commits on the branch do not need to be conventional, but the PR title must be.

---

## Versioning

KAST uses [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.

Versions are derived automatically from git tags using **MinVer** (a NuGet package). You never set the version manually in any `.csproj`.

| Scenario | Version example |
|---|---|
| Tagged commit on `main` (`v0.3.0`) | `0.3.0` |
| Commit on `develop` after `v0.3.0` | `0.3.1-alpha.0.4+abc1234` |
| Nightly Docker image | `ghcr.io/foxlider/kast:nightly-20260520` |
| Release Docker image | `ghcr.io/foxlider/kast:0.3.0`, `:latest` |

To see the current computed version locally:
```bash
dotnet build --configuration Release
# version is embedded in the assembly and shown in the app footer
```

---

## Development Setup

**Prerequisites:** .NET 10 SDK, Docker (optional).

```bash
git clone https://github.com/Foxlider/KAST.git
cd KAST
dotnet restore
dotnet build
dotnet test
```

**Run locally:**
```bash
dotnet run --project src/KAST.UI
```

**EF Core migrations** (if you change any entity):
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add <MigrationName> \
  --project src/KAST.Infrastructure \
  --startup-project src/KAST.UI \
  --output-dir Data/Migrations
```

**Docker:**
```bash
docker compose up --build
```

---

## Submitting Changes

### Issues

- Search existing issues before opening a new one.
- Use the provided templates (bug report / feature request).
- For breaking or architectural changes, open an issue to discuss before writing code.

### Pull Requests

1. Fork the repository (external contributors) or create a branch on the upstream repo (collaborators).
2. Branch from `develop` for features, from `main` for hotfixes:
   ```bash
   git checkout develop
   git checkout -b feature/my-feature
   # or
   git checkout main
   git checkout -b hotfix/critical-fix
   ```
3. Write your code. Keep commits focused; one logical change per commit.
4. Ensure all tests pass: `dotnet test`
5. Push and open a PR targeting the correct base branch (`develop` for features, `main` for hotfixes).
6. **PR title must follow the Conventional Commits format** — it becomes the squash-merge commit message and feeds the changelog.
7. For hotfixes: after merging into `main`, open a second PR to merge the fix into `develop` as well.

**PR checklist:**
- [ ] PR title follows Conventional Commits
- [ ] Tests added or updated for the changed behaviour
- [ ] No unrelated whitespace or refactoring mixed in
- [ ] EF migration included if entity models changed

---

## Release Process

Releases are fully automated. Only maintainers with write access to `main` perform releases.

1. Ensure `develop` is stable and all desired changes are merged.
2. Open a PR from `develop` into `main` with title `chore: release vX.Y.Z`.
3. After merge, tag the commit on `main`:
   ```bash
   git checkout main && git pull
   git tag v0.3.0
   git push origin v0.3.0
   ```
4. The `release` GitHub Actions workflow automatically:
   - Runs the full test suite
   - Generates release notes from conventional commits via git-cliff
   - Builds and pushes the Docker image to GHCR (`ghcr.io/foxlider/kast:0.3.0` + `:latest`)
   - Creates a GitHub Release with the generated notes

No manual changelog editing or version bumping is required.

---

## Getting Help

Join us on [Discord](https://discord.gg/2BUuZa3) and ask in the appropriate channel.
