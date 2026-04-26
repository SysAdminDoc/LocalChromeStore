# Roadmap

Roadmap version: 2026-04-25-strategy-pass. This supersedes the earlier milestone-only roadmap while preserving its core direction: LocalChromeStore should stay a lightweight, local-first Windows tool, but grow from a simple release browser into a private Chromium extension workstation.

## State Of The Repo

LocalChromeStore today is a WPF / .NET 9 Windows desktop app for discovering Chromium extension repositories from GitHub, installing ZIP/CRX release assets into local app data, hiding noisy repos, and launching Chrome-family browsers with `--load-extension`. It uses MVVM without a third-party MVVM toolkit, Octokit 13.0.1 and `System.Security.Cryptography.ProtectedData` as runtime NuGet dependencies, JSON files under `%APPDATA%` for settings/install state, and `%LOCALAPPDATA%` for extracted extensions, icons, profiles, and logs.

What it claims and delivers:

- GitHub-sourced discovery for a primary owner plus JSON-backed extra owners.
- Optional topic filtering, optional DPAPI-protected GitHub PAT, release ZIP/CRX detection, and fallback `manifest.json` probing.
- Manifest enrichment for name, version, description, icon, MV2/MV3, permissions, host permissions, source shape, framework, freshness, update availability, and checksum sidecars.
- Install, update, update-all, uninstall, hidden-repo curation, installed-only filtering, browser detection, browser launch, temporary-profile sessions, startup URLs, copyable launch arguments, export/import environment manifests, settings drawer, and activity log.
- Framework-dependent Windows release workflow with ZIP plus SHA256 sidecar.
- Focused xUnit test project, Windows CI build/test workflow, and Dependabot coverage for NuGet and GitHub Actions.

What is incomplete or stubbed:

- Permission diff is now enforced for local release-asset updates and environment imports that resolve to broader current catalog releases, but exact historical-version restore and policy-hosted update safety remain open.
- Hidden repos are still restored as a single bulk action rather than through a per-repo management list.
- Enterprise policy install, CRX3 signing, update XML, and browser-native hosted auto-update are roadmap-only.
- Test coverage is still narrow: broader manifest/extraction/version/settings migration cases and UI smoke automation remain open.
- No local source folder source or named profile/load-set model.

Philosophy inferred from README, code, and docs:

- Personal developer/dogfood workflow first, not a public marketplace.
- Local-first and explainable: do not clutter the catalog, avoid unnecessary dependencies, keep async operations off the UI thread, and prefer native Windows behavior.
- Small, maintainable WPF architecture over broad framework churn.
- Trust and self-hosting matter because privileged browser code is being installed.

Hard constraints:

- Windows 10/11 desktop target, `net9.0-windows`, WPF, MIT license.
- Octokit is the only third-party runtime dependency today.
- Chromium CRX drag/drop install is no longer a reliable path on stock Windows Chrome-family browsers; enterprise policy plus update manifest is the serious self-host path.
- .NET 9 is STS and supported until November 2026, so an upgrade plan to .NET 10 LTS should be planned before the support window closes.

Local audit notes:

- Top-level docs inspected: `README.md`, `CHANGELOG.md`, existing `ROADMAP.md`, `docs/companion-prompts.md`, `.github/workflows/release.yml`, `CLAUDE.md`, `LICENSE`.
- Manifest inspected: `src/LocalChromeStore/LocalChromeStore.csproj`.
- Source markers: no source `TODO`, `FIXME`, `HACK`, `XXX`, `@deprecated`, or `NotImplementedException` markers outside documentation examples.
- GitHub issues for `SysAdminDoc/LocalChromeStore`: none returned.
- Last 200 commits inspected; repository has 7 commits as of this pass.
- Dependency audit: `dotnet list package --vulnerable --include-transitive` reported no vulnerable packages for the app and test project on 2026-04-25; an earlier outdated-package audit reported Octokit 14.0.0 available over 13.0.1.
- GitHub Dependabot config now exists; repository alert visibility still depends on GitHub-side settings and permissions.

## External Research Summary

### Direct OSS Competitors And Close Analogs

Stars and pushed dates are from GitHub metadata collected on 2026-04-25.

