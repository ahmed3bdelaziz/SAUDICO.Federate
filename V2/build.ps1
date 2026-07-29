param(
    [ValidateSet('2024','2025')][string]$RevitVersion = '2025',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$revitDir = "C:\Program Files\Autodesk\Revit $RevitVersion"
if (-not (Test-Path "$revitDir\RevitAPI.dll")) {
    throw "Revit $RevitVersion API not found: $revitDir"
}
Get-ChildItem -LiteralPath $PSScriptRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj') } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force
$project = "src\Revit$RevitVersion\Revit$RevitVersion.csproj"
dotnet restore $project -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir"
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
dotnet build $project -c $Configuration -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir" --no-restore 2>&1 |
    Tee-Object "build-$RevitVersion.log"
if ($LASTEXITCODE -ne 0) { throw "Build failed. Review build-$RevitVersion.log" }
