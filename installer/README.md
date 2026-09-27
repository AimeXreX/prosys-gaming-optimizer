# Installer and signing

`ProSyS.iss` creates a per-machine x64 installer with Start-menu integration, desktop shortcut, upgrade/repair behavior, a Windows 11 check, an optional Performance Log Users grant for PresentMon, and Windows uninstall registration.

Releases are built by `.github/workflows/release.yml`. Every push builds the installer and uploads it as a workflow artifact. A GitHub Release is published automatically when `main` receives a `<Version>` (in `Directory.Build.props`) that has no release yet, when a `v*` tag is pushed, or when the workflow is run manually. The workflow tests, publishes the app self-contained for `win-x64`, compiles this script, and attaches the installer, a portable ZIP and `SHA256SUMS.txt` to a GitHub Release. `installer/RELEASE_NOTES.md` is used as the release description.

To build locally:

```powershell
./tools/PresentMon/fetch.ps1
dotnet publish src/ProSyS.App -c Release -r win-x64 --self-contained true -o artifacts/app
ISCC.exe /DAppVersion=1.2.0 installer\ProSyS.iss
```

Production distribution must sign both executables and the installer with the publisher's Authenticode certificate. No private signing key is stored in this repository. The updater accepts only HTTPS packages whose RSA-PSS signed manifest and SHA-256 package digest both verify.
