using System.Diagnostics;

namespace TinyTools.Core.Processes;

public enum ProcessRiskLevel
{
    Standard,
    Critical,
    Blocked,
}

public sealed record ProcessRiskAssessment(ProcessRiskLevel Level, string Message);

public sealed record ProcessTerminationResult(bool Succeeded, string ErrorMessage = "");

/// <summary>
/// UI-neutral process safety checks and actions shared by the desktop front ends.
/// Confirmation and user-facing dialogs remain the responsibility of the UI.
/// </summary>
public static class ProcessActionService
{
    private static readonly HashSet<int> BlockedProcessIds = [0, 4];

    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "wininit", "services", "lsass", "smss", "winlogon",
        "explorer", "dwm", "svchost", "System",
    };

    public static ProcessRiskAssessment AssessRisk(int processId, string? processName)
    {
        if (BlockedProcessIds.Contains(processId))
        {
            return new ProcessRiskAssessment(
                ProcessRiskLevel.Blocked,
                "TinyTools 不允许终止系统内核进程（PID 0/4）。");
        }

        if (!string.IsNullOrWhiteSpace(processName)
            && CriticalProcessNames.Contains(processName))
        {
            return new ProcessRiskAssessment(
                ProcessRiskLevel.Critical,
                "这是关键 Windows 进程。强制终止可能导致桌面重启、数据丢失或系统不稳定。");
        }

        return new ProcessRiskAssessment(
            ProcessRiskLevel.Standard,
            "强制终止会立即结束该进程及其子进程，未保存的数据可能丢失。");
    }

    public static string? GetExecutableDirectory(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(executablePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static async Task<ProcessTerminationResult> TerminateAsync(
        int processId,
        string? processName,
        CancellationToken cancellationToken = default)
    {
        var risk = AssessRisk(processId, processName);
        if (risk.Level == ProcessRiskLevel.Blocked)
            return new ProcessTerminationResult(false, risk.Message);

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(
                TimeSpan.FromSeconds(3), cancellationToken);
            return new ProcessTerminationResult(true);
        }
        catch (ArgumentException)
        {
            // The process exited between inspection and the requested action.
            return new ProcessTerminationResult(true);
        }
        catch (TimeoutException)
        {
            return new ProcessTerminationResult(false, "已发送终止请求，但进程未在 3 秒内退出。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessTerminationResult(
                false,
                $"终止进程失败：{ex.Message}。该操作可能需要管理员权限。");
        }
    }
}
