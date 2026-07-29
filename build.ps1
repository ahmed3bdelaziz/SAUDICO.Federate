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

# 1. Clean the required bin and obj folders.
Get-ChildItem -LiteralPath $PSScriptRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in @('bin','obj') } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force

$project = "src\Revit$RevitVersion\Revit$RevitVersion.csproj"

# 2. Restore packages.
dotnet restore $project -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir"
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }

# 3. Build the selected Revit host.
dotnet build $project -c $Configuration -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir" --no-restore 2>&1 |
    Tee-Object "build-$RevitVersion.log"
if ($LASTEXITCODE -ne 0) { throw "Build failed. Review build-$RevitVersion.log" }

# 4. If build succeeds, run unit tests. 5. If tests fail, fail the build with a clear message.
$testProject = "tests\SAUDICO.Federate.Tests\SAUDICO.Federate.Tests.csproj"

dotnet restore $testProject -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir"
if ($LASTEXITCODE -ne 0) { throw 'Test project package restore failed.' }

dotnet test $testProject -c $Configuration -p:RevitYear=$RevitVersion -p:RevitDir="$revitDir" --no-restore 2>&1 |
    Tee-Object "test-$RevitVersion.log"
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed. Review test-$RevitVersion.log" }

# 6. Preserve build logs — build-$RevitVersion.log and test-$RevitVersion.log are left in place, not deleted.
