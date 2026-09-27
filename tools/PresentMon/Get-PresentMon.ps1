<#
.SYNOPSIS
  Downloads the official PresentMon 2.6.0 x64 binary and verifies it against the SHA-256 pinned in manifest.json.
  The binary is not committed to source control; run this once before building a release or running the benchmark tests.
#>
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'manifest.json') | ConvertFrom-Json
$targetDir = Join-Path $PSScriptRoot $manifest.version
$target = Join-Path $targetDir 'PresentMon.exe'
$url = "https://github.com/GameTechDev/PresentMon/releases/download/v$($manifest.version)/$($manifest.asset)"

if (Test-Path $target) {
    if ((Get-FileHash -Algorithm SHA256 -Path $target).Hash -eq $manifest.sha256) { Write-Host "PresentMon $($manifest.version) already present and verified."; return }
    Remove-Item $target -Force
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$temp = "$target.download"
Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
$hash = (Get-FileHash -Algorithm SHA256 -Path $temp).Hash
if ($hash -ne $manifest.sha256) {
    Remove-Item $temp -Force
    throw "SHA-256 mismatch for $url. Expected $($manifest.sha256), got $hash."
}
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $signature = Get-AuthenticodeSignature -FilePath $temp
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Intel Corporation*') {
        Remove-Item $temp -Force
        throw "Authenticode verification failed: $($signature.Status) $($signature.SignerCertificate.Subject)"
    }
}
Move-Item -Force $temp $target
Write-Host "PresentMon $($manifest.version) downloaded and verified: $target"