| Project | Stars | Last pushed | Maintainer / active owner | Why it matters |
| --- | ---: | --- | --- | --- |
| WXT | 9,661 | 2026-04-25 | `wxt-dev` | Sets the bar for extension framework detection, dev server UX, HMR, zipping, publishing, multi-browser targets, and persistent browser profiles. |
| Plasmo | 12,991 | 2026-04-24 | `PlasmoHQ` | Strong extension SDK model with React/TypeScript workflow, publishing lifecycle, storage/messaging abstractions, and community requests around source/build customization. |
| Extension.js | 4,976 | 2026-04-25 | `extension-js` | Zero-config cross-browser extension framework with browser profile support and recent browser/build-tool updates. |
| Mozilla web-ext | 3,061 | 2026-04-24 | Mozilla | Canonical run/build/lint/sign CLI pattern; useful reference for temporary profiles, run configuration, and extension reload workflows. |
| CRXJS Chrome Extension Tools | 4,049 | 2026-04-23 | `crxjs` | Vite-native build/HMR tooling and issue themes around mixed Chrome/Firefox output, CSS HMR, manifest schema/types, and build compatibility. |
| Google Chrome extension samples | 17,493 | 2026-04-17 | GoogleChrome | Official API sample corpus; useful as regression fixtures for manifest parsing, permission display, and API support coverage. |
| Extensity | 529 | 2024-08-31 | `sergiokas` | Extension manager UX reference: profiles, always-on extensions, favorites, search, options shortcuts, and sync. |
| Auto Extension Manager | 764 | 2026-02-15 | `JasonGrass` | Rules, grouping, keyboard/timer requests, i18n, list sorting, and auto enable/disable workflows. |
| One Click Extensions Manager | 252 | 2025-12-13 | `hankxdev` | Sticky extension ideas, translation coverage, Edge upload workflow, and lightweight extension-management UI. |
| Shoji Extension Admin | 1 | 2021-08-17 | `noxasch` | Low-signal but confirms community interest in open-source extension administration. |
| Browser Platform Publisher | 205 | 2025-02-12 | `PlasmoHQ` | Multi-store publishing automation, trusted tester targeting, Firefox source-code submission. |
| chrome-webstore-upload-cli | 489 | 2026-04-16 | `fregante` | Store upload/publish CLI; recent PRs emphasize env-only secrets, CRX input support, retrying in-progress publishing, and Node 20+. |
| Eclipse Open VSX | 1,909 | 2026-04-24 | Eclipse Open VSX | Open extension registry model: publisher namespaces, signed package verification, token scope, storage backends, statistics, search, migration. |
| Windows Package Manager package repository | 10,518 | 2026-04-25 | Microsoft | Strong manifest validation, submission review, package metadata, and checksum expectations. |
| Obtainium | 16,696 | 2026-04-16 | `ImranR98` | Adjacent "install from source" model: many sources, exports, prereleases, local network storage, proxy support, parallel download controls, per-source behavior. |

### Commercial / Closed-Source Signals

| Product or platform | Signal to steal | Source IDs |
| --- | --- | --- |
| Chrome Web Store / Developer Dashboard | Package validation, publisher identity, listing metrics, staged publish percentage, review constraints, store readiness checklist. | S32, S33, S84 |
| Microsoft Edge Add-ons / Partner Center | Submission workflow, visibility controls, markets, package validation, testing notes. | S34 |
| Chrome Enterprise Core | Extension requests, permissions governance, fleet reporting, cloud-managed policy. | S57, S58, S59 |
| Edge / Intune policy management | ExtensionInstallForcelist, ExtensionSettings, `override_update_url`, toolbar state, user inability to uninstall force-installed extensions. | S19, S20, S21, S73, S74 |
| ExtensionTotal | Risk scoring, malicious/risky/vulnerable/non-compliant detection positioning. | S54 |
| LayerX | Extension inventory, permission/developer reputation analysis, risk scoring, adaptive enforcement. | S56 |
| Spin.AI | Chrome extension risk assessment integrated with Chrome Browser Cloud Management. | S60 |
| IronCrux Shield | Permission pivot detection, developer-mode extension flagging, risk explanations. | S61 |
| ChromeStats / Chrome Analytics | Ecosystem analytics, MV3 migration tracking, publisher identity changes, installs/ratings/release metadata. | S62, S63 |

### Community Signals

Recurring user complaints and requests:

- Extension managers are valuable when they support profiles, groups, batch enable/disable, "always on" sets, details/options shortcuts, and quick state switches.
- Users want conditional enable/disable by site, timers, hotkeys, sort order, and visual grouping.
- Enterprises struggle with Edge/Chrome extension policy composition, Intune assignment combinations, self-hosted update URLs, and force-install behavior.
- Extension developers repeatedly hit MV3 remote-hosted-code rejections and CSP confusion.
- Security practitioners want maintained malicious-extension data, permission-change alerts, and explainable risk scoring.
- Developers want reproducible browser sessions, persistent dev profiles, Chrome for Testing, and CDP-backed debugging.

## Feature Harvest And Prioritization

Scoring: Impact, Effort, and Risk are 1 low to 5 high. "Prevalence" is `rare`, `emerging`, `common`, or `table-stakes`.

