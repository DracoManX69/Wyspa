param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
try {
    $installer = Join-Path $Directory 'vc_redist.x64.exe'
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne 'CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B') {
        throw 'The C++ runtime does not match the verified Microsoft package.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Microsoft Corporation' -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw 'The C++ runtime does not have a valid Microsoft signature.'
    }
    $info = (Get-Item -LiteralPath $installer).VersionInfo
    if ($info.ProductName -notmatch '^Microsoft Visual C\+\+.*\(x64\)' -or $info.ProductVersion -ne '14.44.35211.0') {
        throw 'The download is not the required x64 C++ runtime.'
    }
    exit 0
} catch {
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $Directory 'cpp-runtime-error.txt') -Encoding UTF8
    exit 1
}
