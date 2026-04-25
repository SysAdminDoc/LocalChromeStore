# Companion-Project Prompts

Two self-contained build prompts for the natural siblings of LocalChromeStore:

1. **LocalAndroidStore** — a personal Android-app catalog sourced from your GitHub releases (Kotlin / Compose / Material 3, AMOLED dark)
2. **LocalDesktopStore** — a personal Windows desktop-app catalog sourced from your GitHub releases (WPF / .NET 9, mirrors LocalChromeStore architecture)

Each prompt is designed to be dropped, as-is, into a fresh agent session (Claude, Codex, etc.) and produce a working, building, releasable v0.1.0 with the same conventions as LocalChromeStore — Catppuccin Mocha dark, GitHub-sourced discovery, store-style cards, install/uninstall/run buttons, branch-protected public repo, MIT license, smoke-tested release artifact.

How to use:

- Copy the entire prompt block (including the `===` fences) into a new agent thread
- Confirm the agent has a clean working directory under `~/repos/` and access to `gh`, `git`, and the relevant SDK (`./gradlew` for Android, `dotnet 9` for desktop)
- The agent should not need to ask clarifying questions — every decision is pre-made

---

## Prompt 1 — LocalAndroidStore

```
=== LocalAndroidStore v0.1.0 — autonomous build prompt ===

You are an autonomous build agent. Your task is to create LocalAndroidStore,
a personal Android-app catalog sourced from GitHub Releases. It is the
Android sibling of LocalChromeStore (https://github.com/SysAdminDoc/LocalChromeStore),
a personal Chromium extension store the same user already shipped.

WHY THIS EXISTS
The user ships ~15 Android apps from GitHub Releases as signed APKs (some
also produce AABs for Play). Sideloading each one through the file manager
on every fresh install / re-image is friction. F-Droid won't host them
(many are private or in-development). Obtainium does the same job generically
but is not tailored to the user's catalog or visual identity. We want a
private store that mirrors LocalChromeStore's UX one-to-one on Android:
card grid, install / uninstall / open buttons, dark theme, GitHub-sourced.

NON-NEGOTIABLE STACK
- Kotlin 1.9+, Android Gradle Plugin matching the latest stable
  (verify via context7 before pinning)
- Jetpack Compose + Material 3
- minSdk 26, targetSdk = latest stable
- AMOLED-true-black default theme; Catppuccin Mocha accents
  (mauve #cba6f7, sapphire #74c7ec, green #a6e3a1, yellow #f9e2af, red #f38ba8)
- DataStore (Preferences) for settings — NOT SharedPreferences
- EncryptedSharedPreferences (or DataStore + Android Keystore) for the GitHub PAT
- Coroutines + Flow for async; no runBlocking
- OkHttp + kotlinx.serialization for the GitHub REST API
  (do NOT pull in Octokit-Java — too heavy)
- R8 + resource shrink in release; signed release keystore
  (CI consumes KEYSTORE_BASE64 secret per the user's stack-android.md)

PHASE 1 (v0.1.0) — MUST SHIP
1. Settings screen: GitHub user/org input, optional PAT (PasswordVisualTransformation
   + EncryptedSharedPreferences), topic-filter toggle (default `android-app`),
   "show pre-releases" toggle (default off).
2. Catalog screen: Compose LazyVerticalGrid of cards. Each card shows app icon,
   app name, version, two-line description, repo handle, star count, status
   badge, action buttons (Install / Uninstall / Update / Open). Pull-to-refresh.
3. Discovery: for each configured user, list public repos, find latest release
   with an `.apk` asset (skip `.apk.idsig` siblings, treat them as paired).
   If multiple APKs (split-APK / per-ABI), pick the universal one if present,
   otherwise the largest. Optional enrichment: extract `AndroidManifest.xml`
   from the APK ZIP entry and read `applicationId`, `versionName`, `versionCode`,
   `application/@label`, and the largest `application/@icon` resource.
4. Install: download APK to app cache, then drive `PackageInstaller.Session`.
   Manifest must declare `REQUEST_INSTALL_PACKAGES`. On first install, prompt
   the user to grant "Install unknown apps" via system settings deep-link.
5. Uninstall: `Intent.ACTION_DELETE` with the app's `applicationId`.
   Do NOT use `PackageInstaller.uninstall` — that requires device-owner.
6. Installed-state detection: on every refresh, query `PackageManager`
   filtered by the discovered `applicationId` set. Compute "update available"
   as `remoteVersionCode > localVersionCode`. Surface as a yellow status badge.
7. APK signature pinning: on first successful install, capture the APK's
   signing certificate SHA-256 and store it. On subsequent updates, if the
   signature does NOT match, BLOCK the install and show a clear
   "publisher key changed — possible MITM or repo takeover, install manually
    if intentional" warning. Never auto-accept a key swap.
8. Activity log panel + on-disk crash log writer (mirror LocalChromeStore).
9. Async everywhere — UI never blocks. Per-card progress for download / install.
10. `banner.png` and `logo.png` at repo root, wired into README header per
    the user's convention (centered banner, then title-line with logo + badges).

ARCHITECTURE
LocalAndroidStore/
├── app/build.gradle.kts        # AGP, Compose, Material3, OkHttp, kotlinx.serialization,
│                                # DataStore, security-crypto, accompanist if needed
├── app/src/main/AndroidManifest.xml    # REQUEST_INSTALL_PACKAGES, INTERNET,
│                                       # QUERY_ALL_PACKAGES (if necessary, else <queries>)
├── app/src/main/kotlin/com/sysadmin/lasstore/
│   ├── data/
│   │   ├── GitHubClient.kt         # OkHttp + serialization
│   │   ├── ApkInspector.kt         # parse AndroidManifest from APK ZIP
│   │   ├── InstallStateRepo.kt     # PackageManager wrapper
│   │   ├── SignaturePinStore.kt    # encrypted prefs
│   │   └── SettingsStore.kt        # DataStore Preferences
│   ├── domain/
│   │   ├── AppInfo.kt              # the discovered model
│   │   └── DiscoveryUseCase.kt
│   ├── install/
│   │   └── PackageInstallerService.kt  # session-backed install
│   ├── ui/
│   │   ├── theme/Catppuccin.kt + Type.kt
│   │   ├── catalog/CatalogScreen.kt + AppCard.kt + StatusBadge.kt
│   │   ├── settings/SettingsScreen.kt
│   │   └── log/LogScreen.kt
│   └── App.kt + MainActivity.kt
├── app/proguard-rules.pro
├── README.md + CHANGELOG.md + ROADMAP.md + LICENSE (MIT) + .gitignore
├── banner.png + logo.png
├── .github/workflows/release.yml   # workflow_dispatch, version input,
│                                    # ./gradlew assembleRelease, sign with
│                                    # KEYSTORE_BASE64 secret, upload APK + sha256
└── CLAUDE.md (gitignored — local working notes)

PHASE 2 (v0.2.0) — DO NOT SHIP IN v0.1.0
- WorkManager-driven update worker on a 6-hour cadence
- Wear OS companion that pairs and pushes to a connected watch
- Multi-device push via ADB pair (TLS) for non-rooted use
- F-Droid index export
- Light theme + accent picker

DEFINITION OF DONE — v0.1.0
[ ] ./gradlew assembleDebug succeeds without keystore
[ ] ./gradlew assembleRelease succeeds with KEYSTORE_BASE64 wired through CI
[ ] On a real Android 14+ device, sideload the produced APK, grant
    "Install unknown apps", and install at least one of the user's
    existing released apps end-to-end (NovaCut, HostShield, ZeusWatch,
    or AlarmClockXtreme are good targets — they all ship signed APKs)
[ ] Uninstall flow round-trips
[ ] Install an older version manually, refresh, "Update available" appears
[ ] Forge a re-signed APK, attempt install, signature-pin warning fires
[ ] README shows banner + logo + shields.io badges (version, license,
    platform: Android 8.0+, license: MIT)
[ ] All version strings match (build.gradle.kts versionName/versionCode,
    README badge, CHANGELOG, ROADMAP)
[ ] Public repo, branch protection on main with enforce_admins=true
[ ] CLAUDE.md gitignored; no AI references in committed files
[ ] One smoke-tested release published with APK + sha256 sidecar

ANTI-PATTERNS — DO NOT
- Do NOT make a single-Activity mega file; package into data/domain/ui as shown
- Do NOT runBlocking to make async calls synchronous "for simplicity"
- Do NOT request QUERY_ALL_PACKAGES unless you justify it; prefer <queries>
- Do NOT silent-install (we are not device-owner). The system install dialog
  is REQUIRED on stock Android. Document this clearly in the README.
- Do NOT add tests unless the user asks (per their global CLAUDE.md)
- Do NOT add backwards-compatibility for SDK < 26
- Do NOT add "TODO: future" comments — track in ROADMAP.md
- Do NOT auto-accept a publisher-key swap

REFERENCE / INSPIRATION
- LocalChromeStore (the WPF sibling): https://github.com/SysAdminDoc/LocalChromeStore
- Obtainium (similar generic tool): https://github.com/ImranR98/Obtainium
- The user's stack-android.md memory describes their existing Android
  conventions in detail — mirror them.

OUTPUT
Ship a complete, building, releasable v0.1.0. Push to
https://github.com/SysAdminDoc/LocalAndroidStore. Cut the v0.1.0 release
with APK + sha256 attached. Update the user's memory file index with a
new topic file and a one-line entry in MEMORY.md.

=== end prompt ===
```