| ID | Candidate | Signal | Category | Prevalence | Fit | I/E/R | Depends | Novelty | Tier | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F001 | Framework detector for WXT, Plasmo, Extension.js, CRXJS, web-ext, plain MV3/MV2. | S01-S08, S76, S79 | dev-experience | common | Yes | 5/2/1 | Repo file probes | parity | Now | Discovery should explain source projects, not only release assets. |
| F002 | Framework badges on cards. | S01-S08 | UX | common | Yes | 4/1/1 | F001 | parity | Now | Badges make the catalog scannable and trustworthy. |
| F003 | "Why is this shown?" discovery explanation panel. | S37-S40, S41 | UX, trust | emerging | Yes | 5/2/1 | F001 | leapfrog | Now | Explainability is a strong differentiator for a private store. |
| F004 | Optional `localchromestore.json` repo manifest with schema. | S37-S40, S41 | data, plugin ecosystem | common | Yes | 5/3/2 | Schema validation | leapfrog | Now | Declarative metadata unlocks richer catalog governance without a server. |
| F005 | Catalog manifest validator. | S39, S40 | reliability | table-stakes | Yes | 5/3/2 | F004 | parity | Now | Package-manager-grade validation prevents broken cards and bad installs. |
| F006 | Release asset checksum verification using SHA256 sidecars. | S39, S40, S32 | security | table-stakes | Yes | 5/2/2 | Download pipeline | parity | Now | The release workflow already emits sidecars; install should consume them. |
| F007 | Source trust tier badge: local repo, configured owner, verified release, signed CRX, unknown ZIP. | S54-S61 | security | common | Yes | 5/3/2 | F004, F006 | leapfrog | Now | Users need a clear trust model before installing privileged browser code. |
| F008 | DPAPI protection for GitHub PAT. | S50, local audit | security | table-stakes | Yes | 5/2/1 | Settings migration | parity | Now | Hidden UI is not enough; local token persistence should be protected. |
| F009 | Permission/manifest risk panel. | S22-S31, S52-S56, S64-S70 | security | common | Yes | 5/3/3 | Manifest parser | leapfrog | Now | Installing extensions without permission context is a trust gap. |
| F010 | Permission diff before update. | S54-S61, S70 | security | emerging | Yes | 5/3/3 | Installed manifest snapshots | leapfrog | Now | Update safety is more valuable than first-install scanning. |
| F011 | Update-available badges from installed vs latest release/manifest version. | S15, S20, S41 | UX, reliability | table-stakes | Yes | 5/2/2 | Version comparison | parity | Now | Existing roadmap and settings already point here. |
| F012 | Manual "Update" and "Update all". | S41, S15, S20 | UX | table-stakes | Yes | 5/3/2 | F011, F010 | parity | Now | A store without update actions feels incomplete. |
| F013 | Profiles / load sets. | S10-S13, S71, S72 | UX | common | Yes | 5/3/2 | Installed set model | parity | Now | Reusable browser sessions are core to developer workflow. |
| F014 | Always-include extensions per profile. | S10, S12 | UX | common | Yes | 4/2/1 | F013 | parity | Next | Useful after profiles land. |
| F015 | Drag-and-drop install/load order. | S12, S72 | UX | emerging | Yes | 3/3/2 | F013 | parity | Next | Order can matter, but not before profiles exist. |
| F016 | Clean temporary browser profile launch. | S07, S82, S83 | dev-experience | common | Yes | 5/3/2 | BrowserLauncher refactor | parity | Now | Keeps testing separate from the user's daily browser state. |
| F017 | Persistent per-project browser profile launch. | S82, S85 | dev-experience | common | Yes | 5/3/2 | F013, F016 | parity | Next | Mirrors WXT/web-ext workflows after base profiles exist. |
| F018 | Chrome for Testing detection/download. | S83, S86 | testing | emerging | Yes | 4/3/2 | Browser source model | leapfrog | Next | Reproducible browser binaries improve extension QA. |
| F019 | Launch with test URLs. | S07, S82, S81 | dev-experience | common | Yes | 4/2/1 | BrowserLauncher refactor | parity | Now | Reduces the edit-build-launch-debug loop. |
| F020 | Launch argument preview/copy. | S07, S81, S82 | docs, UX | common | Yes | 4/1/1 | BrowserLauncher refactor | parity | Now | Makes behavior auditable and easier to debug. |
| F021 | Debug session panel with browser path, profile path, loaded extensions, target URL, args. | S81, S82, S83 | observability | emerging | Yes | 5/2/1 | F016-F020 | leapfrog | Next | Turns launch into a transparent session object. |
| F022 | Capture browser stdout/stderr and policy/load errors into activity log. | S81, S85 | observability | common | Yes | 4/3/2 | Process tracking | parity | Next | Failure diagnosis should not require hunting in Chrome UI. |
| F023 | Local source folder source. | S01-S08, S41 | platform/OS | common | Yes | 5/3/2 | Source abstraction | parity | Next | Not every extension has a release asset yet. |
| F024 | Build output resolver for `.output/chrome-mv3`, `build/chrome-mv3-prod`, `dist`, `extension`, `public`. | S01-S08, S79 | dev-experience | common | Yes | 5/3/2 | F001, F023 | leapfrog | Next | Bridges source repos to runnable artifacts. |
| F025 | Trusted local build command runner. | S01-S08, S79 | dev-experience | common | Yes | 4/4/4 | F004, sandbox warnings | Under Consideration | Powerful but risks arbitrary command execution. |
| F026 | Build command dry-run/checklist without executing. | S01-S08 | docs | common | Yes | 4/2/1 | F001, F004 | parity | Now | Gives guidance without adding execution risk. |
| F027 | CRX3 signer/packager integration. | S15, S20, S87-S90 | distribution/packaging | common | Yes | 5/4/3 | Key store design | Next | Required for policy mode and stable extension IDs. |
| F028 | Deterministic extension ID preview from signing key. | S15, S20, S87 | security | table-stakes | Yes | 5/3/3 | F027 | parity | Next | Prevents surprise ID changes and broken updates. |
| F029 | Same-key update compatibility check. | S15, S20, S87 | security | table-stakes | Yes | 5/3/3 | F027, F028 | parity | Next | Browser update rules require same private key. |
| F030 | `updates.xml` generator. | S15, S20, S19 | distribution/packaging | table-stakes | Yes | 5/3/2 | F027-F029 | parity | Next | This is the core self-hosted update primitive. |
| F031 | GitHub Pages static update hosting helper. | S15, S20, S32, S35 | distribution/packaging | common | Yes | 4/4/3 | F030, GitHub release helper | parity | Later | Valuable after local policy mode is proven. |
| F032 | Local loopback update server. | S15, S20, S73 | offline/resilience | rare | Partial | 3/4/4 | F030, firewall handling | Under Consideration | Useful for private testing but fragile for browser policy. |
| F033 | Enterprise Policy install backend. | S17-S21, S73, S74 | platform/OS | table-stakes | Yes | 5/5/4 | F027-F030 | Next | The app's "real store" mode depends on policy install. |
| F034 | Policy health checks: registry, ID, update URL, XML, CRX reachability, browser policy page. | S17-S21, S73, S74 | reliability | table-stakes | Yes | 5/4/3 | F033 | leapfrog | Next | Policy mode must be diagnosable, not magical. |
| F035 | Policy rollback path to remove force-install entry and preserve artifacts. | S19, S21 | reliability | table-stakes | Yes | 5/3/3 | F033 | parity | Next | Force-installed extensions cannot be treated like normal installs. |
| F036 | Explicit policy-mode warning and consent copy. | S19, S21 | UX, security | table-stakes | Yes | 5/1/1 | F033 | parity | Next | Users must understand force install and implicit permissions. |
| F037 | ExtensionSettings support for `override_update_url`, toolbar state, blocked hosts. | S18, S21 | platform/OS | emerging | Partial | 4/4/4 | F033 | Later | Powerful for managed use but risks enterprise complexity. |
| F038 | Import/export portable LocalChromeStore environment manifest. | S41, S39, S40 | migration | common | Yes | 5/3/2 | Data schema | Now | Fresh Windows setup is a core use case. |
| F039 | Export machine-readable catalog JSON for CI/other apps. | S37-S41 | integrations | common | Yes | 4/2/1 | F004 | Next | Makes LocalChromeStore useful beyond the GUI. |
| F040 | Import from another machine with missing-source diagnostics. | S41, S39 | migration | common | Yes | 4/3/2 | F038 | Next | Completes the portable environment story. |
| F041 | Multi-owner GUI editor. | S41 | UX | common | Yes | 4/2/1 | Existing `ExtraOwners` | Now | Backed by existing settings but missing UI. |
| F042 | Custom update feed source. | S15, S20 | integrations | common | Yes | 4/3/3 | Source abstraction | Later | Useful after update XML parsing exists. |
| F043 | Local catalog file source. | S39-S41 | offline/resilience | common | Yes | 4/3/2 | F004, F038 | Next | Supports offline/private catalogs. |
| F044 | Pinned/favorite repos. | S10, S12 | UX | common | Yes | 3/2/1 | Card state | Next | Helpful but lower priority than profiles and updates. |
| F045 | Hidden repo management list with per-repo restore. | S37-S41 | UX | common | Yes | 3/2/1 | Existing hidden repos | Next | Current restore-all behavior is too blunt. |
| F046 | Stale repo warnings: archived, no recent commit, no recent release. | S54-S63 | trust | common | Yes | 4/2/1 | Repo metadata | Now | Low-cost trust context from GitHub metadata. |
| F047 | License presence and type badge. | S37-S40 | trust, licensing | common | Yes | 3/2/1 | Repo metadata | Next | Helps filter private/open-source catalog quality. |
| F048 | Release channel selector: stable, prerelease, latest Actions artifact, local build. | S35, S36, S41 | distribution/packaging | emerging | Yes | 5/4/3 | Source model | Later | High value but depends on update/source abstractions. |
| F049 | Release readiness checklist. | S31-S36 | distribution/packaging | common | Yes | 4/3/2 | F004, F006, F009 | Next | Helps extension authors publish better artifacts. |
| F050 | GitHub draft release helper. | S35, S36, S84 | distribution/packaging | common | Yes | 4/4/3 | F049, Octokit upgrade | Later | Useful but should follow install safety foundations. |
| F051 | Chrome Web Store upload/publish integration. | S32, S35, S84 | integrations | common | Partial | 3/4/4 | Secret handling, OAuth | Under Consideration | Valuable for public publishing, but not core private-store scope. |
| F052 | Multi-store publishing integration. | S34-S36 | integrations | common | Partial | 3/5/4 | F051 | Under Consideration | BPP already handles this; LocalChromeStore should not duplicate too early. |
| F053 | Store metrics ingestion. | S33, S62, S63 | telemetry | common | Partial | 2/4/3 | External credentials | Later | Useful for public extensions, not core dogfood install. |
| F054 | Basic analytics-free local usage stats. | S57, S62, local philosophy | telemetry | emerging | Yes | 3/2/1 | Local event log | Later | Keep local-only; no external telemetry by default. |
| F055 | Opt-in external telemetry. | S57 | telemetry | common | No - contradicts local-first unless explicitly requested | 1/3/4 | Consent system | Rejected | The project philosophy favors local state, not product telemetry. |
| F056 | Manifest MV2/MV3 compatibility badge. | S24, S63, S65 | platform/OS | table-stakes | Yes | 4/1/1 | Manifest parser | Now | MV3 status is a central extension-platform health signal. |
| F057 | Remote-hosted-code/CSP checks. | S24, S25, S75, S80 | security | table-stakes | Yes | 5/3/3 | Manifest + package scan | Next | Developers repeatedly hit this rejection and security risk. |
| F058 | High-risk permission explanations. | S22, S23, S58, S54-S61 | security | common | Yes | 5/2/2 | F009 | Now | Risk scoring must be transparent, not opaque. |
| F059 | Optional permissions and host permissions summary. | S22, S23, S58 | security | table-stakes | Yes | 5/2/2 | F009 | Now | Host access is one of the most important review signals. |
| F060 | Permission increase install/update gate. | S54-S61, S70 | security | emerging | Yes | 5/3/3 | F010, F058 | Next | Blocks quiet privilege escalation while still allowing override. |
| F061 | Malicious extension feed cross-check. | S54-S56, S69, S71 | security | emerging | Partial | 4/4/4 | Feed quality policy | Under Consideration | Useful if source/license/maintenance quality is acceptable. |
| F062 | Static code scanner for obfuscation, eval, remote imports, secret leakage. | S64, S66, S70 | security | emerging | Yes | 4/5/4 | Package scanner | Later | High value but much larger than manifest analysis. |
| F063 | Local LLM/security sandbox analysis. | S64 | security | rare | Partial | 2/5/5 | ML/runtime infra | Rejected | Too heavy and outside the current minimal dependency philosophy. |
| F064 | CDP-driven extension reload/debug workflow. | S81, S85, S67 | dev-experience | emerging | Partial | 4/5/4 | Debug session model | Under Consideration | Powerful but CDP/debugger surfaces have security risk. |
| F065 | File watcher with manual reload prompt. | S01, S07, S08 | dev-experience | common | Yes | 4/3/2 | Local source folder | Later | Useful after local folder/source support exists. |
| F066 | DevTools/extension options quick links. | S10, S12, S30 | UX | common | Yes | 3/2/1 | Installed manifest + browser target | Next | Extension managers commonly expose options/details. |
| F067 | Open `chrome://extensions` / `edge://extensions` action. | S30, S81 | UX | table-stakes | Yes | 3/1/1 | Browser target | Now | Low-cost bridge to native browser management. |
| F068 | Browser policy page quick link. | S19, S21, S73 | support | common | Yes | 3/1/1 | Browser target | Next | Helpful once policy mode exists. |
| F069 | Activity log export/copy diagnostics bundle. | S57, S41 | observability | common | Yes | 4/2/1 | Existing log | Now | Makes troubleshooting shareable without adding telemetry. |
| F070 | Structured JSON event log. | S57, S41 | observability | common | Yes | 4/2/1 | Existing log | Next | Enables machine-readable diagnostics and future tests. |
| F071 | Download retry/resume and parallel-download limit. | S41 | performance | common | Yes | 4/3/2 | Download service | Next | Obtainium issues show source downloads need resilience controls. |
| F072 | GitHub rate-limit visibility. | S41, S48 | reliability | common | Yes | 4/2/1 | Octokit metadata | Now | PAT/rate-limit behavior should be visible before failures. |
| F073 | Proxy support. | S41 | integrations | common | Partial | 2/3/2 | HttpClient settings | Later | Useful in locked-down environments but not first-order. |
| F074 | Offline cache mode for last-known catalog and icons. | S41, S43 | offline/resilience | common | Yes | 4/3/2 | Catalog persistence | Next | The app should remain useful when GitHub is unavailable. |
| F075 | Graceful GitHub API degraded state. | S41, S57 | reliability | table-stakes | Yes | 4/2/1 | Existing logging | Now | Current app should distinguish auth, rate, network, and no-release states. |
| F076 | Accessibility pass: keyboard focus, screen reader labels, contrast audit, reduced motion. | S31, S57 | accessibility | table-stakes | Yes | 5/3/1 | UI audit | Now | Premium desktop quality includes accessible controls and states. |
| F077 | High contrast theme. | S31, S57 | accessibility | common | Yes | 3/3/1 | Theme tokens | Later | Important but behind core workflow and base accessibility. |
| F078 | Light theme and accent picker. | local roadmap | UX | common | Yes | 3/3/1 | Theme tokens | Later | Nice polish, lower impact than trust/update/profile work. |
| F079 | i18n-ready string resources. | S12, S13 | i18n | common | Yes | 3/3/1 | UI string extraction | Later | Extension managers show translation demand, but user base is personal-first. |
| F080 | Full localization packs. | S12, S13 | i18n | common | Partial | 2/4/2 | F079, translators | Under Consideration | Premature until strings are resource-backed. |
| F081 | Unit tests for manifest parsing, ZIP/CRX extraction, version compare, settings migration. | S09, S47, S50 | testing | table-stakes | Yes | 5/3/1 | Test project | Now | Risky code paths need automated guardrails. |
| F082 | UI smoke test harness for WPF launch. | S47, S50 | testing | common | Yes | 4/3/2 | Test infra | Next | Build has been smoke-tested manually; automate it. |
| F083 | Chrome extension sample fixture suite. | S09 | testing | common | Yes | 4/2/1 | Test project | Next | Official samples make good parser/permission fixtures. |
| F084 | CI build/test workflow on pull requests. | S51 | testing | table-stakes | Yes | 5/2/1 | Test project | Now | Release-only workflow is too late to catch regressions. |
| F085 | Dependabot and GitHub security scanning. | S48-S51, local audit | security | table-stakes | Yes | 4/1/1 | Repo settings/workflow | Now | Alerts are disabled/inaccessible today. |
| F086 | Upgrade Octokit 13.0.1 to 14.0.0 after compatibility check. | S48-S50 | dependencies | common | Yes | 3/2/2 | Tests | Next | Latest version moves IDs to Int64; verify before upgrading. |
| F087 | .NET 10 LTS migration plan. | S47, S50 | upgrade strategy | table-stakes | Yes | 4/3/2 | Tests | Next | .NET 9 support ends in November 2026. |
| F088 | MSIX package. | S44, S45 | distribution/packaging | common | Partial | 3/4/3 | Release workflow maturity | Later | Useful for Windows install polish but not needed for personal portable use. |
| F089 | Winget manifest export/submission helper. | S39, S40 | distribution/packaging | common | Partial | 3/3/2 | Stable releases | Later | Public distribution optional; export is useful for sibling desktop store. |
| F090 | Signed release artifacts / Authenticode. | S39, S40, S45 | security | common | Yes | 4/4/3 | Certificate strategy | Later | Trust improvement, but certificate logistics may be heavy. |
| F091 | Plugin system for custom source adapters. | S37, S41, S43 | plugin ecosystem | emerging | Partial | 3/5/4 | Stable core source interface | Under Consideration | Powerful but can bloat a personal app. |
| F092 | First-party source adapter interface without external plugins. | S37, S41 | plugin ecosystem | common | Yes | 5/3/2 | F023, F043 | Next | Gives extensibility while staying maintainable. |
| F093 | Multi-user/team catalog server. | S37, S57 | multi-user/collab | common | No - contradicts personal local-first scope today | 2/5/4 | Backend service | Rejected | A central service would change the product category. |
| F094 | Shared Git-backed catalog repo workflow. | S37-S41 | multi-user/collab | emerging | Yes | 3/3/2 | F004, F039 | Later | Lightweight collaboration without operating a server. |
| F095 | Mobile companion app. | S41-S43 | mobile | rare | No - separate product | 1/5/4 | New stack | Rejected | This repo is Windows Chromium-extension focused; mobile belongs in LocalAndroidStore. |
| F096 | Avalonia cross-platform port. | S46 | platform/OS | common | Partial | 2/5/4 | Stable Windows feature set | Later | Valuable only if Windows-first scope changes. |
| F097 | Browser-extension implementation of LocalChromeStore. | S10-S13, S30 | platform/OS | common | No - cannot manage local release assets/policy reliably | 1/5/4 | Browser extension limits | Rejected | A browser extension cannot replace native install/policy/file workflows. |
| F098 | Silent install outside enterprise policy. | S16-S21 | platform/OS | rare | No - conflicts with browser security model | 1/5/5 | None | Rejected | Stock browsers intentionally block this path. |
| F099 | Auto-accept publisher signing-key changes. | S15, S20, S87 | security | rare | No - unsafe | 1/1/5 | None | Rejected | Key changes must block or require explicit override. |
| F100 | Send source packages to third-party scanners by default. | S54-S61 | security | common | No - privacy risk | 2/3/5 | User consent | Rejected | Private extension code should not leave the machine by default. |

