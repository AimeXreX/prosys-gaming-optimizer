# Changelog

## 1.3.0

FPS-focused additions. Each one maps to a documented Windows setting, is opt-in, and is restored exactly by rollback.

- Added: **High-performance GPU per game.** Game profiles can tell Windows to run the game on the dedicated GPU (laptops with integrated + dedicated graphics). This is often the largest single FPS gain on such laptops. It is written to the same place as Settings › Display › Graphics, and other per-app graphics options are preserved.
- Added: **Optimizations for windowed games** (Windows 11 22H2+). Borderless and windowed DirectX 10/11 games use the flip presentation model: lower latency and better frame pacing, plus Auto HDR/VRR support. Other DirectX preferences are preserved.
- Added: **High-performance power plan.** Switches to Ultimate Performance or High performance when the PC has one, and restores your plan afterwards. Inside a game profile it applies only while the game runs. On Modern Standby laptops that only have Balanced, it is shown as unsupported and points to Power mode instead.
- Added: **Highest refresh rate.** Sets every display to the highest refresh rate it supports at its current resolution, for example a 144 Hz monitor that was left at 60 Hz.
- Added: **FPS advisor** (Diagnostics page and `prosys advise`). A read-only check for limiters that software should not change: running on battery, display below its maximum refresh rate, hybrid GPU, single-channel memory, desktop memory at base JEDEC speed (enable XMP/EXPO), hardware-accelerated GPU scheduling off, low free space on the system drive, and Game Mode off.
- Changed: Balanced and Competitive game profiles include the new gaming settings; the per-game GPU option is on by default for new profiles.
- Not added on purpose: disabling Memory Integrity/VBS, Defender or other security features; HPET/timer-resolution, network-throttling and "SystemResponsiveness" registry edits; killing services. These are either security-reducing or have no reproducible FPS benefit on Windows 11.

## 1.2.0

- Changed: the offered catalog is now 15 curated settings, each with a reference to the Windows Settings page or dialog it corresponds to, in the categories Gaming & Capture, Input, Accessibility & Input and Preferences. Only Game Mode is selected by default. Full-screen-optimization (GameConfigStore), DWM and HKLM-only values were removed. Every capability shipped by earlier versions remains available for rollback only, so existing backups still restore.
- Changed: `DetectionStatus.Enabled` is now `NonCompliant`; rollback read-back reports `Present`/`Absent`. Numeric values of existing members are unchanged.
- Changed: game profiles — Balanced applies the gaming & capture settings, Competitive adds input and accessibility shortcuts; preferences are never part of a profile.
- Changed: CPU prioritisation raises a game to Above normal only (never High or Realtime), leaves priority boost untouched and is off by default. A refusal by anti-cheat is reported and the session continues.
- Changed: benchmark comparison requires at least three baseline and three after runs of the same game and duration, reports average FPS and 1% low with Welch 95% confidence intervals and reports regressions. Runs can be marked as baseline in the UI. The capture duration is part of the fingerprint.
- Changed: the PresentMon trust message no longer claims Authenticode chain validation; the pinned SHA-256 is the trust anchor.
- Changed: update manifests use schema 2 with a signed expiry of at most 90 days.
- Changed: restore-point helper exit codes — 3 means Windows skipped creation (24-hour limit), 4 means System Restore is turned off.
- Changed: `tools/PresentMon/Get-PresentMon.ps1` was renamed to `tools/PresentMon/fetch.ps1`.
- Fixed: rollback restores DWORD values above `int.MaxValue` exactly, checks the registry type during verification, and deletes registry keys that apply created when they are empty again.
- Fixed: manual rollback undoes changes in reverse application order (recorded in the journal); automatic rollback only touches items that were applied.
- Added: a cross-process mutation lock so the GUI and CLI can never change settings at the same time.
- Added: MIT license.

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
- Added: `tools/PresentMon/fetch.ps1` (for developers), CI and release GitHub Actions workflows, and an xUnit test suite with regression tests for each fix above.

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
