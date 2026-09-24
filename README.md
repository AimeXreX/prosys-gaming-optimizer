# ProSyS Gaming Optimizer 1.0

ProSyS is a local-first Windows 11 gaming diagnostic and optimization application. Its safety model is **measure → plan → back up → change → verify → restore**. It does not weaken Windows security, modify firmware or inject code into games.

## MVP features

- Real Windows, CPU, memory, display-adapter, fixed-drive, power-plan, laptop, Secure Boot and known anti-cheat discovery.
- Extended local inventory for active network adapters, DNS/gateway data, startup registrations, service counts, top memory-consuming processes and Steam manifests.
- Evidence- and risk-labeled recommendations; unknown values stay unknown.
- Deterministic dependency validation and immutable SHA-256-addressed plans.
- Dry run with no mutation.
- Exact original-value backup, disk journal, post-apply verification, automatic failure rollback and idempotent manual rollback.
- Nine-page WPF control center with dashboard, inventory, recommendations, diagnostics, recovery, settings, benchmark, game profiles and an external live overlay.
- Persian-first RTL and English/LTR interface switching, bundled Vazirmatn typography, local JSON report export and 180 reversible, current-user Windows 11 capabilities across gaming, input, shell, desktop, accessibility, personalization, search and background-content categories. Only a small evidence-backed core is selected by default; preference changes are explicit opt-in choices.
- Live CPU sampling and a local gateway latency/jitter probe; no remote telemetry or Internet endpoint is contacted.
- Local CLI; no account, cloud dependency or telemetry.
- JSON machine-readable scan/audit output and JSONL audit logs.
- Intel-signed, SHA-256-pinned PresentMon 2.6.0 capture for average FPS, 1%/0.1% lows, frame-time variance and A/B comparison.
- Atomic per-game profiles with verified changes, launch integration, automatic exact-state rollback when a game exits, startup detection of interrupted sessions and category filtering for the expanded catalog.
- Per-process TCP ownership, vendor GPU API capability discovery and a short-lived allowlisted administrator helper for Windows restore points.
- RSA-PSS signed update-manifest verification and an Inno Setup installer definition with upgrade/repair/uninstall support.

## Build and run

Requirements: Windows 11 and .NET 8 SDK.

```powershell
.\.tools\dotnet\dotnet.exe build ProSyS.sln -c Release
.\.tools\dotnet\dotnet.exe run --project src/ProSyS.App -c Release
.\.tools\dotnet\dotnet.exe run --project src/ProSyS.Cli -c Release -- audit --json
.\.tools\dotnet\dotnet.exe run --project tests/ProSyS.Tests -c Release
```

Data is stored under `%LOCALAPPDATA%\ProSySOptimizer`. Run `plan` before `optimize --profile safe --confirm`. See [RECOVERY.md](RECOVERY.md) before applying changes.

## Safety and status

The GUI normally runs without elevation. Only the explicit “Create restore point” action starts a short-lived UAC helper; the helper accepts no arbitrary command or registry path. Optimization changes remain allowlisted `HKCU` values and are always backed up. Performance effects are measured rather than guaranteed. See [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) for verified scope and external release requirements.

Vazirmatn font licensing and the pinned source hash are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