## Now

These items should land before the next public feature release because they improve clarity, safety, or already-backed settings.

1. **Catalog explainability and source metadata**
   - Implement F001, F002, F003, F004, F005, F026, F041, F046, F056, F067, F072, and F075.
   - Output: cards explain why a repo appears, what framework/artifact shape was detected, whether it is MV2/MV3, how fresh it is, and what metadata is missing.
   - Acceptance: no install behavior changes; discovery records are richer; settings include extra-owner editing; empty/error/rate-limit states are distinct.

2. **Trust baseline**
   - Implement F006, F007, F008, F009, F058, F059, and F069.
   - Output: checksum verification where sidecars exist, DPAPI-protected PAT migration, manifest risk panel, permission/host permission summary, and diagnostics export.
   - Acceptance: installs fail closed on checksum mismatch; existing plaintext token settings are migrated; risk panel is visible before install.

3. **Update and environment portability**
   - Implement F011, F012, F038, and F060 for the local release-asset path.
   - Output: update badges, manual update/update-all, export/import of installed extension environment, persisted manifest snapshots, and permission-diff approval before access-expanding updates.
   - Acceptance: installed version vs latest version is visible; export file can recreate the selected installed set on another machine; auto-update does not silently accept new extension access.
   - 2026-04-25 progress: F038 and F060 groundwork are implemented. Installed records now retain manifest/trust snapshots, and environment JSON export/import can carry installed extension targets, GitHub owner/topic settings, and launch options across machines. Exact historical-version restore remains open.
   - 2026-04-25 progress: F011 and F012 are implemented for the local release-asset path. Cards expose update availability, the toolbar has **Update all**, settings now expose launch-after-install and auto-update-on-refresh, and diagnostics include those workflow flags.
   - 2026-04-25 progress: F010/F060 are enforced for local release-asset updates. Card updates and manual **Update all** require approval when required permissions, optional permissions, host access, or optional host access expand; auto-update skips those updates for manual review.
   - 2026-04-25 progress: F038 import safety now uses the same permission-diff model. If a portable environment import resolves to a current catalog release with broader access than the exported snapshot or local installed copy, import requires approval before installing it.

