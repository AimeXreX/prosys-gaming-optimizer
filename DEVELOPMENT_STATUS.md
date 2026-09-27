# Development status

Status vocabulary is limited to DONE, PARTIAL, TODO and BLOCKED. DONE means implemented and verified at the level stated below—not merely present in the UI.

| Requirement | Status | Implementation / evidence | Limitation / next action |
|---|---|---|---|
| Domain and risk engine | DONE | `ProSyS.Core/Models.cs`, `Planning.cs`; automated policy tests | Expand profile policies beyond Safe |
| Dependency graph and deterministic planner | DONE | conflict, missing dependency, ordering and cycle detection; tests | Add supersedes/invalidation edges |
| Integrity-checked optimization plan | DONE | read-only items, SHA-256 integrity hash and re-detection of every selected value before mutation; tests | The hash detects accidental change only; add a durable signature if plans are ever stored or transferred |
| Windows scanner | DONE | real OS/build/CPU/RAM/GPU/drives/power/laptop/Secure Boot/anti-cheat, network, startup, services, process, Steam and per-process TCP discovery | Temperature sensors remain hardware/vendor-runtime dependent |
| Safe tweak lifecycle | DONE | 158 allowlisted HKCU capabilities with detect/compatibility/backup/apply/verify/rollback for DWord, QWord, String, ExpandString, MultiString and Binary originals; only a small core is selected by default and preference items are opt-in; Control Panel items declare sign-out | Runtime apply is user-triggered only; effects need per-machine benchmarking |
| Transaction journal and crash visibility | DONE | atomic disk journal before and during mutation | Interactive crash recovery UI is TODO |
| Automatic and idempotent rollback | DONE | reverse rollback on failure or cancellation, read-back verification, recovery-required escalation, journal update after manual rollback and isolated fault-injection tests | Cross-version backup migration is TODO |
| Dry run / audit | DONE | GUI Dry Run and CLI `audit`/`plan`; no mutation | HTML/CSV export TODO |
| WPF control center | DONE | Persian-first RTL, bundled Vazirmatn, dark/purple design system, nine functional pages, visible keyboard focus, F5/Ctrl+L/Alt+1…9 shortcuts, diagnostics, history, export and EN/FA switching | Formal third-party screen-reader certification is not claimed |
| CLI | PARTIAL | scan, audit, list, plan, safe optimize, restore last | benchmark/profile commands TODO |
| Structured logging and privacy | DONE | local JSONL correlation entries; telemetry absent | Log viewer and retention settings TODO |
| Privileged operations | DONE | short-lived UAC helper exposes only an allowlisted Windows restore-point operation (BEGIN/END pair, WMI existence check, distinct codes for disabled protection and the 24-hour limit); no shell or arbitrary path input | Intentionally not a persistent service; safer and smaller attack surface |
| Benchmark/A-B engine | DONE | official Intel-signed PresentMon 2.6.0 pinned by SHA-256; parser and comparison tests | A running game and real repeatable scene are required for meaningful results |
| Game profiles / launch integration | DONE | atomic profile store, Safe/Balanced/Competitive selections, verified apply, launch, optional overlay and rollback on exit | Launcher-started games are tracked by process name; changes are restored on game exit, on launch timeout and when ProSyS closes. Crash/power-loss recovery remains available in History rather than automatic after reboot |
| External overlay | DONE | topmost click-through external WPF window, Windows CPU/RAM/network counters, no injection or game-memory access | FPS stays in the dedicated PresentMon benchmark to preserve anti-cheat safety |
| Vendor GPU integration | PARTIAL | official NVAPI/ADL/IGCL runtime capability detection and status reporting | Sensor telemetry appears only when an approved vendor runtime is installed; no undocumented fallback |
| Installer/updater | PARTIAL | Inno Setup definition supports install/upgrade/repair/uninstall; updater verifies HTTPS, RSA-PSS manifest and package SHA-256 | Production Authenticode certificate and hosted signed manifest are external release credentials and are not in source control |
| BIOS/firmware advisor | TODO | — | Advisor only; no firmware writes will be implemented |

## Verification matrix

Automated tests run with `dotnet test tests/ProSyS.Tests`. Windows-dependent tests are skipped on other operating systems; the GitHub Actions workflow runs the complete suite on `windows-latest` after fetching PresentMon. Changes to the WPF shell (game-session tracking, close-time restore, restore-point messages) are compiled but not covered by automated UI tests.


| Feature | Environment | Verification level |
|---|---|---|
| Core planner/risk/hash/selection | .NET test process | automated |
| Registry lifecycle | isolated current-user test key | automated integration |
| System scanner | this Windows machine | runtime read-only |
| WPF launch | this Windows machine | native window creation and responsive main window verified at runtime |
| Extended scanner | this Windows machine | automated read-only integration test |
| CPU and gateway metrics | this Windows machine | automated CPU bounds test and runtime gateway probe |
| PresentMon provenance/parser | official 2.6.0 x64 binary and synthetic frame capture | automated signer/hash and statistics tests |
| Profiles/network/updater/overlay metrics | isolated temp data and this Windows machine | automated atomic round-trip, PID ownership, tamper rejection and range tests |
| Safe tweaks on personal settings | not auto-executed during development | user-confirmed runtime only |
