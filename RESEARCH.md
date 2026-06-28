# Research - LocalChromeStore

## Executive Summary
LocalChromeStore is a Windows WPF/.NET 9 personal catalog for Chromium extensions sourced from GitHub repos and releases. The v0.3.1 codebase already fixed the prior research pass's biggest risks: version-gated launch strategy, CDP pipe primitives, policy enrollment probing, CRX3 RSA packaging, self-update checks, organization private-repo discovery, semver comparison, atomic JSON state, and single-instance protection. The highest-value direction is now finishing the last mile: wire the existing Chrome CDP loader into the main launch path, turn the policy backend into a guided install workflow, use GitHub's release-asset digest as an integrity fallback, add a browser/platform conformance harness, and make policy installs fail closed on package-risk checks.

## Product Map
- Core workflows: discover GitHub repos with `manifest.json` or ZIP/CRX release assets; install/update/uninstall extension packages into `%LOCALAPPDATA%`; review permission, freshness, checksum, and source signals; launch a selected browser with the active installed set or load set; export/import environment and catalog snapshots.
- User personas: solo extension developer dogfooding many private/public extensions; Windows sysadmin packaging a controlled extension set for managed browsers; power user who wants a local, auditable alternative to repeated "Load unpacked" clicks.
- Platforms and distribution: Windows 10/11, WPF, `net9.0-windows`, framework-dependent ZIP release, MIT license, GitHub Releases with ZIP and SHA256 sidecar.
- Key integrations and data flows: Octokit/GitHub REST for repo/release/content/topic/rate-limit data; release asset downloads; raw icon caching; DPAPI-protected PAT in local JSON; CRX3/update.xml generation; HKLM browser policy registry keys; CDP pipe launch for branded Chrome; exported JSON diagnostics/environment/catalog files.

## Competitive Landscape
- Mozilla `web-ext`: already moved the browser-extension tooling discussion toward CDP `Extensions.loadUnpacked` because `--load-extension` is no longer reliable on Chrome. LocalChromeStore should learn from its CDP migration and avoid bundling a full automation framework.
- SeleniumBase, Cypress, and WebdriverIO: these projects show the same Chrome 137/142 breakage and the practical fallback ladder: Chrome for Testing, CDP, and WebDriver BiDi where available. LocalChromeStore should expose strategy and result details instead of treating browser launch as opaque.
- Chrome for Testing: provides current stable/beta/dev/canary binaries and JSON endpoints for automated download. This supports the existing roadmap item to detect/download CfT, but LocalChromeStore should keep it opt-in rather than shipping a browser.
- WXT, Plasmo, Extension.js, and CRXJS: extension frameworks standardize build output folders, persistent dev profiles, package commands, and target-specific builds. LocalChromeStore should keep resolving their output conventions and avoid becoming a bundler.
- Obtainium: closest product analogue in another domain: source-driven app updates, many source adapters, signature verification, update intervals, and troubleshooting around source fragility. LocalChromeStore should learn from its source-adapter and verification model while staying focused on Chromium extensions.
- Chrome Enterprise and Microsoft Edge policy tooling: policy force-install is the real managed-install path, but Edge's self-hosted update docs make `ExtensionSettings.override_update_url` and manifest/update.xml/CRX version alignment table-stakes. LocalChromeStore should avoid claiming policy install is complete until the UI writes and verifies the full policy set.
- MalExt Sentry and `chrome-mal-ids`: both provide machine-readable malicious-extension intelligence. LocalChromeStore can add offline-cacheable warnings without uploading private extension packages to third-party scanners.

## Security, Privacy, and Reliability
- Verified gap: `Services/Cdp/CdpExtensionLoader.cs` exists, but `ViewModels/MainViewModel.cs:855` -> `BrowserLaunchManager.Launch` only calls `BrowserLauncher.Launch`; branded-Chrome `CdpLoadUnpacked` currently produces a warning instead of invoking the loader.
- Verified gap: `Services/PolicyInstallService.cs` can write/rollback `ExtensionInstallForcelist` and check update XML/CRX reachability, but `ViewModels/MainViewModel.cs:937` only logs readiness. No main workflow lets a user select a packaged CRX/update feed, apply policy, write Edge `ExtensionSettings.override_update_url`, or run rollback.
- Verified gap: GitHub release assets now expose a `digest` field and the repo's own v0.3.1 release has `sha256:...` digests, but `GitHubService` stores only sidecar checksum URLs and `ExtensionService.InstallAsync` verifies only sidecars. Assets without sidecars remain unverifiable even when GitHub provides a digest.
- Verified risk: CDP pipe code documents live Chrome 142+ uncertainty in `Services/Cdp/CdpExtensionLoader.cs`; no browser conformance test records installed Chrome/Edge/Brave versions, strategy, CDP result frames, or post-load extension IDs.
- Verified risk: policy health checks cover registry, update XML, and CRX reachability, but not package-risk preflight. Chrome Web Store policy rejects remotely hosted executable code; policy force-install can make a bad package harder for users to remove, so policy mode should run local static checks before writing HKLM policy.
- Existing strengths to keep: DPAPI token protection, atomic JSON writes with `.bak`, single-instance guard, zip-slip extraction guard, checksum sidecar fail-closed behavior, permission diff gating, CRX3 same-key guard, enrollment readiness messaging, and no third-party runtime dependency beyond Octokit plus ProtectedData.