4. **Profiles and better launch sessions**
   - Implement F013, F016, F019, F020.
   - Output: named load sets, temporary browser profile launches, optional launch URLs, launch args preview.
   - Acceptance: user can launch `All installed`, a custom profile, or a clean temporary test session without editing JSON.
   - 2026-04-25 progress: F016, F019, and F020 are implemented. Launch sessions now support a clean temporary Chromium profile, optional startup URL, and copyable command preview. F013 named load sets remain open.
   - 2026-07-03 progress: F013 named load sets implemented. Toolbar load-set selector, settings drawer management panel (snapshot, list, per-item delete), `loadsets.json` persistence. **F013 done.**
   - 2026-07-03 progress: F045 per-repo hidden-repo restore also done here. **F045 done.**

5. **Engineering quality gate**
   - Implement F076, F081, F084, F085.
   - Output: accessibility sweep, unit tests for manifest/extraction/version/settings migration, PR build/test workflow, Dependabot/security scanning enabled.
   - Acceptance: `dotnet build`, unit tests, and CI gate pass; focus/keyboard/screen-reader labels are audited.
   - 2026-04-25 progress: F081, F084, and F085 groundwork are implemented with focused launch/checksum tests, a Windows CI build/test workflow, and Dependabot coverage for NuGet and GitHub Actions. F076 accessibility sweep and broader manifest/extraction/version/settings tests remain open.
   - 2026-07-03 progress: F076 accessibility sweep done (AutomationProperties.Name on all interactive controls). F081 broader tests done (PermissionCatalogTests + LoadSetSerializationTests, 56 tests total). **F076 and F081 done.**

