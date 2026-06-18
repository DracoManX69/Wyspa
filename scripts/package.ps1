param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\publish\win-x64",
    [switch]$SelfContained,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

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

dotnet publish .\src\Wyspa.App\Wyspa.App.csproj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=$singleFileValue `
    -p:IncludeNativeLibrariesForSelfExtract=$selfContainedValue `
    -o $Output

if (-not $SelfContained -and -not $SingleFile) {
    $publishPath = Resolve-Path $Output
    $exePath = Join-Path $publishPath "Wyspa.exe"
    $dataPath = Join-Path $publishPath "Data"

    if (-not (Test-Path $exePath)) {
        throw "Could not find Wyspa.exe in publish output."
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
