# Recovery

Backups live in `%LOCALAPPDATA%\ProSySOptimizer\Backups\<session-id>`. Each folder contains `backups.json` and an atomically replaced `journal.json`.

Use **Undo last session** in the app or:

```powershell
dotnet run --project src/ProSyS.Cli -c Release -- restore last
```

Rollback restores the captured value and registry type. If the value did not exist before optimization, rollback deletes that value. Repeating rollback is supported and tested. A `RecoveryRequired` result means at least one value could not be read back as its original state; inspect the session journal and audit log before making additional changes.
