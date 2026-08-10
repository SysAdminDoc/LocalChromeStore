# Roadmap

Actionable work only. Historical and completed roadmap material is archived in CHANGELOG.md; blocked work is kept in Roadmap_Blocked.md.

## Actionable Items

- [ ] **Audit-identified reliability**
   - Fix flaky parallel test execution: `BrowserConformanceServiceTests`, `SettingsServiceTests`, `SmokeTests` have TOCTOU temp-dir races under xUnit parallel runs. Add `[Collection]` attributes or unique temp roots.
   - CdpPipeProcess uses raw `IntPtr` for the process handle instead of `SafeProcessHandle`; a partially-failed Dispose can leak the handle.

- [ ] **Audit-identified hardening**
   - CatalogCacheService / UsageStatsService have no file-size limit on deserialization; a corrupted or malicious cache file could exhaust memory before the catch fires.
   - SettingsService `ReadJsonWithBackup` silently resets settings if the JSON is valid but schema-mismatched; consider logging when the backup is used.
   - JsonEventLog has no daily log file rotation or cleanup of old `events-*.jsonl` files.

- [ ] **Later polish and integrations**
   - Add GitHub Pages static update hosting.
   - Add advanced `ExtensionSettings` controls.
   - Add a custom update-feed source.
   - Add a GitHub draft-release helper.
   - Add a light theme and accent picker.
   - Move UI strings to resource files for future localization.
   - Add MSIX packaging.
   - Add Authenticode signing when a certificate is available.
   - Add a shared Git-backed catalog workflow.
   - Revisit an Avalonia port only after the Windows feature set is stable.
