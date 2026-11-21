using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace TinyTools.Installer.Services;

/// <summary>
/// 从安装程序内嵌资源中提取应用文件，自动防止路径穿越并保留用户 data 目录。
/// </summary>
public static class PayloadExtractor
{
    public const string EmbeddedResourceName = "TinyTools.Installer.payload.zip";

    public static bool HasEmbeddedPayload()
        => Assembly.GetExecutingAssembly().GetManifestResourceInfo(EmbeddedResourceName) is not null;

    public static async Task ExtractAsync(
        string targetDirectory,
        IProgress<(double Ratio, string CurrentFile)>? progress,
        CancellationToken cancellationToken)
    {
        using Stream? resourceStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedResourceName);
        if (resourceStream is null)
            throw new InvalidOperationException("安装包缺少内嵌的应用压缩载荷（payload.zip）。");

        Directory.CreateDirectory(targetDirectory);
        string fullTargetRoot = Path.GetFullPath(targetDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(resourceStream, ZipArchiveMode.Read, leaveOpen: false);
        var fileEntries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        int total = Math.Max(1, fileEntries.Count);

        for (int i = 0; i < fileEntries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = fileEntries[i];

            // 永远不覆盖用户 data 目录下的配置
            string normalizedEntry = entry.FullName.Replace('/', '\\').TrimStart('\\');
            if (normalizedEntry.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
                continue;

            string destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, normalizedEntry));
            if (!destinationPath.StartsWith(fullTargetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"检测到非法压缩包路径：{entry.FullName}");

            string? parentDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            await using Stream source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);

            progress?.Report(((double)(i + 1) / total, entry.Name));
        }
    }
}