---

## Prompt 2 — LocalDesktopStore

```
=== LocalDesktopStore v0.1.0 — autonomous build prompt ===

You are an autonomous build agent. Your task is to create LocalDesktopStore,
a personal Windows desktop-app catalog sourced from GitHub Releases. It is
the desktop sibling of LocalChromeStore (https://github.com/SysAdminDoc/LocalChromeStore),
which the same user already shipped.

WHY THIS EXISTS
The user ships ~15 Windows desktop apps across C# WPF (.NET 9), C++ Win32
with WebView2, PowerShell WPF, Python PyQt6, and standalone PowerShell
scripts. Each one delivers via GitHub Releases as some combination of MSI,
NSIS .exe installer, Inno Setup .exe, portable .zip, or PyInstaller .exe.
Hand-installing each one on a fresh box is friction. WinGet is close, but
(a) requires public submission, (b) doesn't surface the in-house ones, and
(c) hides anything not in their catalog. We want a private store that
mirrors LocalChromeStore's UX exactly: card grid, install / uninstall /
run buttons, Catppuccin Mocha dark theme, GitHub-sourced discovery.

NON-NEGOTIABLE STACK
- C# WPF, target net9.0-windows
- MVVM, NO third-party MVVM toolkit. Use the same pattern LocalChromeStore
  uses: ViewModelBase + RelayCommand + AsyncRelayCommand (port these files
  directly — they are clean and tested).
- Octokit 13.x — only third-party dep. Justify any other addition.
- Catppuccin Mocha dark theme. Port DarkTheme.xaml from LocalChromeStore as
  the starting point and tighten as needed.
- Framework-dependent publish (--self-contained false). Per the user's
  stack-csharp.md: framework-dependent is the default; self-contained only
  on explicit request.
- Async everywhere — UI never blocks during download or install.

PHASE 1 (v0.1.0) — MUST SHIP
1. Discovery: same shape as LocalChromeStore. Configurable owners (primary +
   ExtraOwners), optional PAT (PasswordBox in UI; never plaintext-bound to
   a VM string — use the codebehind PasswordChanged pattern from
   LocalChromeStore's MainWindow.xaml.cs), optional topic filter (default
   `windows-app` — pick one and document).
2. Asset classification per release. For the latest release of each repo:
     .msi                              -> MSI installer
     *setup*.exe / *installer*.exe /
     *-setup-*.exe / *-installer-*.exe -> executable installer (Inno or NSIS)
     .zip                              -> portable archive
     .crx                              -> SKIP (LocalChromeStore handles those)
     anything else                     -> SKIP (do not clutter the catalog)
   If multiple eligible assets, prefer in this order: MSI > NSIS/Inno > .zip.
3. Install handlers:
     MSI:        msiexec /i "<file>" /qb /norestart  (log to %LOCALAPPDATA%
                 \LocalDesktopStore\logs\msi-<repo>.log)
     NSIS .exe:  try /S
     Inno .exe:  try /SILENT /NORESTART (detect Inno Setup by reading
                 the .exe's version-info "FileDescription" / "ProductName"
                 for "Inno Setup" or "Setup")
     Generic exe installer: run interactive (no silent flag)
     Portable .zip: extract to %LOCALAPPDATA%\LocalDesktopStore\apps\
                    <owner>\<repo>\<version>\, create Start Menu shortcut
                    pointing at the largest .exe in the extraction
4. Install-state detection: at install time, query Windows uninstall keys
   under both HKLM and HKCU `Software\Microsoft\Windows\CurrentVersion\Uninstall`
   for an entry whose DisplayName / Publisher matches. Save the
   UninstallString and InstallLocation. For portable apps, install state
   is just "extraction folder exists".
5. Uninstall handlers:
     MSI:    msiexec /x <ProductCode> /qb /norestart
     NSIS / Inno: invoke saved UninstallString (typically
                  '"<dir>\unins000.exe" /SILENT')
     Portable: delete extraction folder + remove Start Menu shortcut
6. Run-after-install: a "Run" button per card. For registry-installed apps,
   start the .exe at InstallLocation; for portable apps, start the largest
   .exe in the extraction.
7. Store cards: same visual layout as LocalChromeStore. Logo (from manifest
   icons or repo OG image), name, version, description, repo link, status
   badge ("Installed" / "Update available" / "Ready to install" / "Release
   needed"), action buttons.
8. Activity log panel + on-disk crash log writer.
9. Settings drawer: GitHub user, PAT (PasswordBox), topic filter toggle,
   install root override, "verify SHA256 sidecar" toggle.
10. Asset hash verification: if the release ships a `<asset>.sha256.txt`
    sidecar (LocalChromeStore convention — every release has one), download
    it and verify the artifact hash before invoking the installer. Refuse
    to install on mismatch and log loudly.
11. `banner.png` + `logo.png` at repo root, wired into README header
    (centered banner, then title-line with logo + badges).

ARCHITECTURE — mirror LocalChromeStore aggressively
LocalDesktopStore/
├── LocalDesktopStore.sln
├── src/LocalDesktopStore/
│   ├── LocalDesktopStore.csproj    # net9.0-windows, UseWPF, Octokit 13.x
│   ├── App.xaml + App.xaml.cs      # crash logger, dispatcher unhandled handler
│   ├── MainWindow.xaml + .xaml.cs  # PasswordBox codebehind for PAT
│   ├── Models/
│   │   ├── AppInfo.cs              # discovered model
│   │   ├── InstalledApp.cs
│   │   ├── AppSettings.cs
│   │   └── ArtifactKind.cs (enum: Msi, Nsis, Inno, GenericExe, Portable)
│   ├── Services/
│   │   ├── GitHubService.cs        # Octokit-backed discovery
│   │   ├── AssetClassifier.cs      # MSI / NSIS / Inno / portable detection,
│   │   │                           # incl. PE version-info probe for Inno
│   │   ├── InstallService.cs       # routes to MsiInstaller / NsisInstaller
│   │   │                           # / InnoInstaller / PortableInstaller
│   │   ├── UninstallRegistry.cs    # uninstall-key lookup (HKLM + HKCU)
│   │   ├── HashVerifier.cs         # .sha256.txt sidecar verification
│   │   ├── ShortcutService.cs      # Start Menu .lnk via IShellLink COM
│   │   └── SettingsService.cs      # JSON persistence
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs        # port from LocalChromeStore
│   │   ├── RelayCommand.cs         # port from LocalChromeStore
│   │   ├── MainViewModel.cs
│   │   └── AppCardViewModel.cs
│   ├── Views/AppCardView.xaml
│   ├── Themes/DarkTheme.xaml       # port from LocalChromeStore, keep tokens
│   └── Converters/Converters.cs    # BoolToVis, NullToVis, EmptyStringToVis
├── README.md + CHANGELOG.md + ROADMAP.md + LICENSE (MIT) + .gitignore
├── banner.png + logo.png
├── .github/workflows/release.yml   # workflow_dispatch w/ version input,
│                                    # dotnet publish framework-dependent
│                                    # win-x64, ZIP + sha256 sidecar
└── CLAUDE.md (gitignored — local working notes)

PHASE 2 (v0.2.0) — DO NOT SHIP IN v0.1.0
- Auto-update on refresh (compare local registry version to remote release
  tag → "Update available" badge + "Update all" button)
- Catppuccin Latte light theme + accent picker
- WinGet manifest export (so each app can also be `winget install`-able)
- MSIX packaging support
- Cross-platform port via Avalonia (later)

DEFINITION OF DONE — v0.1.0
[ ] dotnet build -c Release clean, 0 warnings
[ ] dotnet publish src/LocalDesktopStore/LocalDesktopStore.csproj -c Release
    -r win-x64 --self-contained false -o publish/win-x64 succeeds
[ ] App launches, smoke-tested by running it for at least 60 seconds and
    clicking through the catalog (do NOT claim done without launching the .exe)
[ ] Install flow round-trips for at least one of the user's actual repos:
      NeonNote        (C++ WebView2 — likely NSIS or .zip portable)
      NVMe Patcher    (C# WPF — MSI or Inno)
      HEICShift       (PyInstaller — portable .zip)
[ ] Uninstall flow round-trips for the installed app
[ ] Asset hash verification works against a release with a sidecar
    (LocalChromeStore v0.1.0 is a known-good test target — its release
     ships .zip + sha256.txt)
[ ] All version strings synced (csproj <Version>, AssemblyVersion,
    README badge, CHANGELOG, ROADMAP)
[ ] README shows banner + logo + shields.io badges (version, license,
    platform: Windows 10/11, .NET 9)
[ ] Public repo, branch protection on main with enforce_admins=true
[ ] CLAUDE.md gitignored; no AI references in committed files
[ ] One smoke-tested release published with .zip + sha256 sidecar

ANTI-PATTERNS — DO NOT
- Do NOT auto-elevate to admin "just in case". UAC only when an installer
  needs it (per-machine MSI requires it; per-user MSI does not).
- Do NOT shell out to PowerShell for tasks .NET can do natively (registry,
  ZIP, file ops, COM shortcuts).
- Do NOT use WMI to enumerate installed apps. Read the uninstall registry
  keys directly — faster, consent-free, no WMI dependency.
- Do NOT bind a PasswordBox to a plain VM string property. Use the
  PasswordChanged codebehind pattern from LocalChromeStore's
  MainWindow.xaml.cs (GitHubTokenBox_PasswordChanged).
- Do NOT add tests unless the user asks (per their global CLAUDE.md)
- Do NOT add a "TODO" or "FIXME" comment — track in ROADMAP.md
- Do NOT silently swallow install failures. Log to activity panel AND
  crash log AND show an inline error on the card.
- Do NOT add a self-contained build path. Framework-dependent only —
  smaller artifact, matches the user's stack convention.
- Do NOT hardcode the user's GitHub handle. Settings screen first.
  Default to `SysAdminDoc` only as the placeholder value.

REFERENCE / INSPIRATION
- LocalChromeStore (the WPF sibling, port files aggressively):
  https://github.com/SysAdminDoc/LocalChromeStore
- The user's stack-csharp.md memory describes their existing C# WPF
  conventions (.NET 9, MVVM, framework-dependent default) — mirror them.
- The user's PyInstaller fork-bomb guard rules (per global CLAUDE.md) —
  apply if any embedded helper is itself a PyInstaller binary.

OUTPUT
Ship a complete, building, releasable v0.1.0. Push to
https://github.com/SysAdminDoc/LocalDesktopStore. Cut the v0.1.0 release
with .zip + sha256 sidecar attached. Update the user's memory file index
with a new topic file and a one-line entry in MEMORY.md.

=== end prompt ===
```

---

## Notes on running these prompts

**Provider choice.** Both prompts assume an agent with shell access (`gh`, `git`, `dotnet` or `gradle`), filesystem write access under `~/repos/`, and the user's existing memory / convention files loaded. They're written to be agent-agnostic — Claude (any model), Codex / GPT-5.x, and Gemini agents all should be able to execute either of these without follow-up questions.

**One-shot vs iterative.** Each prompt is sized for a one-shot session. If your agent stalls part way, point it at the unfinished `~/repos/<name>/CLAUDE.md` (which the prompt instructs it to keep up to date) and tell it to continue from "Definition of Done — v0.1.0" — that's a complete picture of what's left.

**Order.** Build LocalDesktopStore first if you want a faster path — it shares ~80% of LocalChromeStore's architecture, so much of the WPF scaffold ports directly. LocalAndroidStore is a fresh codebase (Compose + Material 3) and will take longer per pass.

**After both ship.** All three (LocalChromeStore, LocalDesktopStore, LocalAndroidStore) form a coherent personal-store family. Worth adding a unified landing page or pinned README on your GitHub profile that links the three.