## Next

These become valuable once the Now foundation exists.

1. **Policy-ready packaging and install backend**
   - Implement F027, F028, F029, F030, F033, F034, F035, F036.
   - Reason: enterprise policy mode is the product's strongest technical differentiator, but it needs signing/update/preflight/rollback first.

2. **Local/source-aware extension development**
   - Implement F017, F018, F021, F022, F023, F024, F049, F066, F068, F070, F071, F074, F082, F083, F086, F087, F092.
   - Reason: LocalChromeStore should support source repos and developer sessions, but not before source metadata and tests exist.
   - Note: F045 per-repo hidden-repo restore moved to Now and completed in v0.2.0.

3. **Historical restore and policy update safety**
   - Finish exact historical-version restore for environment imports and carry permission-diff checks into future policy-hosted update flows.
   - Reason: local release-asset updates and current-release imports now block silent privilege expansion, but exact historical restores and policy mode need the same safety model.

## Later

These are directionally useful but should not distract from private-store, trust, and policy-mode fundamentals.

- F031 GitHub Pages update hosting.
- F037 advanced ExtensionSettings controls.
- F039 machine-readable catalog export.
- F040 import diagnostics.
- F042 custom update feed source.
- F043 local catalog file source.
- F044 favorites/pinned repos.
- F047 license badges.
- F048 release channels.
- F050 GitHub draft release helper.
- F053 store metrics ingestion.
- F054 local-only usage stats.
- F057 remote-hosted-code/CSP package scanner.
- F062 static package scanner.
- F065 file watcher and manual reload prompt.
- F073 proxy support.
- F077 high contrast theme.
- F078 light theme and accent picker.
- F079 i18n-ready string resources.
- F088 MSIX package.
- F089 Winget manifest export.
- F090 Authenticode signing.
- F094 shared Git-backed catalog workflow.
- F096 Avalonia cross-platform port.

