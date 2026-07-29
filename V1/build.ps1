param([ValidateSet('2024','2025','All')][string]$RevitVersion='All',[string]$Configuration='Release')
$ErrorActionPreference='Stop';$years=if($RevitVersion-eq'All'){@('2024','2025')}else{@($RevitVersion)}
foreach($year in $years){$dir="C:\Program Files\Autodesk\Revit $year";if(!(Test-Path "$dir\RevitAPI.dll")){throw "Revit $year API not found."};dotnet build "src\SAUDICO.Federate.Revit$year\SAUDICO.Federate.Revit$year.csproj" -c $Configuration -p:RevitYear=$year -p:RevitInstallDir="$dir" 2>&1 | Tee-Object "build-$year.log";if($LASTEXITCODE-ne 0){throw "Build $year failed. See build-$year.log."}}
dotnet test tests\SAUDICO.Federate.Tests\SAUDICO.Federate.Tests.csproj `
    -c $Configuration `
    -p:RevitYear=2024
