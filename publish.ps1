param([Parameter(Mandatory=$true)][ValidateSet('2024','2025')][string]$RevitVersion,[string]$Configuration='Release')

$ErrorActionPreference='Stop'
& "$PSScriptRoot\build.ps1" -RevitVersion $RevitVersion -Configuration $Configuration

$framework=if($RevitVersion-eq'2025'){'net8.0-windows'}else{'net48'}
$source="$PSScriptRoot\src\Revit$RevitVersion\bin\$Configuration\$framework"
$addinRoot="$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"
$destination="$addinRoot\SAUDICO.Federate"
$manifest="$addinRoot\SAUDICO.Federate.Revit$RevitVersion.addin"
$assemblyPath=Join-Path $destination "SAUDICO.Federate.Revit$RevitVersion.dll"

Remove-Item $destination -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Force -ItemType Directory $destination | Out-Null
Copy-Item "$source\*" $destination -Recurse -Force

[xml]$addinManifest=Get-Content "$PSScriptRoot\src\Revit$RevitVersion\SAUDICO.Federate.Revit$RevitVersion.addin"
$addinManifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly').InnerText=$assemblyPath
$addinManifest.Save($manifest)

$localOverride="$PSScriptRoot\config\apssettings.local.json"
if(Test-Path $localOverride){Copy-Item $localOverride "$destination\config\apssettings.local.json" -Force}
Write-Host "Installed Revit $RevitVersion" -ForegroundColor Green
