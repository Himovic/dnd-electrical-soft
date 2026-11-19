# Builds the solution and runs all test suites (Windows / PowerShell).
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet build DND-Electrical-Engineering-Suite.sln -c $Configuration
foreach ($suite in "UnitTests", "IntegrationTests", "UIAutomationTests") {
    dotnet test "tests/DND.Electrical.$suite" -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $suite" }
}
