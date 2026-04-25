# Roadmap

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
