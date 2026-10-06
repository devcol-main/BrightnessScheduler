<#
.SYNOPSIS
  Authenticode-signs the exes in .\dist on this PC (local / manual signing).

  Official releases are signed in GitHub Actions via SignPath (see CODE_SIGNING.md).
  This script is for local testing or if you own a code-signing certificate yourself.

.EXAMPLE
  ./sign.ps1 -CreateTestCert          # create a self-signed test cert (CurrentUser\My) and sign with it
  ./sign.ps1 -Thumbprint 1A2B...      # sign with a cert already in the store (e.g. Certum SimplySign)
  ./sign.ps1 -PfxPath cert.pfx        # sign with a .pfx file (asks for the password)

.NOTES
  A self-signed certificate is NOT trusted by other PCs. It only proves the pipeline works.
#>
param(
    [string]$Thumbprint,
    [string]$PfxPath,
    [switch]$CreateTestCert,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [string]$Path = (Join-Path $PSScriptRoot "dist")
)
$ErrorActionPreference = "Stop"
$subject = "CN=BrightnessScheduler Test Signing"

if ($CreateTestCert) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object Subject -eq $subject | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
            -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3)
        Write-Host "Created test certificate $($cert.Thumbprint)" -ForegroundColor Yellow
    }
}
elseif ($PfxPath) {
    $pwd = Read-Host "PFX password" -AsSecureString
    $cert = Get-PfxCertificate -FilePath $PfxPath -Password $pwd
}
elseif ($Thumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object Thumbprint -eq $Thumbprint | Select-Object -First 1
    if (-not $cert) { throw "Certificate $Thumbprint not found in CurrentUser\My or LocalMachine\My" }
}
else {
    throw "Specify -Thumbprint, -PfxPath or -CreateTestCert"
}

Get-ChildItem $Path -Filter *.exe | ForEach-Object {
    $r = Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $cert -HashAlgorithm SHA256 `
        -TimestampServer $TimestampServer -IncludeChain NotRoot
    "{0,-50} {1}" -f $_.Name, $r.Status
}
