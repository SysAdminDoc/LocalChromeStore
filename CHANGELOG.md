# Changelog

All notable changes to LocalChromeStore are documented here. Format roughly follows [Keep a Changelog](https://keepachangelog.com/), versioning is [Semantic Versioning](https://semver.org/).

## Unreleased

### Added
- Auditable launch sessions with startup URL, clean temporary Chromium profile mode, and copyable launch command preview.
- Focused xUnit test project covering launch argument construction and SHA256 sidecar parsing.
- Windows CI build/test workflow and Dependabot coverage for NuGet and GitHub Actions.

### Changed
- Browser launch argument construction now uses raw `ProcessStartInfo.ArgumentList` values and only quotes the human-readable preview.
- README and roadmap now reflect DPAPI token storage, extra-owner UI, trust metadata, launch sessions, and the new quality gate.

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
