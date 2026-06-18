$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet test .\tests\Wyspa.Tests\Wyspa.Tests.csproj --configuration Release
