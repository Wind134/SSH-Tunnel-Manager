using System.Diagnostics;

namespace TinyTools.Core.Updates;

/// <summary>
/// 构造并启动自定义安装包（exe）的升级参数。
/// </summary>
public static class UpdateInstallerLauncher
{
    /// <summary>
    /// 生成升级模式启动信息，通知安装程序等待当前进程退出并覆盖原目录。
    /// </summary>
    public static ProcessStartInfo CreateUpgradeStartInfo(
        string installerPath,
        int currentProcessId,
        string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
            throw new ArgumentException("安装包路径不能为空。", nameof(installerPath));
        if (!installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("仅支持直接运行 .exe 安装包。");

        var startInfo = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("--upgrade");
        if (currentProcessId > 0)
        {
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(currentProcessId.ToString());
        }

        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(targetDirectory);
        }

        return startInfo;
    }

    /// <summary>
    /// 启动升级安装程序。
    /// </summary>
    public static void LaunchUpgrade(
        string installerPath,
        int currentProcessId,
        string? targetDirectory)
    {
        ProcessStartInfo startInfo = CreateUpgradeStartInfo(
            installerPath, currentProcessId, targetDirectory);
        Process.Start(startInfo);
    }
}
