using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TinyTools.Installer.Services;

/// <summary>
/// 通过 Windows 原生 Shell COM 接口创建与清理桌面/开始菜单快捷方式。
/// </summary>
public static class ShortcutService
{
    public const string ShortcutFileName = "TinyTools.lnk";
    public const string StartMenuFolderName = "TinyTools";

    public static string GetDesktopShortcutPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutFileName);

    public static string GetStartMenuFolderPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            StartMenuFolderName);

    public static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string description,
        string? iconPath = null)
    {
        string? dir = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        object comObject = new ShellLink();
        var shellLink = (IShellLinkW)comObject;
        shellLink.SetPath(targetPath);
        shellLink.SetArguments(arguments);
        shellLink.SetWorkingDirectory(workingDirectory);
        shellLink.SetDescription(description);
        if (!string.IsNullOrWhiteSpace(iconPath))
            shellLink.SetIconLocation(iconPath, 0);

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);
    }

    public static void RemoveAllShortcuts()
    {
        try
        {
            string desktop = GetDesktopShortcutPath();
            if (File.Exists(desktop))
                File.Delete(desktop);
        }
        catch { }

        try
        {
            string startMenu = GetStartMenuFolderPath();
            if (Directory.Exists(startMenu))
                Directory.Delete(startMenu, recursive: true);
        }
        catch { }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
