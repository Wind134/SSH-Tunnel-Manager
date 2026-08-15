param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$OutputDir = "",
    [string]$IsccPath = "",

    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$ArtifactPrefix = "TinyTools",

    [ValidatePattern('^[A-Za-z0-9._-]+\.exe$')]
    [string]$AppExeName = "TinyTools.exe",

    [string]$AppMutex = "Local\TinyTools_SingleInstance"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $PSScriptRoot "TinyTools.iss"
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repositoryRoot "artifacts\installer"
} else {
    [System.IO.Path]::GetFullPath($OutputDir)
}

$publishedExe = Join-Path $resolvedPublishDir $AppExeName
if (-not (Test-Path $publishedExe)) {
    throw "Published application executable not found: $publishedExe"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        $IsccPath = $command.Source
    } else {
        $candidates = @(
            "C:\Program Files\Inno Setup 7\ISCC.exe",
            "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe",
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        )
        $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path $IsccPath)) {
    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 7 or pass -IsccPath."
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

Write-Host "Building TinyTools installer..." -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "Compiler: $IsccPath"
Write-Host "Published files: $resolvedPublishDir"
Write-Host "Output: $resolvedOutputDir"
Write-Host "Executable: $AppExeName"

& $IsccPath `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$resolvedPublishDir" `
    "/DMyArtifactPrefix=$ArtifactPrefix" `
    "/DMyAppExeName=$AppExeName" `
    "/DMyAppMutex=$AppMutex" `
    "/O$resolvedOutputDir" `
    $scriptPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $resolvedOutputDir "$ArtifactPrefix-v$Version-win-x64-Setup.exe"
if (-not (Test-Path $installerPath)) {
    throw "Installer output was not found: $installerPath"
}

$size = (Get-Item $installerPath).Length / 1MB
Write-Host "Installer: $installerPath" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB" -f $size)