## Under Consideration

- F025 trusted local build command runner: useful, but command execution needs explicit trust boundaries, dry-run mode first, and clear logs.
- F032 local loopback update server: useful for private testing but fragile around policy, firewall, browser trust, and HTTPS expectations.
- F051 Chrome Web Store publishing integration and F052 multi-store publishing: valuable only if LocalChromeStore grows into a release workstation; existing tools may be better integrations than reimplementation.
- F061 malicious-extension feed cross-check: only acceptable with transparent data provenance and no private code upload.
- F064 CDP-driven reload/debug workflow: powerful, but CDP/debugger surfaces are security-sensitive.
- F080 localization packs: string resources first, translation later.
- F091 external plugin system: source adapter interface first; plugin execution later only if there is a real use case.

## Rejected

- F055 opt-in external telemetry by default path: conflicts with local-first product philosophy unless a future user explicitly asks for analytics.
- F063 local LLM/security sandbox analysis: too heavy for this minimal WPF tool.
- F093 multi-user/team catalog server: changes the product from personal workstation to hosted platform.
- F095 mobile companion app: belongs in sibling projects, not this Windows repo.
- F097 browser-extension implementation: cannot own native local file, release, and policy workflows.
- F098 silent install outside enterprise policy: conflicts with browser security model.
- F099 auto-accept signing-key changes: unsafe.
- F100 default third-party source scanning: privacy risk for private extension code.

## Source Appendix

