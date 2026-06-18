param(
    [string]$Configuration = "Release",
    [string]$PublishOutput = "artifacts\publish\win-x64",
    [string]$InstallerScript = "installer\Wyspa.iss",
    [string]$InnoSetupCompiler = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& .\scripts\package.ps1 -Configuration $Configuration -Output $PublishOutput

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue

    if ($command) {
        $InnoSetupCompiler = $command.Source
    } else {
        $candidates = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )

        $InnoSetupCompiler = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path $InnoSetupCompiler)) {
    throw "Inno Setup 6 compiler was not found. Install it from https://jrsoftware.org/isdl.php or pass -InnoSetupCompiler C:\Path\To\ISCC.exe."
}

& $InnoSetupCompiler $InstallerScript
