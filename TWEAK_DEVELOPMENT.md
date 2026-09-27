# Tweak development

Every tweak implements `ITweak` and supplies a stable ID, revision, reason, risk vector, benefit/evidence classification, build scope, dependency metadata and recovery description. Its lifecycle is detect → compatibility → backup → apply → verify → rollback → verify rollback.

A tweak is not eligible for the Safe profile unless it is evidence-backed, easy to reverse, has no security impact, and has at most low overall risk and very-low stability risk. Tests must cover original-value capture, read-back verification, absent-value rollback and repeated rollback. Hardware-sensitive, undocumented or security-reducing changes must not be added to Safe.

The offered catalog (`TweakCatalog.CreateTweaks`) is deliberately small: every entry must map to a documented Windows setting and carry a `Reference` naming where the user can see it (a Settings page or Control Panel dialog). Only Game Mode is selected by default. Full-screen-optimization (`GameConfigStore`), DWM internals and settings that only exist under HKLM are out of scope.

Besides registry values, `ITweak` is implemented by `PowerPlanTweak` (powrprof API) and `DisplayRefreshTweak` (user32 display modes); both capture and restore the exact original state. Per-game settings (the GPU preference) encode the executable path in their ID (`gaming.gpu-preference:<path>`), and `TweakCatalog.ResolveDynamic` rebuilds them so their backups can be restored without the game profile.

Never delete a capability ID that has shipped: move it to the retired list in `CreateRollbackCatalog` so backups taken by earlier versions can still be restored.