S01. https://wxt.dev/
S02. https://github.com/wxt-dev/wxt
S03. https://www.plasmo.com/
S04. https://github.com/PlasmoHQ/plasmo
S05. https://extension.js.org/
S06. https://github.com/mozilla/web-ext
S07. https://extensionworkshop.com/documentation/develop/getting-started-with-web-ext/
S08. https://github.com/crxjs/chrome-extension-tools
S09. https://github.com/GoogleChrome/chrome-extensions-samples
S10. https://github.com/sergiokas/Extensity
S11. https://chromewebstore.google.com/detail/simpleextmanager/kniehgiejgnnpgojkdhhjbgbllnfkfdk
S12. https://github.com/JasonGrass/auto-extension-manager
S13. https://github.com/hankxdev/one-click-extensions-manager
S14. https://github.com/noxasch/shoji-extension-admin
S15. https://developer.chrome.com/docs/extensions/how-to/distribute/host-on-linux
S16. https://developer.chrome.com/docs/extensions/mv2/hosting-changes
S17. https://chromeenterprise.google/policies/extension-install-forcelist/
S18. https://support.google.com/chrome/a/answer/9867568?hl=en-EN
S19. https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/extensioninstallforcelist
S20. https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/update/auto-update
S21. https://learn.microsoft.com/en-us/deployedge/microsoft-edge-manage-extensions-ref-guide
S22. https://developer.chrome.com/docs/extensions/reference/permissions-list
S23. https://developer.chrome.com/docs/extensions/reference/api/permissions
S24. https://developer.chrome.com/docs/extensions/develop/migrate/what-is-mv3
S25. https://developer.chrome.com/docs/extensions/develop/migrate/remote-hosted-code?hl=en
S26. https://developer.chrome.com/docs/extensions/reference/sidePanel/
S27. https://developer.chrome.com/docs/extensions/reference/api/offscreen
S28. https://developer.chrome.com/docs/extensions/reference/api/userScripts
S29. https://developer.chrome.com/blog/chrome-userscript
S30. https://developer.chrome.com/docs/extensions/reference/api/management
S31. https://developer.chrome.com/docs/webstore/best_practices
S32. https://developer.chrome.com/docs/webstore/publish/
S33. https://developer.chrome.com/docs/webstore/metrics/
S34. https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/publish/publish-extension
S35. https://github.com/fregante/chrome-webstore-upload-cli
S36. https://github.com/PlasmoHQ/bpp
S37. https://github.com/eclipse-openvsx/openvsx
S38. https://www.eclipse.org/community/eclipse_newsletter/2020/march/1.php
S39. https://learn.microsoft.com/en-us/windows/package-manager/package/repository
S40. https://learn.microsoft.com/en-us/windows/package-manager/package/manifest
S41. https://github.com/ImranR98/Obtainium
S42. https://github.com/f-droid/fdroidclient
S43. https://github.com/f-droid/fdroidserver
S44. https://github.com/electron-userland/electron-builder
S45. https://github.com/microsoft/PowerToys
S46. https://github.com/AvaloniaUI/Avalonia
S47. https://github.com/dotnet/wpf
S48. https://github.com/octokit/octokit.net
S49. https://www.nuget.org/packages/Octokit/13.0.1
S50. https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
S51. https://github.com/actions/setup-dotnet
S52. https://github.com/palant/chrome-extension-manifests-dataset
S53. https://duo.com/resources/infographics/chrome-extension-security-crxcavator
S54. https://www.extensiontotal.com/chrome
S55. https://extensionsecurity.io/
S56. https://layerxsecurity.com/use-cases/browser-extensions-protection/
S57. https://chromeenterprise.google/products/cloud-management/
S58. https://support.google.com/chrome/a/answer/7515036/chrome-app-and-extension-permissions?hl=en
S59. https://support.google.com/chrome/a/answer/10405494?hl=en
S60. https://www.businesswire.com/news/home/20230502005270/en/Spin.AI-Introduces-Chrome-Extension-Risk-Assessment-Integration-in-Partnership-with-Google-Chrome
S61. https://www.ironcrux.com/extension-protection
S62. https://chrome-analytics.com/extensions
S63. https://chrome-stats.com/manifest-v3-migration
S64. https://arxiv.org/abs/2505.21263
S65. https://arxiv.org/abs/2404.08310
S66. https://arxiv.org/abs/2505.19456
S67. https://arxiv.org/abs/2305.11506
S68. https://arxiv.org/abs/2406.12710
S69. https://arxiv.org/abs/2512.10029
S70. https://www.securitee.org/files/extensiondelta_ccs2020.pdf
S71. https://www.reddit.com/r/chrome_extensions/comments/1ofpd9x/i_built_an_extension_to_manage_all_other_chrome/
S72. https://www.reddit.com/r/chrome_extensions/comments/wda21a/extension_manager_extension_with_hotkey_timer_support/
S73. https://www.reddit.com/r/MicrosoftEdge/comments/1n7b6vz/a_way_to_force_install_from_certain_crx_extension/
S74. https://www.reddit.com/r/Intune/comments/1rddsn3/managing_chrome_andor_edge_extensions/
S75. https://www.reddit.com/r/chrome_extensions/comments/1q7djyu/my_chrome_extension_was_almost_removed_i_hit_a/
S76. https://github.com/fregante/Awesome-WebExtensions
S77. https://crxjs.dev/awesome/
S78. https://github.com/awesome-soft/awesome-chrome-extensions
S79. https://addfox.dev/
S80. https://stackoverflow.com/questions/26242682/unsafe-eval-on-chrome-extension
S81. https://chromedevtools.github.io/devtools-protocol/
S82. https://wxt.dev/guide/essentials/config/browser-startup.html
S83. https://developer.chrome.com/blog/chrome-for-testing
S84. https://developer.chrome.com/docs/webstore/using-api
S85. https://developer.chrome.com/docs/extensions/reference/api/debugger
S86. https://googlechromelabs.github.io/chrome-for-testing/
S87. https://chromium.googlesource.com/chromium/src/+/HEAD/components/crx_file/
S88. https://chromium.googlesource.com/chromium/src/+/refs/tags/131.0.6765.0/components/crx_file/crx3.proto
S89. https://www.npmjs.com/package/crx3
S90. https://pypi.org/project/crx3/
S91. https://www.nuget.org/packages/Octokit/14.0.0
S92. https://github.com/octokit/octokit.net/releases

## Self-Audit

- Every roadmap candidate includes at least one source ID and every source ID is listed in the appendix.
- Tier placements are justified in the feature table and summarized in Now/Next/Later/Under Consideration/Rejected.
- Required categories covered: security, accessibility, i18n/l10n, observability/telemetry, testing, docs, distribution/packaging, plugin ecosystem, mobile, offline/resilience, multi-user/collab, migration paths, and upgrade strategy.
- No duplicate accepted items intentionally appear across tiers; later phase summaries reference feature IDs rather than restating conflicting requirements.
- Rejected items are explicitly identified with one-line reasons.
- The roadmap is written to the repository root as `ROADMAP.md`.
