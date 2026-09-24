# Architecture

## Boundaries

- `ProSyS.Core`: dependency-free domain records, risk policy, graph planner, immutable plan factory and transactional execution engine.
- `ProSyS.Windows`: Windows discovery, allowlisted registry tweak adapter, tweak catalog and structured local audit logging.
- `ProSyS.App`: non-elevated WPF presentation shell.
- `ProSyS.Cli`: scan, audit, plan, explicit safe apply and rollback entry points.
- `ProSyS.Tests`: dependency-free unit and isolated HKCU integration tests.

The direction of dependencies is UI/CLI → Windows → Core. Core never invokes a shell or accepts command strings. The current mutation boundary accepts constructed `ITweak` instances only. Registry writes are constrained to a compile-time allowlist of current-user Game Bar, Game DVR, GameConfigStore, mouse and visual-effects namespaces; paths and values are not supplied by users or imported profiles.

## Transaction sequence

Scanner → compatibility → dependency order → immutable hashed plan → backup all selected tweaks → persist backup → apply one tweak → read-back verify → append audit → complete. Any exception transitions the journal to `RollbackPending`; successfully backed-up operations are restored in reverse order. A failed restore produces `RecoveryRequired`.

The privileged-service boundary is designed but deliberately not installed in MVP-0 because no current tweak requires elevation. Future elevated operations must use a versioned, authenticated named-pipe protocol with a fixed operation enum and server-side parameter validation—never arbitrary commands, paths, registry keys or scripts.
