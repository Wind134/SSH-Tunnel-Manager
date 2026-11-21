using Microsoft.Win32;

namespace TinyTools.Installer.Services;

/// <summary>
/// 在当前用户注册表中登记或移除 Windows“应用和功能”卸载项。
/// </summary>
public static class RegistryInstallService
{
    private const string UninstallSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TinyTools";

    public static void Register(
        string installDirectory,
        string executablePath,
        string uninstallerPath,
        string displayVersion)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(UninstallSubKeyPath, writable: true);
        if (key is null)
            return;

        key.SetValue("DisplayName", "TinyTools");
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("Publisher", "AI开发工具");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", executablePath);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall --target \"{installDirectory}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallSubKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 卸载清理注册表失败不阻断流程
        }
    }
}
