# Changelog

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
