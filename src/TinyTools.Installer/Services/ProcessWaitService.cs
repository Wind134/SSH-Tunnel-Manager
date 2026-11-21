using System.ComponentModel;
using System.Diagnostics;

namespace TinyTools.Installer.Services;

/// <summary>
/// 在安装、升级或卸载前等待并关闭正在运行的目标应用（兼容普通权限与管理员权限进程）。
/// </summary>
public static class ProcessWaitService
{
    public static async Task EnsureStoppedAsync(
        int processId,
        string[] processNamesWithoutExtension,
        CancellationToken cancellationToken)
    {
        // 1. 若有显式传入 PID，先短暂等待其主动退出
        if (processId > 0)
        {
            await WaitProcessIdGoneAsync(processId, TimeSpan.FromSeconds(3), cancellationToken);
        }

        // 2. 按进程名收集所有仍在运行的实例（不因权限不足访问 HasExited 而漏掉高权限进程）
        HashSet<int> targetPids = FindTargetProcessIds(processId, processNamesWithoutExtension);
        if (targetPids.Count == 0)
            return;

        // 3. 尝试优雅关闭窗口
        foreach (int pid in targetPids)
        {
            try
            {
                using Process proc = Process.GetProcessById(pid);
                proc.CloseMainWindow();
            }
            catch { }
        }

        if (await WaitAllPidsGoneAsync(targetPids, TimeSpan.FromSeconds(2), cancellationToken))
            return;

        // 4. 尝试普通权限强制终止进程树
        var elevatedPids = new List<int>();
        foreach (int pid in targetPids)
        {
            if (!IsProcessAlive(pid))
                continue;

            try
            {
                using Process proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
                // 已退出
            }
            catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException)
            {
                // 目标进程可能以管理员权限运行，记入提权终止列表
                if (IsProcessAlive(pid))
                    elevatedPids.Add(pid);
            }
        }

        if (await WaitAllPidsGoneAsync(targetPids, TimeSpan.FromSeconds(3), cancellationToken))
            return;

        // 5. 对高权限残留进程，发起一次 UAC 提权 taskkill 彻底关闭
        List<int> stillAlive = targetPids.Where(IsProcessAlive).Union(elevatedPids.Where(IsProcessAlive)).ToList();
        if (stillAlive.Count > 0)
        {
            await KillElevatedAsync(stillAlive, cancellationToken);
            await WaitAllPidsGoneAsync(stillAlive, TimeSpan.FromSeconds(5), cancellationToken);
        }

        // 6. 最终确认，若仍存活则直接给出明确提示，不盲目进入解压
        List<int> remaining = targetPids.Where(IsProcessAlive).ToList();
        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"无法自动关闭正在运行的 TinyTools（PID: {string.Join(", ", remaining)}）。请先右键系统托盘图标选择“退出”，然后点击重试。");
        }
    }

    private static HashSet<int> FindTargetProcessIds(int explicitPid, string[] processNames)
    {
        var pids = new HashSet<int>();
        if (explicitPid > 0 && explicitPid != Environment.ProcessId && IsProcessAlive(explicitPid))
            pids.Add(explicitPid);

        foreach (string name in processNames)
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                using (proc)
                {
                    if (proc.Id != Environment.ProcessId)
                        pids.Add(proc.Id);
                }
            }
        }

        return pids;
    }

    // 通过 GetProcessById 判定存活，跨权限也能准确识别
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            try
            {
                return !proc.HasExited;
            }
            catch (Win32Exception)
            {
                // 无权查询退出状态说明高权限进程仍存活
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitProcessIdGoneAsync(
        int pid,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && IsProcessAlive(pid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken);
        }
    }

    private static async Task<bool> WaitAllPidsGoneAsync(
        IEnumerable<int> pids,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!pids.Any(IsProcessAlive))
                return true;
            await Task.Delay(200, cancellationToken);
        }

        return !pids.Any(IsProcessAlive);
    }

    private static async Task KillElevatedAsync(List<int> pids, CancellationToken cancellationToken)
    {
        string pidArgs = string.Join(" ", pids.Select(pid => $"/PID {pid}"));
        var startInfo = new ProcessStartInfo("taskkill.exe", $"/F /T {pidArgs}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using Process? proc = Process.Start(startInfo);
            if (proc is not null)
                await proc.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch (Win32Exception)
        {
            // 用户取消 UAC 授权，交由后续存活校验给出清晰引导
        }
        catch (TimeoutException) { }
    }
}
