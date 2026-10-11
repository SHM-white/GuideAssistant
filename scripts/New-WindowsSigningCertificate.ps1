#requires -Version 5.1
<#
.SYNOPSIS
  Creates a reusable self-signed MSIX code-signing certificate for GuideAssistant.
.NOTES
  Use an existing publisher certificate when possible. Never commit the PFX.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $env:USERPROFILE 'GuideAssistant-signing')
)

$ErrorActionPreference = 'Stop'
$subject = 'CN=SHM_white'
$pfxFile = Join-Path $OutputDirectory 'GuideAssistant-signing.pfx'
$cerFile = Join-Path $OutputDirectory 'GuideAssistant-signing.cer'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if ((Test-Path -LiteralPath $pfxFile) -or (Test-Path -LiteralPath $cerFile)) {
    throw "Certificate files already exist in '$OutputDirectory'. Reuse them instead of replacing the publisher certificate."
}

$password = Read-Host 'Choose a strong PFX password (keep it safe)' -AsSecureString
if ($password.Length -eq 0) { throw 'The PFX password cannot be empty.' }
$certificate = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject $subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(3)
Export-PfxCertificate -Cert $certificate -FilePath $pfxFile -Password $password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerFile | Out-Null

Write-Host "PFX (keep secret): $pfxFile"
Write-Host "Public certificate: $cerFile"
Write-Host "Certificate Subject: $($certificate.Subject)"
Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
Write-Host ''
Write-Host 'Add these GitHub Actions repository secrets:'
Write-Host '  WINDOWS_CERTIFICATE_BASE64 = the Base64 string produced by running locally:'
Write-Host "  [Convert]::ToBase64String([IO.File]::ReadAllBytes('$pfxFile'))"
Write-Host '  WINDOWS_CERTIFICATE_PASSWORD = the password you entered above'
Write-Host 'Do not put either secret into Git commits, Issues, or Actions logs.'
Write-Host 'See RELEASE.md for trust/installation instructions.'
