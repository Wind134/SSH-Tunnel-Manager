using System.Diagnostics;

namespace TinyTools.Installer.Services;

/// <summary>
/// 在安装、升级或卸载前等待并关闭正在运行的目标应用，彻底释放可执行文件锁。
/// </summary>
public static class ProcessWaitService
{
    public static async Task EnsureStoppedAsync(
        int processId,
        string[] processNamesWithoutExtension,
        CancellationToken cancellationToken)
    {
        // 1. 优先短暂等待显式传入的旧进程自行退出（v1.2+ 升级模式会主动退出）
        if (processId > 0)
        {
            await WaitSingleProcessAsync(processId, TimeSpan.FromSeconds(4), cancellationToken);
        }

        // 2. 收集仍在运行的同名应用进程（兼容旧版 v1.1.0 不会自动退出或隐藏在托盘的场景）
        List<Process> running = FindRunningProcesses(processNamesWithoutExtension);
        if (running.Count == 0)
            return;

        try
        {
            // 先尝试优雅关闭
            foreach (Process proc in running)
            {
                try
                {
                    if (!proc.HasExited)
                        proc.CloseMainWindow();
                }
                catch { }
            }

            if (await WaitAllExitedAsync(running, TimeSpan.FromSeconds(2), cancellationToken))
                return;

            // 旧版默认“关闭窗口到托盘”，仍存活则强制结束进程树释放文件锁
            foreach (Process proc in running)
            {
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch { }
            }

            await WaitAllExitedAsync(running, TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            foreach (Process proc in running)
                proc.Dispose();
        }
    }

    private static async Task WaitSingleProcessAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process proc = Process.GetProcessById(processId);
            if (!proc.HasExited)
                await proc.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (ArgumentException) { }
        catch (TimeoutException) { }
    }

    private static List<Process> FindRunningProcesses(string[] processNames)
    {
        var result = new List<Process>();
        foreach (string name in processNames)
        {
            foreach (Process proc in Process.GetProcessesByName(name))
            {
                if (proc.Id == Environment.ProcessId)
                {
                    proc.Dispose();
                    continue;
                }

                try
                {
                    if (!proc.HasExited)
                    {
                        result.Add(proc);
                        continue;
                    }
                }
                catch { }

                proc.Dispose();
            }
        }

        return result;
    }

    private static async Task<bool> WaitAllExitedAsync(
        List<Process> processes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool anyAlive = false;
            foreach (Process proc in processes)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        anyAlive = true;
                        break;
                    }
                }
                catch { }
            }

            if (!anyAlive)
                return true;

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }
}
