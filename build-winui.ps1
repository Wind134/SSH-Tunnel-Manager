# Build the WinUI 3 migration preview without replacing the stable WPF release.
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts\winui-preview",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\TinyTools.WinUI\TinyTools.WinUI.csproj"
$output = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
} else {
    Join-Path $PSScriptRoot $OutputDir
}

if (-not $SkipTests) {
    $testProject = Join-Path $PSScriptRoot "tests\TinyTools.Tests\TinyTools.Tests.csproj"
    dotnet test $testProject -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishProfile=win-x64 -o $output --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem -Path $output -Filter "*.pdb" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$exe = Join-Path $output "TinyTools.WinUI.exe"
if (-not (Test-Path $exe)) {
    throw "WinUI publish did not produce $exe"
}

Write-Host "WinUI preview: $exe" -ForegroundColor Green
