# Roadmap

ROADMAP.md is actionable-only. Completed work is removed; blocked work lives in
Roadmap_Blocked.md.

## P2

1. **Local/source-aware extension development**
   - Add a WPF UI smoke-test harness.
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
