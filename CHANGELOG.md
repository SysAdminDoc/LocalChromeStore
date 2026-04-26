# Changelog

All notable changes to LocalChromeStore are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/), versioning is [Semantic Versioning](https://semver.org/).

## Unreleased

## v0.3.0 — 2026-07-03

### Added
- **`localchromestore.json` repo manifest (F004)** — Extension repos can now place a `localchromestore.json` in their root to supply catalog metadata (display name, description, homepage URL, icon URL, category, keywords, and optional `hideFromCatalog` flag). When found, the file's fields take precedence over `manifest.json`/repo metadata for catalog-facing display, and the card shows an "LCS Manifest" badge.
- **Catalog manifest validator (F005)** — The manifest is validated on fetch: field lengths (display name ≤ 64 chars, description ≤ 280 chars), known category values, and URL syntax. Validation failures surface in the card's existing warnings system without blocking discovery.
- **Build command dry-run / checklist (F026)** — The "Why" tooltip and a "Copy Build Cmd" card button now show the conventional build command for the detected framework (WXT → `wxt build`, Plasmo → `plasmo build`, Extension.js → `npx extension build`, CRXJS → `vite build`, web-ext → `web-ext build`). Clicking copies the command to the clipboard. Unknown/plain extensions show nothing.
- **Teal accent tokens** — Added `TealColor`/`TealBrush`/`TealSoftBrush` (Catppuccin Mocha `#94e2d5`) to `DarkTheme.xaml` for the new LCS Manifest badge.
- **`RepoManifest` tests** — `RepoManifestTests.cs` covers `Validate()` (valid manifest, field-length limits, unknown category, URL validity, multiple errors) and `FrameworkLabels.BuildCommand()` for all five frameworks and the three no-command cases. Total test count 56 → 79.

## v0.2.0 — 2026-07-03

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
