param([Parameter(Mandatory=$true)][ValidateSet('2024','2025')][string]$RevitVersion,[string]$Configuration='Release')

$ErrorActionPreference='Stop'
& "$PSScriptRoot\build.ps1" -RevitVersion $RevitVersion -Configuration $Configuration

$f=if($RevitVersion-eq'2025'){'net8.0-windows'}else{'net48'}
$src="$PSScriptRoot\src\Revit$RevitVersion\bin\$Configuration\$f"
$root="$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"
$dst="$root\SAUDICO.Federate"
$manifest="$root\SAUDICO.Federate.Revit$RevitVersion.addin"
$assemblyName="SAUDICO.Federate.Revit$RevitVersion.dll"
$assemblyPath = Join-Path $dst $assemblyName

Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Force -ItemType Directory $dst | Out-Null
Copy-Item "$src\*" $dst -Recurse -Force

[xml]$addinManifest = Get-Content "$PSScriptRoot\src\Revit$RevitVersion\SAUDICO.Federate.Revit$RevitVersion.addin"
$addinManifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly').InnerText = $assemblyPath
$addinManifest.Save($manifest)

Write-Host "Installed Revit $RevitVersion" -ForegroundColor Green
