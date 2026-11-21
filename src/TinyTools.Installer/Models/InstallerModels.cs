namespace TinyTools.Installer.Models;

public enum InstallerMode
{
    Install,
    Upgrade,
    Uninstall,
}

public sealed record InstallerLaunchOptions(
    InstallerMode Mode,
    string InstallDirectory,
    int WaitProcessId,
    bool AutoStart);

public sealed record InstallRequest(
    InstallerMode Mode,
    string InstallDirectory,
    int WaitProcessId,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    bool LaunchAfterInstall);

public sealed record InstallProgressInfo(double Percentage, string Message);

public sealed record InstallExecutionResult(bool Succeeded, string Message, string? InstalledExecutablePath = null);
