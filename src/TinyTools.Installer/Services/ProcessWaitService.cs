using System.Diagnostics;

namespace TinyTools.Installer.Services;

/// <summary>
/// 在升级或卸载前等待目标进程退出，避免文件被占用。
/// </summary>
public static class ProcessWaitService
{
    public static async Task WaitForExitAsync(
        int processId,
        string processNameWithoutExtension,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        // 优先等待显式传入的旧进程 PID
        if (processId > 0)
        {
            try
            {
                using Process proc = Process.GetProcessById(processId);
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && !proc.HasExited)
                    await proc.WaitForExitAsync(cancellationToken).WaitAsync(remaining, cancellationToken);
            }
            catch (ArgumentException)
            {
                // 进程已退出
            }
            catch (TimeoutException)
            {
                // 超时后交由后续按名称检查处理
            }
        }

        // 检查同名的残留主程序进程
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process[] running = Process.GetProcessesByName(processNameWithoutExtension);
            bool anyAlive = false;
            foreach (Process proc in running)
            {
                using (proc)
                {
                    if (proc.Id != Environment.ProcessId && !proc.HasExited)
                        anyAlive = true;
                }
            }

            if (!anyAlive)
                return;

            await Task.Delay(300, cancellationToken);
        }
    }
}
