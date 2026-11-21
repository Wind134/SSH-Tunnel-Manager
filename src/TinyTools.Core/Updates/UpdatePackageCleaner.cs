namespace TinyTools.Core.Updates;

/// <summary>
/// 清理已完成升级的历史更新安装包，避免持续占用本地磁盘空间。
/// </summary>
public static class UpdatePackageCleaner
{
    public static string GetUpdatesRootDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyTools",
            "Updates");

    /// <summary>
    /// 删除版本号小于或等于当前运行版本的已下载更新目录，保留尚未安装的更高版本包。
    /// </summary>
    public static int CleanupInstalledPackages(string updatesRootDirectory, Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(updatesRootDirectory) || !Directory.Exists(updatesRootDirectory))
            return 0;

        Version normalizedCurrent = NormalizeVersion(currentVersion);
        int deletedCount = 0;

        foreach (string subDir in Directory.EnumerateDirectories(updatesRootDirectory))
        {
            string folderName = Path.GetFileName(subDir);
            if (!TryParseVersion(folderName, out Version? folderVersion))
                continue;

            if (folderVersion > normalizedCurrent)
                continue;

            try
            {
                Directory.Delete(subDir, recursive: true);
                deletedCount++;
            }
            catch
            {
                // 文件正被占用时跳过，等待下次启动再清理
            }
        }

        return deletedCount;
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string trimmed = value.Trim().TrimStart('v', 'V');
        if (Version.TryParse(trimmed, out Version? parsed))
        {
            version = NormalizeVersion(parsed);
            return true;
        }

        version = null;
        return false;
    }

    private static Version NormalizeVersion(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