## Architecture Assessment
- `BrowserLauncher` owns strategy resolution and command-line launches; `CdpExtensionLoader` owns CDP loading; `BrowserLaunchManager` should become the orchestrator that invokes command-line or CDP load based on `LaunchStrategy` and returns exact loaded IDs/errors.
- `PolicyInstallService` is a good WPF-free backend, but it needs a workflow service above it for package selection, CRX/update.xml generation, hosting path selection, Edge `ExtensionSettings`, elevation/consent, health check, and rollback.
- `GitHubService` should model release-asset provenance more completely: digest, uploader, asset ID, content type, and update timestamp. This enables digest verification and clearer diagnostics without another GitHub call.
- The current `MainWindow.xaml` surface is dense and functional, but policy install, CDP load results, and CfT management need dedicated panels or dialogs rather than more toolbar buttons.
- Tests are strong at the service level, but missing live or fixture-backed conformance for CDP launch, policy workflow orchestration, GitHub asset digests, and package-risk scanning. Keep live-browser tests opt-in so normal `dotnet test` remains stable.
- Documentation is current for v0.3.1 in README/CHANGELOG, but `ROADMAP.md` is local-only and broad. New implementation items should keep acceptance criteria precise so future agents do not redo this research.

## Rejected Ideas
- Add a mobile version to this repo: rejected because the Windows WPF app's core value is managing local browser profiles, registry policy, CDP pipes, and Windows filesystem state. Source: current code architecture and companion prompts in `docs/companion-prompts.md`.
- Replace the app with a browser extension: rejected because Chrome extension APIs cannot write HKLM policy, launch browsers, manage local extension directories, or inspect arbitrary local packages. Source: Chrome extension platform docs.
- Upload private packages to third-party scanners by default: rejected because it contradicts the private-store/privacy model. Prefer local static scanning plus offline threat feeds. Source: README privacy posture, MalExt/chrome-mal-ids feeds.
- Bundle Chrome for Testing by default: rejected because CfT is large and already has JSON endpoints. Offer managed opt-in download/cache instead. Source: Chrome for Testing availability docs.
- Depend on Puppeteer/Selenium/Playwright for CDP: rejected for now because the project intentionally keeps runtime dependencies minimal and already has a small pipe client. Reconsider only if live CDP validation shows protocol maintenance is too costly. Source: `CdpProtocol.cs`, `CdpPipeProcess.cs`, Octokit-only philosophy in `CLAUDE.md`.
- Promise unmanaged consumer policy force-install: rejected because Chrome policy docs require domain/management context for non-Web-Store automatic installs. The app already detects this; the UI must keep it explicit. Source: Chrome Enterprise policy docs and `PolicyEnrollmentService.cs`.

## Sources
Platform and browser loading:
- https://chromedevtools.github.io/devtools-protocol/tot/Extensions/
- https://github.com/mozilla/web-ext/issues/3388
- https://github.com/seleniumbase/SeleniumBase/issues/4053
- https://github.com/cypress-io/cypress/issues/31690
- https://github.com/webdriverio/webdriverio/issues/14505
- https://googlechromelabs.github.io/chrome-for-testing/
- https://developer.chrome.com/docs/chromedriver/downloads/version-selection

Enterprise policy and packaging:
- https://support.google.com/chrome/a/answer/7532015
- https://chromeenterprise.google/policies/
- https://learn.microsoft.com/en-us/troubleshoot/microsoft-edge/development/self-host-extension-update
- https://chromium.googlesource.com/chromium/src/+/HEAD/components/crx_file/README.md

Extension security and standards:
- https://developer.chrome.com/docs/extensions/develop/migrate/remote-hosted-code
- https://developer.chrome.com/docs/extensions/develop/migrate/mv2-deprecation-timeline
- https://developer.chrome.com/docs/extensions/reference/permissions-list
- https://malext.io/
- https://github.com/The-Privacy-Commons-Institute/chrome-mal-ids
- https://www.synacktiv.com/en/publications/the-phantom-extension-backdooring-chrome-through-uncharted-pathways

Comparable tools:
- https://github.com/ImranR98/Obtainium
- https://github.com/mozilla/web-ext
- https://github.com/wxt-dev/wxt
- https://github.com/PlasmoHQ/plasmo
- https://github.com/extension-js/extension.js
- https://github.com/crxjs/chrome-extension-tools
- https://wxt.dev/guide/essentials/config/browser-startup.html
- https://docs.plasmo.com/framework/workflows/build

Dependencies and distribution:
- https://docs.github.com/en/rest/releases/assets?apiVersion=2022-11-28
- https://github.com/octokit/octokit.net/releases
- https://learn.microsoft.com/en-us/dotnet/core/releases-and-support

## Open Questions
- Does the installed branded Chrome on the target Windows machine return stable success frames for `Extensions.loadUnpacked` over the hand-rolled fd 3/4 pipe, and does Chrome remain running after LocalChromeStore closes the pipe?
- Does current Microsoft Edge require `ExtensionSettings.override_update_url` for every self-hosted policy update in this app's target deployment model, or only for Edge-managed installs that depend on manifest `update_url`?
