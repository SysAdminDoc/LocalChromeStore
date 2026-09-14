# Changelog

All notable changes to LocalChromeStore are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/), versioning is [Semantic Versioning](https://semver.org/).

## Unreleased

### Fixed
- **Test isolation and CDP handle cleanup** — serialized the affected temp-file tests, corrected a cache-test cleanup path that could delete the shared temp root, and changed CDP browser process ownership to `SafeProcessHandle` with partial-launch cleanup.
- **State and log hardening** — bounded cache, usage-stat, and settings JSON reads before deserialization; settings backup/schema recovery now surfaces a non-secret activity-log warning; daily JSONL event logs retain only the configured 30-day window.

## v0.4.0 — 2026-06-30

### Added
- **Framework build output resolution** — local source folders now resolve `manifest.json` from framework build outputs (`.output/chrome-mv3`, `build/chrome-mv3-prod`, `dist`, `extension`, `public`) in addition to the project root.
- **Structured JSON event log** — every activity log line is now written to daily `events-*.jsonl` files under `%LOCALAPPDATA%\LocalChromeStore\logs\` with ISO 8601 timestamps, level (info/warn/error), auto-classified category, and message.
- **DevTools/options quick links** — extension cards now show "Options" and "DevTools" link buttons when the manifest declares `options_page`/`options_ui` or `devtools_page`, with the page path surfaced in the "Why" tooltip.
- **Parallel batch downloads** — "Update all" now runs up to 3 downloads concurrently instead of sequentially.
- **Offline catalog cache** — the last-discovered catalog is cached to disk and loaded on startup, so the store shows extensions immediately without waiting for a GitHub refresh.
- **Chrome extension sample fixtures** — test fixtures (`mv3-full`, `mv2-legacy`, `mv3-minimal`, `high-risk`, `edge-cases`) drive parser and permission regression tests against real-world manifest shapes.
- **Permission-diff in policy consent** — applying Enterprise Policy now shows permission changes between the installed version and the current catalog release in the consent dialog.
- **Historical version restore** — environment import now attempts to find the exact requested release version on GitHub before falling back to the current catalog release.
- **WPF UI smoke-test harness** — headless integration tests exercise settings persistence, extension service, catalog cache, event log, browser detection, and policy enrollment against real temp-directory state.
- **Source adapter interface** — `IExtensionSource` abstracts GitHub and local-folder discovery behind a common interface for future source types.
- **Pinned/favorite repos** — cards can be pinned to stay at the top of the catalog; pins are persisted in settings.
- **License badges** — extension cards display the repository's SPDX license identifier from GitHub metadata.
- **Local-only usage stats** — refreshes, installs, uninstalls, updates, and launches are tallied per-session and per-extension in `usage-stats.json` and surfaced in diagnostics exports.
- **File watcher** — local source folder `manifest.json` changes trigger an activity log notification prompting a manual refresh.

### Changed
- **Octokit 14.0.0** — upgraded from 13.0.1 with zero breaking changes.

## v0.3.12 — 2026-06-30

### Added
- **Local source folders** — settings now accepts unpacked extension folders with a root `manifest.json`, refresh merges them into the catalog as `local/...` entries, and install links the source folder directly without copying or deleting project files.
- **Local source portability** — settings persistence, environment import/export, diagnostics, card status/readiness, update-all logic, and search now treat local source folders as first-class install sources.
- **Regression coverage** — added tests for local source discovery, settings de-duping, direct source linking, environment manifest persistence, and import-source classification.

## v0.3.11 — 2026-06-30

### Added
- **Browser stdout/stderr capture** — command-line launch sessions now redirect browser stdout/stderr and stream output into the activity log. Nonzero process exits are logged with the browser display name and exit code, making startup/load failures visible after launch.
- **Capture regression test** — added coverage for stdout, stderr, and nonzero process-exit reporting through the retained browser output capture path.

## v0.3.10 — 2026-06-30

### Added
- **Launch debug panel** — the launch toolbar now shows the selected browser path, resolved profile path, active extension set, startup URL, and launch arguments before a session starts. The panel is derived from the same launch plan used by **Launch session** and **Copy args**.

## v0.3.9 — 2026-06-30

### Added
- **Chrome for Testing downloader** — added a **Get CfT** toolbar action that resolves the official latest-Stable Chrome for Testing metadata feed, downloads the Windows ZIP, extracts it under `%LOCALAPPDATA%\LocalChromeStore\cache\chrome-for-testing\`, refreshes browser detection, and selects the installed CfT executable.
- **CfT diagnostics and tests** — diagnostics now report the Chrome for Testing cache path, browser detection scans LocalChromeStore's CfT cache, and installer tests cover metadata selection, ZIP extraction, and existing-build reuse without live network calls.

## v0.3.8 — 2026-06-30

### Added
- **Persistent launch profiles** — browser launches now support **Default**, **Persistent**, and **Clean temp** profile modes. Persistent mode reuses a stable LocalChromeStore `--user-data-dir` per browser/load set under `%LOCALAPPDATA%\LocalChromeStore\profiles\persistent\`.
- **Profile-mode persistence** — settings and environment manifests now carry the explicit launch profile mode while still migrating the legacy `LaunchWithTemporaryProfile` flag.
- **Profile-aware launch evidence** — launch preview, activity log, diagnostics, and CDP launch arguments now identify persistent versus temporary profile directories.

## v0.3.7 — 2026-06-30

### Added
- **Release asset provenance** — GitHub discovery now records release asset ID, content type, uploader, upload/update timestamps, size, digest, and download count from the release-assets REST payload.
- **Changed-since-install signal** — installs persist a release-asset snapshot, and cards/catalog export/diagnostics compare the current asset against the installed snapshot to show unchanged, changed, or legacy-snapshot-unavailable states.
- **Review and export visibility** — extension cards, the manifest risk review, diagnostics bundles, machine-readable catalog export, and environment manifests now expose compact release provenance alongside checksum source.

## v0.3.6 — 2026-06-30

### Added
- **Policy package-risk preflight** — Enterprise Policy apply now scans the installed extension package after CRX packaging and before any HKLM write. The scan blocks policy installs for MV2 packages, remote executable code patterns, `eval`/`new Function`/string timer execution, remote `script-src` CSP, and configured malicious extension ID matches.
- **Diagnostics risk evidence** — diagnostics now include package-risk summaries and findings for installed extensions, including derived policy extension IDs when a policy signing key exists.
- **Optional malicious-ID feed** — policy preflight can read newline-delimited Chrome extension IDs from `%LOCALAPPDATA%\LocalChromeStore\cache\policy-risk\malicious-extension-ids.txt` or an app-local `policy-risk\malicious-extension-ids.txt`.

## v0.3.5 — 2026-06-28

### Added
- **Browser loading conformance harness** — added an opt-in **Conformance** action that generates a tiny MV3 fixture extension, launches each detected Chromium-family browser and Chrome for Testing build in an isolated profile, records browser version, resolved load strategy, effective arguments, command preview, process outcome, CDP extension IDs/errors, and writes JSON plus text reports under `%LOCALAPPDATA%\LocalChromeStore\logs\`.
- **Chrome for Testing detection** — browser discovery now scans common Chrome for Testing/Selenium/Puppeteer cache locations, labels detected builds separately, and treats them as plain `--load-extension` capable even on newer milestones.
- **Diagnostics report linkage** — diagnostics bundles now include the latest browser conformance JSON/text report paths.

### Fixed
- **Launch-toolbar clipping** — browser launch controls now wrap at narrower desktop widths instead of truncating the rightmost action.

## v0.3.4 — 2026-06-28

### Added
- **GitHub API digest verification** — discovery records release asset `digest` values from GitHub's REST asset JSON when Octokit does not expose them. Installs still prefer SHA-256 sidecars, but now fail closed against GitHub `sha256:` API digests when no sidecar is present.
- **Verification-source visibility** — installed state, environment/catalog exports, risk review copy, card trust badges, and diagnostics now distinguish sidecar-verified, GitHub API digest-verified, integrity-verifiable, and unverified release assets.

## v0.3.3 — 2026-06-28

### Added
- **Guided Enterprise Policy workflow** — installed extension cards now expose **Policy** and **Rollback** actions. Policy packaging creates/reuses a per-extension RSA signing key, builds a CRX3 package under `%LOCALAPPDATA%\LocalChromeStore\policy-packages\`, generates or copies `update.xml`, prompts for hosted CRX/update URLs, shows enrollment/HKLM impact before writing, applies the selected browser policy, and runs registry/update XML/CRX reachability health checks.
- **Edge self-host update override** — Edge policy installs now merge `ExtensionSettings` JSON with `installation_mode=force_installed`, `update_url`, and `override_update_url=true`, while rollback removes only the matching extension ID from both `ExtensionInstallForcelist` and Edge `ExtensionSettings`.

## v0.3.2 — 2026-06-28

### Added
- **Branded-Chrome CDP launch path** — `BrowserLaunchManager` now routes `LaunchStrategy.CdpLoadUnpacked` through `CdpExtensionLoader`, launching branded Chrome with `--remote-debugging-pipe --enable-unsafe-extension-debugging` and loading each active installed extension via `Extensions.loadUnpacked`. The activity log records the full launch command, returned extension IDs, and exact CDP error messages with fallback guidance when live loading fails.

## v0.3.1 — 2026-06-27

### Added
- **CRX3 policy-packaging primitives** — added a local RSA-2048 CRX3 packager that writes the CRX3 protobuf header, signs `CRX3 SignedData`, derives Chrome's deterministic a-p extension ID from the RSA SubjectPublicKeyInfo, verifies generated package signatures, blocks same-extension updates when the signing key fingerprint changes, and emits self-hosted Chrome/Edge `update.xml` manifests. Covered by `Crx3PackageServiceTests`.
- **Enterprise Policy install backend** — added a WPF-free `PolicyInstallService` that maps Chrome / Edge / Brave / Chromium to their `HKLM\Software\Policies\...\ExtensionInstallForcelist` targets, writes numbered force-install entries only after explicit consent, updates existing extension-ID entries in place, rolls back registry policy values without deleting packaged CRX/update artifacts, and exposes reusable warning/consent copy.
- **Policy health checks** — added policy diagnostics for native browser policy-page availability, registry state, extension-ID validity, update URL validity, update XML structure/app ID/codebase/version, and CRX URL reachability. The main UI now has a policy-readiness action that logs the selected browser policy target and machine enrollment state without writing policy.
- **In-app self-update check** — on launch the app checks its own GitHub releases (`SysAdminDoc/LocalChromeStore`) and, if a newer published version exists, shows a dismissible banner with a "Download update" button that opens the release page. The check is non-blocking and best-effort (offline/rate-limit failures are silent), uses the tolerant semver comparison, and never downloads or installs the app itself. (`GitHubService.CheckForAppUpdateAsync` + `EvaluateSelfUpdate`; banner in `MainWindow`.)
- **Version-gated launch strategy** — `BrowserLauncher` now detects each browser's major version (`FileVersionInfo`) and resolves how it can load extensions: plain `--load-extension` (pre-137 / Chrome for Testing), `--load-extension` + `--disable-features=DisableLoadExtensionCommandLineSwitch` (unbranded Chromium / Brave / Edge / Vivaldi / Opera 137+), or none (branded Chrome 142+, which removed both the flag and the override). Launch plans carry the chosen `LaunchStrategy`, a `LoadsExtensions` flag, and human-readable warnings.

- **Single-instance guard** — a per-user named mutex now prevents a second app instance from running (trivial from a portable ZIP) and corrupting the JSON state files via concurrent writes; the second launch signals the first to surface its window and exits immediately.
- **CDP `Extensions.loadUnpacked` loader for branded Chrome** — hand-rolled DevTools-Protocol wire framing (NUL-delimited JSON), `Extensions.loadUnpacked` command builder, and the required `--remote-debugging-pipe --enable-unsafe-extension-debugging` launch flags (`Services/Cdp`), the basis for loading unpacked extensions into branded Chrome 137+/142+ which removed command-line `--load-extension`. The Windows fd-3/4 handshake is now implemented in full: `CdpPipeProcess` launches the browser via `CreateProcess` with a `STARTUPINFOEX` handle list plus the MSVCRT `lpReserved2` inheritance block that maps the two CDP pipes onto the child's fd 3 (read) and fd 4 (write) — which `ProcessStartInfo` cannot express — and the loader drives `loadUnpacked` over those pipes, falling back gracefully on any failure. (End-to-end load against branded Chrome 142 still needs on-device verification.)
- **Enterprise-policy enrollment precondition check** — `PolicyEnrollmentService` detects whether the machine is Active Directory domain-joined, Microsoft Entra–joined, or Chrome Browser Cloud Management–enrolled, and evaluates whether off-store/self-hosted `ExtensionInstallForcelist` force-install can actually work (it does not on un-enrolled consumer Windows). The diagnostics bundle now reports policy-mode readiness so the limitation is visible before any future policy-install path is used.
- **Crash-safe JSON state persistence** — `settings.json`, `installed.json`, and `loadsets.json` are now written atomically (temp file + `fsync` + `File.Replace`) keeping the prior good copy as a `.bak`. A crash or power loss mid-write can no longer truncate state; loads transparently recover from the `.bak` if the live file is corrupt.

- **Organization private-repo discovery** — discovery now detects whether each configured owner is a user or an organization and lists org repos via `GetAllForOrg`, which surfaces private org repos the PAT can access (`GetAllForUser` silently omitted them).

### Changed
- **MainViewModel decomposition for headless testing** — launch, load-set, and import/export logic moved out of the ~1,500-line `MainViewModel` into focused, WPF-free services: `BrowserLaunchManager` (empty-set + post-launch messages), `LoadSetManager` (sentinel, active-set resolution, snapshot, persistence), and `ImportExportService` (catalog export projection + environment-import target classification). The view model delegates to them with behavior unchanged; the new services are unit-tested directly (`BrowserLaunchManagerTests`, `LoadSetManagerTests`, `ImportExportServiceTests`). Builds on the earlier `IDialogService` seam toward the F082 UI-smoke harness.
- **MV2 extensions marked as not-loadable** — the manifest-version badge for MV2 extensions now reads "MV2 · not loadable" with a tooltip explaining that Manifest V2 was removed from Chrome 139 (mid-2025) and an MV3 release is required; the manifest is still parsed for metadata.
- **Cheaper, faster discovery** — discovery no longer downloads the entire release ZIP just to read `manifest.json`; it reads the source manifest once via the repo content API (eliminating a duplicate path-probe pass) and probes repos with bounded concurrency. ZIP-only repos still appear from their release asset and enrich at install time.

### Fixed
- **Accessibility: mute-text contrast** — bumped the muted/caption text color to Catppuccin Mocha Overlay2 (`#9399b2`) so small de-emphasized text clears WCAG AA (~5.8:1 on the base surface; the previous Overlay1 `#7f849c` was ~4.46:1). Activity-log auto-scroll-to-newest and settings-drawer first-field focus were already in place; subtext (`#a6adc8`) already met AA.
- **Stale extension icons after an update** — the icon cache key now hashes the icon URL and version, so an extension whose icon changed re-fetches it instead of serving the old cached `{owner}_{name}.png` forever.
- **Version/identity drift** — the footer and GitHub `User-Agent`/`ProductHeaderValue` now read the real assembly version instead of a hardcoded `0.1.0`; the README roadmap now matches shipped CHANGELOG history.
- **False "update available" badges from naive version comparison** — update detection now uses a tolerant semver-aware comparison (`VersionCompare`) that normalizes a leading `v`/`V`, compares dotted segments numerically (so `1.10 > 1.2`), ignores `+build` metadata, and ranks prereleases below their release. Equal-but-differently-formatted tags (`v1.0` vs `1.0`) no longer show as updates.
- **Extensions silently failing to load on current Chromium builds** — the launcher now adds the `--disable-features` override for Chromium-family browsers (Chromium 137+ disabled `--load-extension` by default), so Brave/Chromium/Edge actually load extensions again.
- **Branded Chrome 137+/142+ no longer load via command line** — launching branded Chrome that can no longer accept `--load-extension` now opens the browser but clearly warns that extensions will not load, and points to Chrome for Testing / Brave / a clean temp profile / policy mode.
- **Silent arg-drop into a running browser** — launching into a non-temporary profile while the same browser is already running now warns that Chromium forwards the command line to the existing window and drops `--load-extension`.

## v0.3.0 — 2026-06-14

### Added
- **`localchromestore.json` repo manifest (F004)** — Extension repos can now place a `localchromestore.json` in their root to supply catalog metadata (display name, description, homepage URL, icon URL, category, keywords, and optional `hideFromCatalog` flag). When found, the file's fields take precedence over `manifest.json`/repo metadata for catalog-facing display, and the card shows an "LCS Manifest" badge.
- **Catalog manifest validator (F005)** — The manifest is validated on fetch: field lengths (display name ≤ 64 chars, description ≤ 280 chars), known category values, and URL syntax. Validation failures surface in the card's existing warnings system without blocking discovery.
- **Build command dry-run / checklist (F026)** — The "Why" tooltip and a "Copy Build Cmd" card button now show the conventional build command for the detected framework (WXT → `wxt build`, Plasmo → `plasmo build`, Extension.js → `npx extension build`, CRXJS → `vite build`, web-ext → `web-ext build`). Clicking copies the command to the clipboard. Unknown/plain extensions show nothing.
- **Teal accent tokens** — Added `TealColor`/`TealBrush`/`TealSoftBrush` (Catppuccin Mocha `#94e2d5`) to `DarkTheme.xaml` for the new LCS Manifest badge.
- **`RepoManifest` tests** — `RepoManifestTests.cs` covers `Validate()` (valid manifest, field-length limits, unknown category, URL validity, multiple errors) and `FrameworkLabels.BuildCommand()` for all five frameworks and the three no-command cases. Total test count 56 → 79.

## v0.2.0 — 2026-06-14

### Added
- **Named load sets (F013)** — Snapshot the currently-installed extensions into a named launch profile. The toolbar load-set selector lets you switch between "All installed" and any saved set. Sets are persisted to `%APPDATA%\LocalChromeStore\loadsets.json` and respected by Launch, Launch (installed only), and the launch preview summary.
- **Per-repo hidden-repo restore (F045)** — The settings drawer "Hidden repositories" section now lists individual hidden repos with per-row Restore buttons in addition to the existing "Restore all" action.
- **Accessibility sweep (F076)** — Added `AutomationProperties.Name` to all previously unlabeled interactive controls: search box, browser selector, load-set selector, GitHub credentials inputs, extra-owner input, and topic filter. Screen readers now surface useful names for every toolbar and settings-drawer input.
- **Broader unit tests (F081)** — `PermissionCatalogTests.cs` covers `Describe()` risk classification (High/Medium/Low/Informational) for 20+ permissions, case-insensitive lookup, optional-flag forwarding, `DescribeHost()` universal/wildcard/exact host patterns, and `Aggregate()` dominance rules. `LoadSetSerializationTests.cs` covers JSON roundtrip with null and non-null `ExtensionKeys`, list serialization, and `CreatedAt` preservation. Total test count 10 → 56.

### Fixed
- `LaunchBrowser()` `installedOnly` parameter bug where both branches were identical; replaced by load-set–aware `GetActiveLoadSetExtensions()`.

## v0.1.0 — 2026-04-25

Initial release.

### Added
- WPF / .NET 9 desktop store UI with Catppuccin Mocha dark theme
- GitHub-sourced discovery of Chrome extensions across one or more user / org accounts
- Detection rules: latest-release `.zip` or `.crx` asset, fallback to `manifest.json` at common repo paths (`/`, `extension/`, `src/`, `dist/`, `public/`)
- Manifest enrichment: `name`, `version`, `description`, and `icons` parsed from the ZIP or `manifest.json`
- Per-extension store cards with logo, name, version, description, repo link, and stars
- One-click **Install** — downloads the latest release asset, extracts to `%LOCALAPPDATA%\LocalChromeStore\extensions\<owner>\<repo>\<version>\`, prunes older versions
- One-click **Uninstall** — removes the local copy and updates the install manifest
- One-click **Launch** — fires Chrome / Brave / Edge / Vivaldi / Opera / Chromium with `--load-extension=` pointing at every installed extension
- Browser auto-detection from standard install paths
- Search and "installed only" filter
- Optional GitHub topic filter (default `chrome-extension`)
- Optional GitHub PAT for higher API rate limits and private-repo access
- Activity log panel + on-disk crash log writer
- Zip-slip path-traversal guard during extraction
- CRX2 + CRX3 header stripping with inner-ZIP extraction
- Async I/O for every network and disk operation

### Known limitations
- Browser extensions loaded via `--load-extension` show the standard "developer mode extensions" banner; this is a Chromium UX, not a LocalChromeStore bug. The Enterprise Policy path in v0.2.0 will eliminate the banner.
- Auto-update is not implemented; refresh detects new releases but installation is still manual per card.
- Light theme is not yet available.
- Settings UI exposes only the primary GitHub user; additional owners are persisted in `settings.json` but not yet editable in the GUI.

## Roadmap archive — 2026-08-10 — ROADMAP.md

<details>
<summary>Original roadmap snapshot</summary>

```markdown
# Roadmap

ROADMAP.md is actionable-only. Completed work is removed; blocked work lives in
Roadmap_Blocked.md.

## P1

1. **Audit-identified reliability**
   - Fix flaky parallel test execution: `BrowserConformanceServiceTests`, `SettingsServiceTests`, `SmokeTests` have TOCTOU temp-dir races under xUnit parallel runs. Add `[Collection]` attributes or unique temp roots.
   - CdpPipeProcess uses raw `IntPtr` for the process handle instead of `SafeProcessHandle`; a partially-failed Dispose can leak the handle.

## P2

1. **Audit-identified hardening**
   - CatalogCacheService / UsageStatsService have no file-size limit on deserialization; a corrupted or malicious cache file could exhaust memory before the catch fires.
   - SettingsService `ReadJsonWithBackup` silently resets settings if the JSON is valid but schema-mismatched; consider logging when the backup is used.
   - JsonEventLog has no daily log file rotation or cleanup of old `events-*.jsonl` files.

## P3

1. **Later polish and integrations**
   - Add GitHub Pages static update hosting.
   - Add advanced `ExtensionSettings` controls.
   - Add a custom update-feed source.
   - Add a GitHub draft-release helper.
   - Add a light theme and accent picker.
   - Move UI strings to resource files for future localization.
   - Add MSIX packaging.
   - Add Authenticode signing when a certificate is available.
   - Add a shared Git-backed catalog workflow.
   - Revisit an Avalonia port only after the Windows feature set is stable.
```

</details>
