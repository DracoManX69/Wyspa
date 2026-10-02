param(
    [Parameter(Mandatory)][ValidateSet('Resolve', 'Verify')][string]$Action,
    [Parameter(Mandatory)][string]$Directory
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$metadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$manifestPath = Join-Path $Directory 'desktop-runtime.json'
try {
    if ($Action -eq 'Resolve') {
        # Select a stable release in the app's compatible major/minor line, not a preview or SDK.
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $releases = Invoke-RestMethod -Uri $metadataUrl -TimeoutSec 120
        $version = [string]$releases.'latest-runtime'
        if ($version -notmatch '^10\.0\.\d+$') { throw 'Microsoft did not return a stable .NET 10.0 release.' }
        $release = @($releases.releases | Where-Object { $_.windowsdesktop.version -eq $version })
        if ($release.Count -ne 1) { throw 'Desktop Runtime release metadata was ambiguous.' }
        $file = @($release[0].windowsdesktop.files | Where-Object {
            $_.rid -eq 'win-x64' -and $_.name -eq 'windowsdesktop-runtime-win-x64.exe'
        })
        if ($file.Count -ne 1) { throw 'The x64 Desktop Runtime installer was not found.' }
        $uri = [Uri]$file[0].url
        if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com') -or
            [string]$file[0].hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'Unexpected runtime download metadata.' }
        @{ Version = $version; Url = $uri.AbsoluteUri; Hash = [string]$file[0].hash } |
            ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        "[Runtime]`r`nVersion=$version`r`nUrl=$($uri.AbsoluteUri)" |
            Set-Content -LiteralPath (Join-Path $Directory 'desktop-runtime.ini') -Encoding ASCII
    } else {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $installer = Join-Path $Directory 'windowsdesktop-runtime.exe'
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ne $manifest.Hash) {
            throw 'The runtime download does not match the SHA-512 published by Microsoft.'
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -notin @('Microsoft Corporation', '.NET') -or
            $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
            throw 'The runtime installer does not have a valid Microsoft signature.'
        }
        $info = (Get-Item -LiteralPath $installer).VersionInfo
        if ($info.ProductName -ne "Microsoft Windows Desktop Runtime $($manifest.Version) (x64)") {
            throw 'The downloaded file is not the selected x64 Desktop Runtime.'
        }
    }
    exit 0
} catch {
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $Directory 'desktop-runtime-error.txt') -Encoding UTF8
    exit 1
}
