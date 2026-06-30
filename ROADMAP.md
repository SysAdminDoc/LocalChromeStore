# Roadmap

ROADMAP.md is actionable-only. Completed work is removed; blocked work lives in
Roadmap_Blocked.md.

## P2

1. **Local/source-aware extension development**
   - Add persistent per-project browser profile launches.
   - Detect and optionally download Chrome for Testing.
   - Add a debug session panel with browser path, profile path, loaded extensions, startup URL, and launch arguments.
   - Capture browser stdout/stderr and policy/load errors into the activity log.
   - Add a local source-folder extension source.
   - Resolve framework build outputs such as `.output/chrome-mv3`, `build/chrome-mv3-prod`, `dist`, `extension`, and `public`.
   - Add a release-readiness checklist.
   - Add DevTools/options quick links for installed extensions.
   - Add native browser policy-page quick links.
   - Add a structured JSON event log.
   - Add download retry/resume and a parallel-download limit.
   - Add offline cache mode for the last-known catalog and icons.
   - Add a WPF UI smoke-test harness.
   - Add Chrome extension sample fixtures for parser and permission regression tests.
   - Validate an Octokit 14.x upgrade.
   - Plan the .NET 10 LTS migration before .NET 9 support ends.
   - Add a first-party source adapter interface for future source types.

2. **Historical restore and policy update safety**
   - Restore exact historical versions during environment import when the source asset still exists.
   - Carry permission-diff checks into policy-hosted update flows.

## P3

1. **Later polish and integrations**
   - Add GitHub Pages static update hosting.
   - Add advanced `ExtensionSettings` controls.
   - Export machine-readable catalog JSON for other tools.
   - Add richer import diagnostics.
   - Add a custom update-feed source.
   - Add a local catalog-file source.
   - Add pinned/favorite repos.
   - Add license badges.
   - Add release channel selection.
   - Add a GitHub draft-release helper.
   - Add local-only usage stats.
   - Add remote-hosted-code/CSP package scanning.
   - Add a static package scanner for obfuscation, `eval`, remote imports, and secret leakage.
   - Add a file watcher with manual reload prompt.
   - Add proxy support.
   - Add a high-contrast theme.
   - Add a light theme and accent picker.
   - Move UI strings to resource files for future localization.
   - Add MSIX packaging.
   - Add Winget manifest export.
   - Add Authenticode signing when a certificate is available.
   - Add a shared Git-backed catalog workflow.
   - Revisit an Avalonia port only after the Windows feature set is stable.

## Research-Driven Additions

### P2

- [ ] P2 - Enrich release provenance in cards and diagnostics
  Why: Trust decisions need more than repo name and checksum state; GitHub exposes asset ID, content type, uploader, upload/update timestamps, size, digest, and download count.
  Evidence: GitHub release-assets REST docs; `ImportExportService.BuildCatalog`; `BuildDiagnosticsBundle`; Obtainium verification/troubleshooting model.
  Touches: `Models/ExtensionInfo.cs`, `Services/GitHubService.cs`, `Services/ImportExportService.cs`, `Views/ExtensionCardView.xaml`, `Views/ManifestRiskWindow.xaml`, diagnostics export.
  Acceptance: Cards/risk view/diagnostics show compact release provenance for installable assets, including upload timestamp, asset size, digest/checksum source, and whether the asset changed since last install.
  Complexity: M
