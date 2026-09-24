# Security and threat model

Assets are original system state, backup integrity and the user's Windows session. Threats include malicious profile data, command/path injection, confused-deputy elevation, tampered update artifacts and disclosure through logs.

MVP mitigations:

- no elevated GUI, shell execution for mutation, imported executable content, telemetry or network communication;
- compile-time tweak catalog and constructor-enforced HKCU gaming-settings allowlist;
- backup before mutation, immutable-plan hash validation, verification after mutation and read-back verification after automatic rollback;
- audit logs exclude usernames, IP addresses, secrets and full machine identifiers;
- machine name is stored only as a truncated SHA-256 derivative.

Not yet implemented: signed updater, authenticated privileged service, profile import, package signing and installer hardening. These remain unavailable rather than simulated.
