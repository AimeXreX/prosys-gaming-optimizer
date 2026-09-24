# Installer and signing

`ProSyS.iss` creates a per-machine x64 installer with Start-menu integration, optional desktop shortcut, upgrade/repair behavior, and Windows uninstall registration. Build the published app first, then compile with Inno Setup 6:

```powershell
ISCC.exe installer\ProSyS.iss
```

Production distribution must sign both executables and the installer with the publisher's Authenticode certificate. No private signing key is stored in this repository. The updater accepts only HTTPS packages whose RSA-PSS signed manifest and SHA-256 package digest both verify.
