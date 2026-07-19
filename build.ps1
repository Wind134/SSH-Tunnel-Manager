# Build script for SSH Tunnel Manager
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "src\SSHTunnelManager"
$publishDir = Join-Path $projectDir $OutputDir

Write-Host "=== SSH Tunnel Manager Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project: $projectDir"

Write-Host ""
Write-Host "Checking .NET SDK..." -NoNewline
try {
    $sdkVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -ne 0) { throw "dotnet not found" }
    Write-Host " OK ($sdkVersion)" -ForegroundColor Green
} catch {
    Write-Host " NOT FOUND" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install .NET SDK 8.0+ from:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

Write-Host ""
Write-Host "Restoring packages..." -ForegroundColor Cyan
Push-Location $projectDir
dotnet restore
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "Restore failed." -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o $publishDir

if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "Publish failed." -ForegroundColor Red; exit 1 }
Pop-Location

$exePath = Join-Path $publishDir "SSHTunnelManager.exe"
Write-Host ""
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host "=== Build Successful ===" -ForegroundColor Green
    Write-Host "Output: $exePath"
    Write-Host ("Size: {0:N1} MB" -f $size)
} else {
    Write-Host "=== Build Failed ===" -ForegroundColor Red
    exit 1
}
