## [1.1.1] - 2026-06-06
### Bug Fixes
- Generate changelog via action
- Honor parallel mod download limit

## [1.1.0] - 2026-06-06
### Bug Fixes
- Config file generation, disk write, and bidirectional UI sync
- Implement proper IDisposable pattern in SteamClientService (S3881)
- Pipeline hotfix for docker
- Unit tests
- Update permissions in release workflow
- Update Dockerfile to include CPM files
- Relative pathing & incorrect instanceID access
- Support internal self-connections
- Keep mod downloads running across navigation
- Harden discovery and mod download retries
- Resolve develop conflicts
- Pass all code quality warnings
- Resolve Arma import cancellation race
- Handle protected browser keyring failures
- Harden bulk queue recovery

### Features
- AST-based Arma 3 config parser with nested class support
- Wire up SignalR hubs to services for real-time broadcasting
- Add background metrics service for real-time SignalR broadcasting
- Add DB-persisted application settings with env var overrides
- Add process watchdog for crash recovery with restart policies
- Add scheduling background service for auto start/stop
- Implement DownloadAppAsync for Arma 3 DS install/update via SteamKit2
- Add dark/light theme toggle in header
- Enhance database queries and UI components for improved performance and usability
- Enhance server instance management with installation tracking and download progress
- Enhance download process with step tracking and parallel download settings
- Enhance process management with output handling and monitoring capabilities
- Update GitHub Actions workflows for CI/CD and add new features
- Add LastChecked and Comment properties to SteamMod model and update related migrations
- Enhance mod management by adding IsClientSide flag and updating related services and UI components
- Add ModsTab for managing server mods with drag-and-drop functionality
- Migration to CPM
- Enhance theme management with dark mode toggle and accent color options
- Add nightly build and release workflows for automated packaging and GitHub releases
- Add Unload All action to instance mods tab
- Update UX and monitoring flows
- Better campaign view
- Implement Output Sanitizer for improved error handling and logging
- Update OutputSanitizer to use content root path and add test for mods virtual path
- Sanitize executable path in StartInstanceAsync method
- Remove cancellation token from RunAsync calls in StartServerInstall and StartModInstall methods
- Add native app update flow
- Add Windows service management

### Maintenance
- Add GitHub Actions CI/CD pipeline and pin .NET SDK version
- Use .KAST_DATA/ as dev workdir for all data files
- Bump xunit.runner.visualstudio from 3.1.4 to 3.1.5 (#30)
- Bump QRCoder from 1.7.0 to 1.8.0 (#29)
- Bump MinVer from 5.0.0 to 7.0.0 (#28)
- Bump softprops/action-gh-release from 2 to 3 (#23)
- Bump coverlet.collector from 6.0.4 to 10.0.1 (#27)
- Bump github/codeql-action from 3 to 4 (#25)
- Bump crazy-max/ghaction-github-labeler from 5 to 6 (#24)
- Bump docker/build-push-action from 6 to 7 (#22)
- Bump Microsoft.AspNetCore.Cryptography.KeyDerivation and 8 others (#26)
- Bump action versions (#36)
- Sanitize paths
- Trigger caster nightly
- Skip SonarCloud when token is missing
- Add stable promotion workflow

### Other
- Merge native app update flow
- Sync remote caster

### Refactoring
- Code quality fixes

