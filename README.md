<p align="center">
  <img src="banner.png" alt="LocalChromeStore" />
</p>

<h1 align="center">
  <img src="logo.png" alt="" width="36" align="center" />
  &nbsp;LocalChromeStore
</h1>

<p align="center">
  <a href="https://github.com/SysAdminDoc/LocalChromeStore/releases"><img src="https://img.shields.io/badge/version-0.1.0-cba6f7?style=for-the-badge" alt="Version" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-a6e3a1?style=for-the-badge" alt="License" /></a>
  <a href="https://github.com/SysAdminDoc/LocalChromeStore"><img src="https://img.shields.io/badge/platform-Windows%2010%2F11-74c7ec?style=for-the-badge" alt="Platform" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge" alt=".NET" /></a>
</p>

> **A personal store for the Chromium extensions you build yourself.**
> Lists every extension across your GitHub repos, downloads the latest release ZIP/CRX, and loads them into Chrome / Brave / Edge with a single click. Install. Uninstall. Move on.

LocalChromeStore exists for one reason: when you build a lot of Chrome extensions, "Load unpacked" gets old fast. This is a native Windows store UI for your own extensions — sourced from your GitHub releases, with proper install / uninstall semantics.

---

## Why it exists

Chromium 75+ blocks drag-and-drop install of self-signed CRX files with `CRX_REQUIRED_PROOF_MISSING` — even with developer mode on, even with a valid signing key. The official paths are:

1. **Chrome Web Store** — useful for shipping, awful for testing your own dogfood
2. **Load unpacked** — works, but clicking through the file picker for ten extensions every browser reset is friction
3. **Enterprise Policy `ExtensionInstallForcelist`** — the only self-host path that actually works on stock Chrome / Brave / Edge

LocalChromeStore wraps path 2 with a real store UI and (in v0.2.0) wraps path 3 for true auto-install.

---

## Features

- **GitHub-sourced discovery** — lists every repo with a `manifest.json` or a release ZIP / CRX asset for any user or org
- **Store-style cards** — extension logo, name, version, description, install / uninstall buttons, link to repo
- **One-click install** — downloads the latest release ZIP, extracts to a managed folder, tracks it
- **One-click uninstall** — wipes the local copy and removes it from the load list
- **Browser launcher** — fires Chrome / Brave / Edge / Vivaldi / Opera / Chromium with `--load-extension=...` pointing at every installed extension
- **Auditable launch sessions** — optional startup URL, clean temporary profile mode, and a copyable launch command preview
- **Environment portability** — export/import installed extension targets and portable discovery settings as JSON
- **Update workflow** — update-available badges, permission-change review, manual **Update all**, optional auto-update on refresh, and optional launch-after-install
- **Search and filter** — by name, repo, or description; toggle to show only installed
- **Topic filter (optional)** — restrict discovery to repos tagged with a specific GitHub topic (default `chrome-extension`)
- **Optional GitHub PAT** — unauthenticated GitHub API caps at 60 req/h; a personal access token raises that to 5,000/h, unlocks private repos, and is stored with Windows DPAPI
- **Catppuccin Mocha dark theme** — easy on the eyes; light theme planned
- **Activity log + crash log** — every install / uninstall / launch is logged in-app and to disk
- **Async** — every API call, download, and extraction runs off the UI thread

---

## Install

### From release (recommended)

