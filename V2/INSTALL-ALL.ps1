param(
    [switch]$Revit2024Only,
    [switch]$Revit2025Only
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
Unblock-File -LiteralPath '.\build.ps1' -ErrorAction SilentlyContinue
Unblock-File -LiteralPath '.\publish.ps1' -ErrorAction SilentlyContinue
if (-not $Revit2025Only) {
    & "$PSScriptRoot\publish.ps1" -RevitVersion 2024
}
if (-not $Revit2024Only) {
    & "$PSScriptRoot\publish.ps1" -RevitVersion 2025
}
Write-Host 'SAUDICO Federate build and publication completed.' -ForegroundColor Green
