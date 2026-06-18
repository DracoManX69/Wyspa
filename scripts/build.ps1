param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet restore .\Wyspa.slnx
dotnet build .\Wyspa.slnx --configuration $Configuration --no-restore
