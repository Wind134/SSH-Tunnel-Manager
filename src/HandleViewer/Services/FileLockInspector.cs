using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HandleViewer.Models;

namespace HandleViewer.Services;

/// <summary>
/// Uses the Windows Restart Manager API (rstrtmgr.dll) to find processes using
/// a file or files contained in a directory.
/// </summary>
public static class FileLockInspector
{
    public const int MaxDirectoryFileCount = 4096;

    private const int RmRebootReasonNone = 0;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle,
        uint nFiles, string[] rgsFilenames,
        uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

    public static List<FileLockEntry> GetFileLockers(string filePath)
        => GetPathLockers(filePath).Entries.ToList();

    /// <summary>
    /// Checks a single file, or recursively checks files contained in a folder.
    /// Directory traversal skips reparse points and inaccessible subdirectories.
    /// </summary>
    public static PathLockQueryResult GetPathLockers(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Error(path, "路径不能为空");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Error(path, "路径格式无效");
        }

        bool isFile = File.Exists(fullPath);
        bool isDirectory = Directory.Exists(fullPath);
        if (!isFile && !isDirectory)
            return Error(fullPath, "路径不存在");

        cancellationToken.ThrowIfCancellationRequested();

        List<string> files;
        int skippedDirectories = 0;
        bool wasTruncated = false;

        if (isFile)
        {
            files = new List<string> { fullPath };
        }
        else
        {
            files = EnumerateDirectoryFiles(
                fullPath,
                MaxDirectoryFileCount,
                cancellationToken,
                out skippedDirectories,
                out wasTruncated);
        }

        // Restart Manager only guarantees support for file resources. An empty
        // directory has no supported resource to register, so return a valid
        // empty scan and let the UI explain the direct-directory-handle limit.
        if (isDirectory && files.Count == 0)
        {
            return CreateResult(
                fullPath, true, 0, skippedDirectories, wasTruncated,
                Array.Empty<FileLockEntry>(), string.Empty);
        }

        string sessionKey = Guid.NewGuid().ToString("N");
        int startResult = RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (startResult != ERROR_SUCCESS)
        {
            return CreateResult(
                fullPath, isDirectory, files.Count, skippedDirectories, wasTruncated,
                Array.Empty<FileLockEntry>(), $"Restart Manager 启动失败（错误 {startResult}）");
        }

        try
        {
            int registerFilesResult = RmRegisterResources(
                sessionHandle, (uint)files.Count, files.ToArray(), 0, null, 0, null);
            if (registerFilesResult != ERROR_SUCCESS)
            {
                return CreateResult(
                    fullPath, isDirectory, files.Count, skippedDirectories, wasTruncated,
                    Array.Empty<FileLockEntry>(), $"注册查询文件失败（错误 {registerFilesResult}）");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var entries = GetRegisteredResourceLockers(sessionHandle, cancellationToken, out string error);
            return CreateResult(
                fullPath, isDirectory, files.Count, skippedDirectories, wasTruncated,
                entries, error);
        }
        finally
        {
            RmEndSession(sessionHandle);
        }
    }

    private static List<string> EnumerateDirectoryFiles(
        string rootPath,
        int maximumCount,
        CancellationToken cancellationToken,
        out int skippedDirectoryCount,
        out bool wasTruncated)
    {
        var files = new List<string>(Math.Min(maximumCount, 256));
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        skippedDirectoryCount = 0;
        wasTruncated = false;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDirectory = pendingDirectories.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (files.Count >= maximumCount)
                    {
                        wasTruncated = true;
                        return files;
                    }
                    files.Add(file);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skippedDirectoryCount++;
                continue;
            }

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                            pendingDirectories.Push(directory);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        skippedDirectoryCount++;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skippedDirectoryCount++;
            }
        }

        return files;
    }

    private static IReadOnlyList<FileLockEntry> GetRegisteredResourceLockers(
        uint sessionHandle,
        CancellationToken cancellationToken,
        out string error)
    {
        error = string.Empty;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint processCount = 0;
            uint processCountNeeded;
            uint rebootReasons = RmRebootReasonNone;

            int firstResult = RmGetList(
                sessionHandle, out processCountNeeded, ref processCount, null, ref rebootReasons);

            if (firstResult == ERROR_SUCCESS)
                return Array.Empty<FileLockEntry>();
            if (firstResult != ERROR_MORE_DATA)
            {
                error = $"读取占用进程失败（错误 {firstResult}）";
                return Array.Empty<FileLockEntry>();
            }

            var processInfo = new RM_PROCESS_INFO[checked((int)processCountNeeded)];
            processCount = processCountNeeded;
            int secondResult = RmGetList(
                sessionHandle, out processCountNeeded, ref processCount, processInfo, ref rebootReasons);

            if (secondResult == ERROR_MORE_DATA)
                continue;
            if (secondResult != ERROR_SUCCESS)
            {
                error = $"读取占用进程失败（错误 {secondResult}）";
                return Array.Empty<FileLockEntry>();
            }

            int returnedProcessCount = checked((int)processCount);
            var entries = new List<FileLockEntry>(returnedProcessCount);
            for (int index = 0; index < returnedProcessCount; index++)
            {
                int pid = processInfo[index].Process.dwProcessId;
                var (name, executablePath, startTime) = GetProcessInfo(pid);
                entries.Add(new FileLockEntry
                {
                    Pid = pid,
                    ProcessName = string.IsNullOrEmpty(name) ? processInfo[index].strAppName : name,
                    ProcessPath = executablePath,
                    AppName = processInfo[index].strAppName,
                    StartTime = startTime,
                });
            }

            return entries
                .GroupBy(entry => entry.Pid)
                .Select(group => group.First())
                .OrderBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Pid)
                .ToList();
        }

        error = "占用进程列表在查询期间持续变化，请重试";
        return Array.Empty<FileLockEntry>();
    }

    private static PathLockQueryResult Error(string path, string errorMessage)
        => CreateResult(path, false, 0, 0, false, Array.Empty<FileLockEntry>(), errorMessage);

    private static PathLockQueryResult CreateResult(
        string path,
        bool isDirectory,
        int scannedFileCount,
        int skippedDirectoryCount,
        bool wasTruncated,
        IReadOnlyList<FileLockEntry> entries,
        string errorMessage)
        => new()
        {
            QueriedPath = path,
            IsDirectory = isDirectory,
            Entries = entries,
            ScannedFileCount = scannedFileCount,
            SkippedDirectoryCount = skippedDirectoryCount,
            WasTruncated = wasTruncated,
            ErrorMessage = errorMessage,
        };

    private static (string name, string path, string startTime) GetProcessInfo(int pid)
    {
        if (pid == 0)
            return ("System Idle", string.Empty, string.Empty);

        try
        {
            using var process = Process.GetProcessById(pid);
            string executablePath = string.Empty;
            try { executablePath = process.MainModule?.FileName ?? string.Empty; }
            catch { }

            string startTime = string.Empty;
            try { startTime = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { }

            return (process.ProcessName, executablePath, startTime);
        }
        catch
        {
            return ("(access denied)", string.Empty, string.Empty);
        }
    }
}
