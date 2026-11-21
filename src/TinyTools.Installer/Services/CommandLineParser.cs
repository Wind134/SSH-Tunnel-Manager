using System.IO;
using TinyTools.Installer.Models;

namespace TinyTools.Installer.Services;

/// <summary>
/// 解析安装程序启动参数，支持全新安装、升级与卸载模式。
/// </summary>
public static class CommandLineParser
{
    public static string GetDefaultInstallDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "TinyTools");

    public static InstallerLaunchOptions Parse(string[] args)
    {
        InstallerMode mode = InstallerMode.Install;
        string? targetDir = null;
        int waitPid = 0;
        bool autoStart = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--upgrade", StringComparison.OrdinalIgnoreCase))
            {
                mode = InstallerMode.Upgrade;
                autoStart = true;
            }
            else if (arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                mode = InstallerMode.Uninstall;
            }
            else if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out waitPid);
            }
            else if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetDir = args[++i];
            }
        }

        string resolvedDir = string.IsNullOrWhiteSpace(targetDir)
            ? GetDefaultInstallDirectory()
            : targetDir.Trim().Trim('"');

        return new InstallerLaunchOptions(mode, resolvedDir, waitPid, autoStart);
    }
}
