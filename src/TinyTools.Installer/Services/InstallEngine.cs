using System.Diagnostics;
using System.IO;
using System.Reflection;
using TinyTools.Installer.Models;

namespace TinyTools.Installer.Services;

/// <summary>
/// 安装引擎：统一编排全新安装、覆盖升级与卸载步骤。
/// </summary>
public sealed class InstallEngine
{
    public const string AppExecutableName = "TinyTools.WinUI.exe";
    public const string AppProcessName = "TinyTools.WinUI";
    public const string UninstallerFileName = "TinyTools-Uninstall.exe";

    public static string GetInstallerVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.1.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    public async Task<InstallExecutionResult> ExecuteAsync(
        InstallRequest request,
        IProgress<InstallProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        return request.Mode == InstallerMode.Uninstall
            ? await RunUninstallAsync(request, progress, cancellationToken)
            : await RunInstallOrUpgradeAsync(request, progress, cancellationToken);
    }

    private async Task<InstallExecutionResult> RunInstallOrUpgradeAsync(
        InstallRequest request,
        IProgress<InstallProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.InstallDirectory))
                return new InstallExecutionResult(false, "安装目录不能为空。");

            progress?.Report(new InstallProgressInfo(8, "正在关闭正在运行的 TinyTools 进程…"));
            await ProcessWaitService.EnsureStoppedAsync(
                request.WaitProcessId,
                [AppProcessName, "TinyTools"],
                cancellationToken);

            progress?.Report(new InstallProgressInfo(15, "正在解压应用文件…"));
            var extractProgress = new Progress<(double Ratio, string CurrentFile)>(p =>
            {
                double pct = 15 + p.Ratio * 68;
                progress?.Report(new InstallProgressInfo(pct, $"正在写入：{p.CurrentFile}"));
            });
            await PayloadExtractor.ExtractAsync(request.InstallDirectory, extractProgress, cancellationToken);

            string installedExe = Path.Combine(request.InstallDirectory, AppExecutableName);
            if (!File.Exists(installedExe))
                return new InstallExecutionResult(false, $"未在安装目录中找到主程序：{AppExecutableName}");

            // 复制自身作为独立卸载程序，支持在“应用和功能”中卸载
            progress?.Report(new InstallProgressInfo(86, "正在配置卸载程序与系统登记…"));
            string uninstallerPath = Path.Combine(request.InstallDirectory, UninstallerFileName);
            CopySelfAsUninstaller(uninstallerPath);
            RegistryInstallService.Register(
                request.InstallDirectory,
                installedExe,
                uninstallerPath,
                GetInstallerVersion());

            progress?.Report(new InstallProgressInfo(93, "正在创建快捷方式…"));
            if (request.CreateDesktopShortcut)
            {
                ShortcutService.CreateShortcut(
                    ShortcutService.GetDesktopShortcutPath(),
                    installedExe,
                    string.Empty,
                    request.InstallDirectory,
                    "TinyTools 桌面工具箱",
                    installedExe);
            }

            if (request.CreateStartMenuShortcut)
            {
                string startMenuDir = ShortcutService.GetStartMenuFolderPath();
                ShortcutService.CreateShortcut(
                    Path.Combine(startMenuDir, ShortcutService.ShortcutFileName),
                    installedExe,
                    string.Empty,
                    request.InstallDirectory,
                    "TinyTools 桌面工具箱",
                    installedExe);
                ShortcutService.CreateShortcut(
                    Path.Combine(startMenuDir, "卸载 TinyTools.lnk"),
                    uninstallerPath,
                    $"--uninstall --target \"{request.InstallDirectory}\"",
                    request.InstallDirectory,
                    "卸载 TinyTools",
                    installedExe);
            }

            progress?.Report(new InstallProgressInfo(100, request.Mode == InstallerMode.Upgrade ? "升级完成！" : "安装完成！"));
            return new InstallExecutionResult(
                true,
                request.Mode == InstallerMode.Upgrade
                    ? "TinyTools 已成功升级到最新版本，原有配置已完整保留。"
                    : "TinyTools 已成功安装到您的电脑。",
                installedExe);
        }
        catch (OperationCanceledException)
        {
            return new InstallExecutionResult(false, "安装已被取消。");
        }
        catch (Exception ex)
        {
            return new InstallExecutionResult(false, $"安装失败：{ex.Message}");
        }
    }

    private async Task<InstallExecutionResult> RunUninstallAsync(
        InstallRequest request,
        IProgress<InstallProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            progress?.Report(new InstallProgressInfo(15, "正在关闭正在运行的 TinyTools 进程…"));
            await ProcessWaitService.EnsureStoppedAsync(
                request.WaitProcessId,
                [AppProcessName, "TinyTools"],
                cancellationToken);

            progress?.Report(new InstallProgressInfo(40, "正在清理快捷方式与卸载登记…"));
            ShortcutService.RemoveAllShortcuts();
            RegistryInstallService.Unregister();

            progress?.Report(new InstallProgressInfo(70, "正在删除程序文件（保留用户 data 目录）…"));
            if (Directory.Exists(request.InstallDirectory))
            {
                string selfPath = Path.GetFullPath(Environment.ProcessPath ?? string.Empty);
                foreach (string file in Directory.EnumerateFiles(request.InstallDirectory))
                {
                    if (string.Equals(Path.GetFullPath(file), selfPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try { File.Delete(file); } catch { }
                }

                foreach (string dir in Directory.EnumerateDirectories(request.InstallDirectory))
                {
                    if (string.Equals(Path.GetFileName(dir), "data", StringComparison.OrdinalIgnoreCase))
                        continue;
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }

            progress?.Report(new InstallProgressInfo(100, "卸载完成。"));
            return new InstallExecutionResult(true, "TinyTools 已从您的电脑移除（data 目录中的个人配置已保留）。");
        }
        catch (Exception ex)
        {
            return new InstallExecutionResult(false, $"卸载失败：{ex.Message}");
        }
    }

    public static void LaunchApplication(string executablePath)
    {
        if (File.Exists(executablePath))
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            });
        }
    }

    private static void CopySelfAsUninstaller(string uninstallerPath)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            return;

        if (string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
            return;

        File.Copy(currentExe, uninstallerPath, overwrite: true);
    }
}
