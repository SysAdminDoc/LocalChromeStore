# Roadmap

## Research findings — projects and ecosystems to learn from

Research date: 2026-04-25. Goal: make LocalChromeStore more capable than a simple GitHub release browser by turning it into a serious private extension catalog, install orchestrator, trust surface, and developer workflow hub.

### 1. Extension build frameworks should become first-class discovery signals

Projects researched:

- [WXT](https://wxt.dev/) / [wxt-dev/wxt](https://github.com/wxt-dev/wxt) — modern open-source WebExtension framework with multi-browser output, MV2/MV3 builds, fast dev mode, file-based entrypoints, zip/publish tooling, and browser startup/profile support.
- [Plasmo](https://docs.plasmo.com/) / [PlasmoHQ/plasmo](https://github.com/PlasmoHQ/plasmo) — browser extension SDK with TypeScript/React-oriented workflow, live reload/HMR, storage/messaging helpers, and publishing tooling.
- [Extension.js](https://extension.js.org/) — cross-browser extension framework with browser-specific manifest compilation, predictable target outputs, reload behavior, packaging, Playwright E2E guidance, and security/performance workflow docs.
- [Mozilla web-ext](https://github.com/mozilla/web-ext) / [web-ext docs](https://extensionworkshop.com/documentation/develop/getting-started-with-web-ext/) — command-line tool for running, linting, building, and signing WebExtensions; useful even for Chromium-adjacent validation and packaging ideas.
- [Google Chrome extension samples](https://github.com/GoogleChrome/chrome-extensions-samples) — official sample corpus organized by APIs and functional extension examples.

What to add to LocalChromeStore:

- [ ] Detect project type: WXT, Plasmo, Extension.js, web-ext, plain MV3, plain MV2, packed release only, unpacked local folder.
- [ ] Show framework badges on cards: `WXT`, `Plasmo`, `Extension.js`, `web-ext`, `MV3`, `MV2`, `CRX`, `ZIP`.
- [ ] Add a "Build output resolver" that can infer likely artifact directories: `.output/chrome-mv3`, `build/chrome-mv3-prod`, `dist`, `extension-dist`, `public`, `extension`, etc.
- [ ] Add per-repo build instructions capture: if no release asset exists but a known framework is detected, show the build command and expected output path rather than just "No release".
- [ ] Add an optional local framework build step for trusted repos: run configured `npm run build`, `pnpm build`, `wxt zip`, `plasmo build`, or `extension build`, then install the generated output.
- [ ] Add a "Sample mode" using Google Chrome samples as known-good test fixtures for manifest parsing, permission display, icon discovery, and install smoke tests.

Why it matters:

LocalChromeStore should not only consume finished ZIP/CRX releases. It should understand how extension developers actually build projects and bridge the gap from source repo to runnable local extension.

### 2. The install/update path should be modeled on browser-native self-hosting

Projects/docs researched:

- [Chrome self-hosting and update manifest docs](https://developer.chrome.com/docs/extensions/how-to/distribute/host-on-linux) — documents `update_url`, CRX hosting, update manifest XML, extension ID, codebase, version, and same-key update requirements.
- [Microsoft Edge external auto-update docs](https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/update/auto-update) — Edge uses the same update manifest pattern for externally installed extensions.
- [Microsoft Edge ExtensionInstallForcelist policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/extensioninstallforcelist) — policy installs silently, grants permissions implicitly, and users cannot remove force-installed extensions.
- [Chrome Enterprise ExtensionInstallForcelist policy](https://chromeenterprise.google/policies/extension-install-forcelist/) and [Chrome Windows registry policy docs](https://support.google.com/chrome/a/answer/9131254?hl=en-EN) — confirm the policy/registry direction already planned for v0.2.
- [ahwayakchih/crx3](https://github.com/ahwayakchih/crx3) / [crx3 npm package](https://www.npmjs.com/package/crx3) — Node CRX3 packaging library for Chromium-family browsers.
- [crx3 PyPI](https://pypi.org/project/crx3/) — Python CRX packaging/parsing library with private key creation support.

What to add to LocalChromeStore:

- [ ] Treat Enterprise Policy mode as a separate install backend with a visible safety model, not just a setting.
- [ ] Generate a deterministic extension ID from the signing key and show it before install.
- [ ] Validate same-key update compatibility before replacing any policy-managed extension.
- [ ] Generate static `updates.xml` with one `<app>` entry per extension, and a combined catalog XML for multi-extension hosting.
- [ ] Support two hosting modes: GitHub Pages static hosting and local loopback hosting for private/internal testing.
- [ ] Add "Policy health" checks: registry path exists, extension ID present, update URL reachable, XML parses, CRX URL reachable, CRX signature/key matches, installed browser reports policy.
- [ ] Add a rollback path: remove registry policy entry, preserve CRX/update XML artifacts, and optionally reinstall as `Load unpacked`.
- [ ] Add explicit warning copy for policy mode: force-installed extensions cannot be disabled by normal browser UI and permissions are granted implicitly.

Why it matters:

This is the path that turns the app from a developer convenience wrapper into a real private extension store. The update manifest and signing model need to be treated as product-critical trust infrastructure.

### 3. Extension manager UX should borrow profiles, grouping, and curation

Projects researched:

- [Extensity](https://github.com/sergiokas/Extensity) — open-source Chrome extension manager focused on quickly enabling/disabling extensions, keeping the browser lean, profile switching, always-enabled extensions, and cloud storage sync.
- [SimpleExtManager ecosystem references](https://www.makeuseof.com/tag/simpleextmanager-extension-manager-for-chrome/) — common extension-manager pattern around fast enable/disable, grouping, extension options/details access, and quick uninstall.

What to add to LocalChromeStore:

- [ ] Profiles / load sets: `Work`, `Testing`, `Debug`, `Minimal`, `All installed`.
- [ ] Per-profile extension selection and launch command generation.
- [ ] "Always include" extensions for every launched browser session.
- [ ] Drag-and-drop order inside each profile, because `--load-extension` order can matter.
- [ ] Per-card quick actions: pin, hide, add to profile, remove from profile, open options page if detected.
- [ ] Global actions: launch selected profile, duplicate profile, export profile, import profile.
- [ ] Browser-session presets: Chrome stable, Chrome for Testing, Edge, Brave, clean temporary profile, persistent test profile.

Why it matters:

Developers do not just install extensions one by one; they switch between project sets, test profiles, and browser targets. Profiles would make LocalChromeStore feel like an extension workstation rather than a static catalog.

### 4. Package-manager and registry projects suggest stronger catalog governance

Projects researched:

- [Eclipse Open VSX](https://github.com/eclipse/openvsx) — open-source extension registry with server, web UI, and CLI publisher; useful reference for a vendor-neutral extension marketplace architecture.
- [Open VSX public registry/community model](https://www.eclipse.org/community/eclipse_newsletter/2020/march/1.php) — shows the value of a vendor-neutral registry plus publisher workflow.
- [Windows Package Manager repository submission process](https://learn.microsoft.com/en-us/windows/package-manager/package/repository) — validates manifests and checks packages before they are discoverable by winget.
- [WinGet manifest authoring docs](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest) — useful model for declarative package metadata and validation.

What to add to LocalChromeStore:

- [ ] Optional `localchromestore.json` catalog manifest in each extension repo for richer metadata: display name, channel, homepage, release asset pattern, build command, output dir, trust notes, screenshots, tags, supported browsers.
- [ ] Catalog validation before install: manifest schema, release asset checks, SHA256 checksums when available, version consistency, icon validity, update URL sanity.
- [ ] Publisher/source trust tiers: local repo, configured owner, signed release, verified GitHub release, policy-ready CRX, unknown ZIP.
- [ ] "Why is this shown?" panel explaining which discovery rule matched: release asset, manifest path, topic, local source, custom feed.
- [ ] Moderation-like local curation: hidden repos, pinned repos, favorites, allowlist/denylist, stale repo warnings.
- [ ] Machine-readable export of the whole catalog so LocalChromeStore can become a source for another machine or CI job.

Why it matters:

A league-of-its-own version should feel governed and explainable. Every card should make clear where it came from, why it is trusted, how it updates, and what metadata is missing.

### 5. Trust and security should become a visible product surface

Projects/docs researched:

- [Chrome Web Store best practices](https://developer.chrome.com/docs/webstore/best_practices) — emphasizes Manifest V3, compliance, security, privacy, performance, and testing.
- [Chrome MV3 security migration guidance](https://developer.chrome.com/docs/extensions/develop/migrate/improve-security) — highlights removing arbitrary string execution, banning remotely hosted code, and tightening CSP.
- [Chrome permission warning guidance](https://developer.chrome.com/docs/extensions/mv2/permission-warnings) — useful for permission-risk explanations even though the linked page is MV2-era and should be mapped to current MV3 semantics.
- [Chrome management API docs](https://developer.chrome.com/docs/extensions/reference/api/management) — shows the browser-native concepts users expect around managing installed apps/extensions.
- [Duo CRXcavator writeup](https://duo.com/resources/infographics/chrome-extension-security-crxcavator) and [ExtensionTotal](https://www.extensiontotal.com/chrome) — examples of extension risk scoring and security posture reporting.
- [palant/chrome-extension-manifests-dataset](https://github.com/palant/chrome-extension-manifests-dataset) — manifest corpus useful for permission/CSP/query rule ideas and benchmark data.

What to add to LocalChromeStore:

- [ ] Manifest risk panel: permissions, host permissions, optional permissions, externally connectable, content scripts, CSP, background/service worker type, web accessible resources, update URL, minimum Chrome version.
- [ ] Permission diff on update: new permissions, removed permissions, broader host patterns, CSP weakening, newly added remote endpoints.
- [ ] Trust summary badge: `Low risk`, `Review`, `High risk`, based on transparent heuristics rather than opaque scoring.
- [ ] "Why risky?" explanations with actionable language: broad host access, remote code patterns, MV2, unsafe-eval, all_urls, debugger/proxy/downloads/history/tabs permissions.
- [ ] Security regression gate before install/update when risk increases; allow user override with a clear confirmation.
- [ ] Source freshness checks: last release date, last commit date, open security advisories if GitHub exposes them, archived repo status, license presence.
- [ ] Local-only privacy posture: make clear that PAT and installed-extension state remain in local app data unless the user explicitly publishes artifacts.

Why it matters:

The app is installing privileged browser code. Premium polish here is not just visuals; it is giving users confidence, context, and a way to avoid quietly accepting dangerous extension changes.

### 6. Publishing and release automation should be built in, not outsourced mentally

Projects researched:

- [fregante/chrome-webstore-upload-cli](https://github.com/fregante/chrome-webstore-upload-cli) — CLI wrapper for uploading/publishing Chrome Web Store extensions.
- [Plasmo Browser Platform Publisher / BPP](https://github.com/PlasmoHQ/bpp) — multi-store browser extension publisher.
- [Plasmo docs](https://docs.plasmo.com/) — frames extension creation, testing, beta distribution, and publishing as one lifecycle.

What to add to LocalChromeStore:

- [ ] Release readiness checklist per repo: manifest version, version bump, icon set, permissions summary, changelog, ZIP/CRX asset present, checksum present.
- [ ] GitHub release helper: package output, calculate SHA256, create draft release, upload ZIP/CRX/update XML.
- [ ] Optional multi-store publishing links/integrations: Chrome Web Store, Edge Add-ons, Firefox AMO, without making public store publishing mandatory.
- [ ] "Dogfood channel" concept: install from latest successful GitHub Actions artifact, prerelease, stable release, or local build.
- [ ] Staged rollout notes: support beta/test channels before promoting to stable.

Why it matters:

The strongest version of this project owns the whole private-extension lifecycle: discover, build, validate, package, install, update, publish, and rollback.

### 7. Developer workflow should support live test sessions

Projects/docs researched:

- [web-ext run](https://extensionworkshop.com/documentation/develop/getting-started-with-web-ext/) — starts a browser session, disables auto-updates, and supports extension reload workflows.
- [WXT browser startup docs](https://wxt.dev/guide/essentials/config/browser-startup.html) — persistent browser profiles and startup configuration for extension development.
- [Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/) — automation surface for browser/debug workflows; powerful but should be used carefully.
- [Chrome extension DevTools extension docs](https://developer.chrome.com/docs/extensions/how-to/devtools/extend-devtools) — reference for extension-specific debugging surfaces.

What to add to LocalChromeStore:

- [ ] Launch with clean temporary profile, persistent per-project profile, or selected existing profile.
- [ ] Launch with test URL(s) after browser start.
- [ ] Optional file watcher for local unpacked folders that prompts or triggers session reload.
- [ ] Capture browser stderr/stdout and extension load errors into LocalChromeStore activity log.
- [ ] Add a "debug session" panel with browser target, profile path, extension paths, launch args, and quick open to `chrome://extensions`.
- [ ] Add Chrome for Testing detection as a safer automation target separate from the user's daily browser.

Why it matters:

The current launch flow is useful, but a premium developer tool should reduce the entire edit-build-launch-debug loop, not just open a browser with extensions loaded.

### 8. Competitive positioning: what would make this product feel unique

- A private extension catalog that understands GitHub, local folders, framework outputs, custom feeds, and policy-managed installs.
- Store-like browsing with package-manager-grade explainability: source, trust tier, risk, version, update channel, install mode.
- First-class extension profiles for repeatable browser sessions.
- Policy install/update infrastructure that makes self-hosted CRX deployment approachable on Windows.
- Permission and manifest diffs that make extension updates safer than a blind reinstall.
- Integrated release readiness and GitHub release publishing so developers can dogfood their own extensions quickly.
- Export/importable machine manifests so a fresh Windows machine can recreate the same extension development environment.

## v0.2.0 — Enterprise Policy auto-install (the "real store" mode)

The reason this exists at all. Stock Chromium browsers reject self-signed CRX install via drag-drop with `CRX_REQUIRED_PROOF_MISSING`, but they happily honor `ExtensionInstallForcelist` policy. v0.2.0 wraps that path.

- [ ] Toggle in Settings: **Install mode** = `Load unpacked` (current) or `Enterprise Policy`
- [ ] Per-browser registry policy writer:
  - `HKLM\Software\Policies\Google\Chrome\ExtensionInstallForcelist`
  - `HKLM\Software\Policies\BraveSoftware\Brave\ExtensionInstallForcelist`
  - `HKLM\Software\Policies\Microsoft\Edge\ExtensionInstallForcelist`
  - Vivaldi, Chromium, Opera as available
- [ ] CRX3 self-signing pipeline (per-project `selfhost.pem`, ID derived from key)
- [ ] Self-hosted `updates.xml` generator (per-extension, per-version)
- [ ] Optional GitHub Pages publish flow so each extension's update.xml has a stable HTTPS URL
- [ ] Local fallback: serve update.xml + CRX from `127.0.0.1:<port>` if user opts out of public hosting
- [ ] Elevation prompt when writing HKLM (UAC); document a one-time install run
- [ ] Migration helper: convert existing `Load unpacked` installs to Policy installs

## v0.3.0 — Update intelligence

- [ ] On Refresh, compare installed version vs. latest release tag — show **Update available** badge
- [ ] **Update all** button
- [ ] Auto-update toggle (per-extension or global) that runs on app launch
- [ ] Optional notification when a new release is published since last refresh

## v0.4.0 — Theming + UX polish

- [ ] Catppuccin Latte (light theme)
- [ ] Accent color picker (Mauve, Sapphire, Green, Pink, Peach)
- [ ] Per-card progress bar instead of percentage text during downloads
- [ ] Drag-and-drop reorder for the install order (matters for `--load-extension` precedence)
- [ ] Favorites / pin a card to the top
- [ ] Keyboard navigation (per project rules: no shortcuts; this is mouse only)

## v0.5.0 — Multi-source

- [ ] GUI editor for additional GitHub owners (`ExtraOwners`)
- [ ] GitHub org support with per-org token
- [ ] Local folder source: point at a directory of unpacked extensions and they show up alongside GitHub ones
- [ ] Custom update-feed source (`updates.xml` URL → list of tracked extensions)
- [ ] Cross-platform port? (.NET 9 lets us go cross-platform with Avalonia later if there's demand)

## v0.6.0 — Diagnostic / power-user

- [ ] Manifest viewer: open the `manifest.json` of an installed extension in the app
- [ ] Permissions diff: highlight new permissions when an extension updates
- [ ] Trust score: surface the source repo's open issues / star count / last commit / signing state
- [ ] Export the current set of installed extensions as a portable JSON manifest for a fresh machine
- [ ] Import flow: ingest the JSON manifest and bulk-install everything

## Backlog / maybes

- Firefox / LibreWolf support (different manifest, different load path — might split into its own app)
- Extension settings export (`chrome.storage.sync` snapshot via DevTools protocol — risky, deferred)
- GitHub Codespaces integration
- Microsoft Store packaging (MSIX)