1. Grab the latest `LocalChromeStore-vX.Y.Z.zip` from the [Releases page](https://github.com/SysAdminDoc/LocalChromeStore/releases)
2. Extract anywhere
3. Run `LocalChromeStore.exe`

Requires the .NET 9 Desktop Runtime (the installer prompts if missing).

### From source

```bash
git clone https://github.com/SysAdminDoc/LocalChromeStore.git
cd LocalChromeStore
dotnet build src/LocalChromeStore/LocalChromeStore.csproj -c Release
dotnet test LocalChromeStore.sln -c Release
./src/LocalChromeStore/bin/Release/net9.0-windows/LocalChromeStore.exe
```

---

## Usage

1. **Click Settings** in the top right
2. Set **GitHub user / org** to your handle (defaults to `SysAdminDoc`)
3. *(Optional)* Paste a GitHub personal access token to raise rate limits and surface private repos
4. *(Optional)* Enable **Filter by topic** if you want to limit to repos tagged with `chrome-extension`
5. Click **Save settings**, then click **Refresh**

Every qualifying repo appears as a card. Click **Install** on a card — LocalChromeStore downloads the latest release ZIP/CRX, extracts it to `%LOCALAPPDATA%\LocalChromeStore\extensions\<owner>\<repo>\<version>\`, and registers it.

When installed extensions have newer catalog versions, their cards show **Update available**. Use the card's update button for one extension, or **Update all** to replace every installable outdated local copy. If an update adds required permissions, optional permissions, host access, or optional host access, LocalChromeStore shows the diff and asks for approval first. **Auto-update on refresh** skips permission-expanding updates so new extension access is not accepted silently.

To load installed extensions into a browser:

1. Pick the browser from the dropdown
2. *(Optional)* Enter a startup URL to open after the extensions load
3. *(Optional)* Enable **Clean temp profile** to launch with an isolated browser profile under `%LOCALAPPDATA%\LocalChromeStore\profiles\temp\`
4. Click **Launch session**

The browser opens with `--load-extension=<all installed paths>`. The browser will show its standard "developer mode extensions" banner — that is normal for `--load-extension` and not a sign anything is wrong. The extensions persist for that browsing session; close the browser and they unload (which is exactly what you want during dev/test).

Use **Copy args** to copy the exact command LocalChromeStore will run. This is useful when debugging extension load failures or reproducing a launch session outside the app.

Use **Export environment** to save the installed extension set, manifest trust snapshot, GitHub owner list, topic filter, and launch options as a portable JSON file. Use **Import environment** on another machine to apply those discovery settings, refresh GitHub, and install matching ZIP/CRX release assets. GitHub tokens are never written to the export file.

---

## How discovery works

For each user/org you've configured, LocalChromeStore:

1. Lists their repos via the GitHub API
2. For each repo, checks for a latest release with a `.zip` or `.crx` asset
3. If no release asset, probes for `manifest.json` at common paths (root, `extension/`, `src/`, `dist/`, `public/`)
4. If the manifest is found (in the ZIP or repo), reads `name`, `version`, `description`, and `icons` to enrich the card
5. Caches the icon to `%LOCALAPPDATA%\LocalChromeStore\cache\icons\`

Repos with no manifest and no release ZIP/CRX are skipped — they won't clutter the store. Archived repos are skipped too.

---

## Where things live

| Path | Purpose |
| --- | --- |
| `%APPDATA%\LocalChromeStore\settings.json` | User settings (GitHub user, DPAPI-protected token, preferred browser, launch/update options) |
| `%APPDATA%\LocalChromeStore\installed.json` | Installed-extension manifest |
| `%LOCALAPPDATA%\LocalChromeStore\extensions\<owner>\<repo>\<version>\` | Extracted extension files |
| `%LOCALAPPDATA%\LocalChromeStore\profiles\temp\` | Clean temporary Chromium profiles created for launch sessions |
| `%LOCALAPPDATA%\LocalChromeStore\cache\icons\` | Cached extension icons |
| `%LOCALAPPDATA%\LocalChromeStore\logs\` | Crash logs |

To start fresh, just delete the two folders.

---

## Architecture

WPF on .NET 9 — MVVM, no third-party MVVM toolkit.

- `Models/` — plain data records (`ExtensionInfo`, `InstalledExtension`, `BrowserInfo`, `AppSettings`)
- `Services/` — `GitHubService` (Octokit wrapper, discovery), `ExtensionService` (download, ZIP / CRX extract, install state), `BrowserLauncher` (browser detection + launch-plan construction), `SettingsService` (JSON persistence + DPAPI token protection)
- `ViewModels/` — `MainViewModel` orchestrates everything; `ExtensionCardViewModel` per-card state
- `Views/` — `ExtensionCardView` user control, plus the main window
- `Themes/` — Catppuccin Mocha resource dictionary

CRX files are unpacked by stripping the CRX2/CRX3 header and extracting the inner ZIP — Chrome / Brave / Edge re-sign the unpacked tree on load anyway, so we don't need to verify the signature ourselves.

---

## Roadmap

See [ROADMAP.md](ROADMAP.md). Highlights:

- **v0.2.0** — Enterprise Policy install path: write `HKLM\Software\Policies\Google\Chrome\ExtensionInstallForcelist` (and Brave / Edge equivalents) plus a self-hosted `update.xml` on GitHub Pages, so extensions auto-install at next browser launch with no `--load-extension` flag and no developer-mode banner
- **v0.3.0** — Update safety: historical-version restore for environment imports, stronger manifest/extraction tests, and policy-ready package checks
- **v0.4.0** — Light theme + accent color picker
- **v0.5.0** — Local folder source, custom update-feed source, and richer named launch profiles

---

## Contributing

This is built primarily for personal dev/test workflow, but PRs are welcome. Open an issue first if it's a bigger change.

---

## License

[MIT](LICENSE).
