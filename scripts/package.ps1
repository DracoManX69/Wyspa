param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\publish\win-x64",
    [switch]$SelfContained,
    [switch]$SingleFile,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
& (Join-Path $PSScriptRoot "prepare-local-model.ps1")

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$singleFileValue = if ($SingleFile -or $SelfContained) { "true" } else { "false" }

function Find-BytePattern {
    param(
        [byte[]]$Bytes,
        [byte[]]$Pattern,
        [int]$StartIndex = 0
    )

    for ($i = $StartIndex; $i -le $Bytes.Length - $Pattern.Length; $i++) {
        $matched = $true

        for ($j = 0; $j -lt $Pattern.Length; $j++) {
            if ($Bytes[$i + $j] -ne $Pattern[$j]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            return $i
        }
    }

    return -1
}

function Set-AppHostRelativePath {
    param(
        [string]$ExePath,
        [string]$OriginalPath,
        [string]$RelativePath
    )

    $bytes = [System.IO.File]::ReadAllBytes($ExePath)
    $originalBytes = [System.Text.Encoding]::UTF8.GetBytes($OriginalPath)
    $relativeBytes = [System.Text.Encoding]::UTF8.GetBytes($RelativePath)
    $matchIndex = -1
    $searchIndex = 0

    while ($true) {
        $candidateIndex = Find-BytePattern -Bytes $bytes -Pattern $originalBytes -StartIndex $searchIndex

        if ($candidateIndex -lt 0) {
            break
        }

        $hasPadding = $true
        for ($i = $originalBytes.Length; $i -lt $relativeBytes.Length; $i++) {
            if ($bytes[$candidateIndex + $i] -ne 0) {
                $hasPadding = $false
                break
            }
        }

        if ($hasPadding) {
            $matchIndex = $candidateIndex
            break
        }

        $searchIndex = $candidateIndex + 1
    }

    if ($matchIndex -lt 0) {
        throw "Could not update app host path from '$OriginalPath' to '$RelativePath'."
    }

    [Array]::Copy($relativeBytes, 0, $bytes, $matchIndex, $relativeBytes.Length)
    [System.IO.File]::WriteAllBytes($ExePath, $bytes)
}

foreach ($requiredTool in @("Tools\Video\yt-dlp.exe", "Tools\Video\ffmpeg.exe")) {
    if (-not (Test-Path (Join-Path "src\Wyspa.App" $requiredTool))) {
        throw "Missing bundled dependency: $requiredTool. Run python scripts/prepare-v7-tools.py before packaging."
    }
}

if (-not $SkipPublish) {
dotnet publish .\src\Wyspa.App\Wyspa.App.csproj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=$singleFileValue `
    -p:IncludeNativeLibrariesForSelfExtract=$selfContainedValue `
    -o $Output

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

}

# Runtime packages also carry other OS/architecture binaries. This installer is win-x64.
$runtimePath = Join-Path $Output "runtimes"
if (Test-Path $runtimePath) {
    Get-ChildItem $runtimePath -Directory -Recurse |
        Where-Object { $_.Name -match '^(linux-|osx-|android-|ios-|maccatalyst-|tvos-|win-arm64$|win-x86$)' } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { if (Test-Path $_.FullName) { Remove-Item $_.FullName -Recurse -Force } }
}

if (-not $SelfContained -and -not $SingleFile) {
    $publishPath = (Resolve-Path $Output).ProviderPath
    $exePath = Join-Path $publishPath "Wyspa.exe"
    $dataPath = Join-Path $publishPath "Data"

    if (-not (Test-Path $exePath)) {
        throw "Could not find Wyspa.exe in publish output."
    }

    if ((Test-Path $dataPath) -and -not (Test-Path (Join-Path $publishPath "Wyspa.dll"))) {
        throw "Output is already packaged. Publish to a fresh directory before packaging again."
    }

    if (Test-Path $dataPath) {
        Remove-Item $dataPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $dataPath | Out-Null
    Set-AppHostRelativePath -ExePath $exePath -OriginalPath "Wyspa.dll" -RelativePath "Data\Wyspa.dll"

    Get-ChildItem -Path $publishPath -Force |
        Where-Object { $_.Name -ne "Wyspa.exe" -and $_.Name -ne "Data" } |
        Move-Item -Destination $dataPath -Force
}
