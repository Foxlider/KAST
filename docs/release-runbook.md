# Stable release runbook

KAST stable releases are tag-driven. A stable release is published when a tag
matching `vX.Y.Z` points to a commit reachable from the `caster` branch.

## Promote from GitHub Actions

1. Open **Actions**.
2. Run **Promote Stable Release**.
3. Set `version` to the stable version, for example `1.2.3`.
4. Leave `target_ref` as `caster` unless promoting a specific commit that is
   already reachable from `caster`.
5. Leave `publish_latest` enabled for normal stable releases.

The promotion workflow creates an annotated tag and pushes it. The `Release`
workflow then runs tests, builds native packages, pushes Docker images, and
creates the GitHub Release.

## Promote from the command line

```bash
git fetch origin caster --tags
git checkout caster
git pull --ff-only origin caster
git tag -a v1.2.3 -m "KAST v1.2.3"
git push origin v1.2.3
```

CLI-created stable tags default to `make_latest: true` in the release workflow.
To create a stable release without marking it as latest, include this line in
the annotated tag message:

```text
Publish-Latest: false
```

## Expected outputs

- GitHub Release named `KAST vX.Y.Z`.
- Linux x64 archive plus `.sha256` file.
- Linux ARM64 archive plus `.sha256` file.
- Windows x64 archive plus `.sha256` file.
- GHCR Docker tags for `X.Y.Z`, `latest`, and `stable`.

Nightly releases remain separate and continue to use the rolling `nightly` tag.
