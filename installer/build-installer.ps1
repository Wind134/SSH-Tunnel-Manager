param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$OutputDir = "",

    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$ArtifactPrefix = "TinyTools-WinUI",

    [ValidatePattern('^[A-Za-z0-9._-]+\.exe$')]
    [string]$AppExeName = "TinyTools.WinUI.exe"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$installerProject = Join-Path $repositoryRoot "src\TinyTools.Installer\TinyTools.Installer.csproj"
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $repositoryRoot "artifacts\installer"
} else {
    [System.IO.Path]::GetFullPath($OutputDir)
}

$publishedExe = Join-Path $resolvedPublishDir $AppExeName
if (-not (Test-Path $publishedExe)) {
    throw "未找到发布的应用主程序：$publishedExe"
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("TinyTools-Installer-Build-" + [Guid]::NewGuid().ToString("N"))
$payloadZip = Join-Path $tempRoot "payload.zip"
$installerPublishDir = Join-Path $tempRoot "publish"
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    Write-Host "正在打包应用载荷 payload.zip..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $resolvedPublishDir "*") -DestinationPath $payloadZip -CompressionLevel Optimal

    Write-Host "正在编译自研单文件安装程序..." -ForegroundColor Cyan
    dotnet publish $installerProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -p:PayloadZipPath=$payloadZip `
        -o $installerPublishDir

    if ($LASTEXITCODE -ne 0) {
        throw "安装程序发布失败，退出码：$LASTEXITCODE"
    }

    $builtExe = Join-Path $installerPublishDir "TinyTools.Installer.exe"
    if (-not (Test-Path $builtExe)) {
        throw "未生成安装程序文件：$builtExe"
    }

    $finalInstallerPath = Join-Path $resolvedOutputDir "$ArtifactPrefix-v$Version-win-x64-Setup.exe"
    Copy-Item -Path $builtExe -Destination $finalInstallerPath -Force

    $sizeMb = (Get-Item $finalInstallerPath).Length / 1MB
    Write-Host "自定义安装程序已生成：$finalInstallerPath" -ForegroundColor Green
    Write-Host ("体积：{0:N1} MB" -f $sizeMb)
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
