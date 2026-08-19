# Generates the self-signed certificate the VSTO project uses to sign its ClickOnce manifests,
# and points the project at it.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\new-signing-key.ps1
#
# The key is intentionally not in source control. It is a throwaway per-developer identity:
# self-signed, untrusted by Windows, and valid for a year. Sharing one across everyone who
# uses this template would give them all the same publisher identity, so anyone holding the
# key could sign an update that the victim's machine accepts as the same publisher.
#
# For actual distribution, replace it with a code-signing certificate from a certificate
# authority. A self-signed key always shows "Unknown Publisher" on first install.

[CmdletBinding()]
param(
    [string]$Subject = "CN=$env:USERNAME",
    [int]$ValidYears = 1
)

$ErrorActionPreference = 'Stop'

$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\DataverseAddIn.Excel\DataverseAddIn.Excel.csproj'
$keyPath = Join-Path (Split-Path -Parent $project) 'DataverseAddIn.Excel_TemporaryKey.pfx'

if (-not (Test-Path $project)) { throw "Project not found: $project" }

Write-Host "Creating a code-signing certificate for $Subject ..." -ForegroundColor Cyan

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears($ValidYears)

# Visual Studio writes this file with no password, and MSBuild reads it the same way. Adding
# one here would make the build prompt for it.
[IO.File]::WriteAllBytes($keyPath, $certificate.Export('Pfx', ''))

# The project pins the thumbprint, so a new key without this edit fails the build.
$content = Get-Content $project -Raw
$updated = [regex]::Replace(
    $content,
    '(<ManifestCertificateThumbprint>)[^<]*(</ManifestCertificateThumbprint>)',
    "`${1}$($certificate.Thumbprint)`${2}")

if ($updated -eq $content) {
    Write-Warning "No <ManifestCertificateThumbprint> found in the project; nothing to update."
} else {
    Set-Content -Path $project -Value $updated -NoNewline
}

Write-Host "  key        : $keyPath"
Write-Host "  thumbprint : $($certificate.Thumbprint)"
Write-Host "  expires    : $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "  store      : Cert:\CurrentUser\My\$($certificate.Thumbprint)"
Write-Host ''
Write-Host 'Done. The thumbprint change to the .csproj is local to you - do not commit it.' -ForegroundColor Yellow
