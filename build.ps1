param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectPath = Join-Path $RepoRoot "src\DPIShift\DPIShift.csproj"
$SolutionPath = Join-Path $RepoRoot "DPIShift.sln"
$DistDir = Join-Path $RepoRoot "dist"
$TempSelfContained = Join-Path $RepoRoot "artifacts\publish-sc"
$TempLight = Join-Path $RepoRoot "artifacts\publish-light"

Write-Host "=== DPIShift Release Build Pipeline ===" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "`n[1/4] Running automated unit & integration tests..." -ForegroundColor Yellow
    dotnet test $SolutionPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
}

if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
if (Test-Path $TempSelfContained) { Remove-Item $TempSelfContained -Recurse -Force }
if (Test-Path $TempLight) { Remove-Item $TempLight -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

Write-Host "`n[2/4] Publishing Self-Contained Portable Single-File EXE (zero runtime prerequisites)..." -ForegroundColor Yellow
dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $TempSelfContained `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed." }

Copy-Item (Join-Path $TempSelfContained "DPIShift.exe") (Join-Path $DistDir "DPIShift.exe") -Force

Write-Host "`n[3/4] Publishing Ultra-Light Framework-Dependent Single-File EXE (.NET 8 Desktop Runtime)..." -ForegroundColor Yellow
dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --no-self-contained `
    -p:SelfContained=false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $TempLight `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "Light publish failed." }

Copy-Item (Join-Path $TempLight "DPIShift.exe") (Join-Path $DistDir "DPIShift-light.exe") -Force

Remove-Item (Join-Path $RepoRoot "artifacts") -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n[4/4] Generating SHA-256 checksums..." -ForegroundColor Yellow
$HashLines = @()
Get-ChildItem -Path $DistDir -Filter "*.exe" | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sizeMb = [math]::Round($_.Length / 1MB, 2)
    $HashLines += "$hash  $($_.Name)"
    Write-Host ("  -> {0,-22} {1,6} MB   SHA256: {2}" -f $_.Name, $sizeMb, $hash) -ForegroundColor Green
}

$HashLines | Set-Content -Path (Join-Path $DistDir "SHA256SUMS.txt") -Encoding UTF8

Write-Host "`nBuild succeeded! Ready-to-distribute executables are located in: dist\" -ForegroundColor Cyan
