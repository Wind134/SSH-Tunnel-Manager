# Build script for TinyTools
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "src\TinyTools"
$publishDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
} else {
    Join-Path $projectDir $OutputDir
}

Write-Host "=== TinyTools Build ===" -ForegroundColor Cyan
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
dotnet restore (Join-Path $projectDir "TinyTools.csproj")
if ($LASTEXITCODE -ne 0) { Write-Host "Restore failed." -ForegroundColor Red; exit 1 }

if (-not $SkipTests) {
    $testProject = Join-Path $PSScriptRoot "tests\TinyTools.Tests\TinyTools.Tests.csproj"
    if (Test-Path $testProject) {
        Write-Host ""
        Write-Host "Running tests..." -ForegroundColor Cyan
        dotnet test $testProject -c $Configuration
        if ($LASTEXITCODE -ne 0) { Write-Host "Tests failed." -ForegroundColor Red; exit 1 }
    }
}

Write-Host ""
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish (Join-Path $projectDir "TinyTools.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir

if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit 1 }

# Do not ship stale PDB files when reusing an existing output directory.
Get-ChildItem -Path $publishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$exePath = Join-Path $publishDir "TinyTools.exe"
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
