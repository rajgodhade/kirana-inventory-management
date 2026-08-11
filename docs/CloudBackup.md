# Cloud Backup

Cloud Backup stores validated Phase 11 `.kbak` bundles in the store owner's own Google Drive or
OneDrive. It is not cloud synchronization: live sales, inventory and settings remain local and
offline-first.

The application creates and validates a normal local bundle first (manifest, SHA-256 and SQLite
integrity check), then passes that exact file to `ICloudBackupProvider`. A failed upload never
blocks billing and the local verified backup remains available for restore. Cloud restore is
expected to download to a temporary path and call the existing `IRestoreService` pipeline, which
validates before taking the mandatory safety backup and replacing the live database.

Provider adapters live in Infrastructure. OAuth client configuration is deployment supplied; no
client secret is compiled into the app. Tokens are encrypted for the current Windows user with
Windows DPAPI and are deleted when Disconnect is used. Tokens are never stored in AppSettings,
`.kbak` files or audit logs.

Backups belong under `VyaparOS/Backups/<Store Name>/` and retention must only remove files positively
identified as VyaparOS `.kbak` files. Automatic cloud upload is non-blocking and retries on the next
scheduled local backup when the provider is unavailable or the machine is offline.

Known limitation: this development build contains provider boundaries and safe failure handling,
but Google/OneDrive OAuth endpoints require deployment-specific registered desktop client IDs and
redirect configuration before real-account connection can be enabled. No fake successful login is
performed.
