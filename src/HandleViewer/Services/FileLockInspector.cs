using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HandleViewer.Models;

namespace HandleViewer.Services;

/// <summary>
/// Uses the Windows Restart Manager API (rstrtmgr.dll) to find which processes
/// have a lock on a given file. Restart Manager is the same mechanism Windows
/// Installer uses to ask "which apps need to close before I can update this file?"
/// </summary>
public static class FileLockInspector
{
    private const int RmRebootReasonNone = 0;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;
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

    /// <summary>
    /// Returns the list of processes currently holding a lock on the given file.
    /// </summary>
    public static List<FileLockEntry> GetFileLockers(string filePath)
    {
        var result = new List<FileLockEntry>();

        if (!File.Exists(filePath))
            return result;

        string sessionKey = Guid.NewGuid().ToString();

        int res = RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (res != 0)
            return result;

        try
        {
            string[] files = { filePath };
            res = RmRegisterResources(sessionHandle, (uint)files.Length, files, 0, null, 0, null);
            if (res != 0)
                return result;

            uint pnProcInfo = 0;
            uint pnProcInfoNeeded = 0;
            uint lpdwRebootReasons = RmRebootReasonNone;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

            if (res == ERROR_MORE_DATA)
            {
                var procInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, procInfo, ref lpdwRebootReasons);
                if (res != 0)
                    return result;

                for (int i = 0; i < pnProcInfo; i++)
                {
                    int pid = procInfo[i].Process.dwProcessId;
                    var (name, path, startTime) = GetProcessInfo(pid);

                    result.Add(new FileLockEntry
                    {
                        Pid = pid,
                        ProcessName = string.IsNullOrEmpty(name) ? procInfo[i].strAppName : name,
                        ProcessPath = path,
                        AppName = procInfo[i].strAppName,
                        StartTime = startTime,
                    });
                }
            }
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return result;
    }

    private static (string name, string path, string startTime) GetProcessInfo(int pid)
    {
        if (pid == 0) return ("System Idle", string.Empty, string.Empty);
        try
        {
            using var p = Process.GetProcessById(pid);
            string path = string.Empty;
            try { path = p.MainModule?.FileName ?? string.Empty; }
            catch { }
            string startTime = string.Empty;
            try { startTime = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { }
            return (p.ProcessName, path, startTime);
        }
        catch
        {
            return ("(access denied)", string.Empty, string.Empty);
        }
    }
}