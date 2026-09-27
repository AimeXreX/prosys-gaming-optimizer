# Changelog

## 1.1.0

- Added: one-click release. The GitHub Release ships a self-contained installer (no .NET install needed) with PresentMon and the restore-point helper bundled, plus a portable ZIP and SHA-256 checksums. The installer checks for Windows 11, can add the user to Performance Log Users so FPS benchmarking works without administrator rights, and launches the app as the signed-in user.

- Fixed: cancelling an optimization no longer skips automatic rollback; recovery always runs to completion.
- Fixed: Binary, MultiString and ExpandString registry values are restored correctly from on-disk backups and compared correctly during verification.
- Fixed: a plan whose values changed after it was created is rejected before any mutation.
- Fixed: manual rollback now updates the session journal, so restored sessions no longer appear as incomplete; a session interrupted before its backup (which changed nothing) is closed cleanly.
- Fixed: `restore last` selects the newest session by journal time and ignores sessions that never captured a backup; CLI errors are reported instead of crashing.
- Fixed: game profiles launched through a launcher (Steam, Epic, …) track the real game process; changes are restored if the game never starts or when ProSyS closes during a session.
- Fixed: unreadable game-profile and benchmark-history files are kept aside instead of being overwritten.
- Fixed: the restore-point helper closes each BEGIN_SYSTEM_CHANGE with END_SYSTEM_CHANGE, verifies the point exists, and reports disabled System Protection or the 24-hour frequency limit; the helper's dependencies are copied and published with the app.
- Fixed: PresentMon is killed on cancellation and both output pipes are drained.
- Changed: removed 22 catalog entries that were internal Windows state, undocumented, cosmetic, or written with the wrong type (`MaximumRecordLength` is a QWORD in 100 ns units); the catalog now has 158 capabilities and no minimum-count rule.
- Changed: Control Panel capabilities declare `RequiresSignOut`; the UI and CLI say when a sign-out is needed.
- Changed: the Safe profile requires a default recommendation with non-legacy evidence, so the filter is no longer a no-op.
- Changed: `DetectionStatus.Disabled` was renamed to `Compliant` (same numeric value, so existing journals still load).
- Added: IPv6 TCP connections in the network scanner; update manifests can reject downgrades; the CPU boost button works for any running game, not only CS2.
- Added: `tools/PresentMon/Get-PresentMon.ps1` (for developers), CI and release GitHub Actions workflows, and an xUnit test suite with regression tests for each fix above.

## 1.0.0

- Expanded the allowlisted current-user catalog from 14 to 180 reversible capabilities in eight product areas.
- Kept automatic selection conservative; personalization and preference changes remain opt-in.
- Added category filtering to the recommendations screen.
- Added plan hash validation before any mutation.
- Added verified automatic rollback and explicit `RecoveryRequired` reporting.
- Added startup discovery of interrupted or incomplete optimization sessions.
- Hardened registry allowlist path-boundary checks.
- Added machine-readable catalog export and incomplete-session listing to the CLI.
- Added fault-injection, tampered-plan and catalog-integrity tests.
- Updated release version and benchmarking documentation.
