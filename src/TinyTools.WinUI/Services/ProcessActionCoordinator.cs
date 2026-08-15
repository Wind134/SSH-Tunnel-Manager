using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinyTools.Core.Processes;

namespace TinyTools.WinUI.Services;

internal static class ProcessActionCoordinator
{
    private static readonly SemaphoreSlim DialogGate = new(1, 1);

    public static async Task OpenExecutableDirectoryAsync(XamlRoot xamlRoot, string? executablePath)
    {
        string? directory = ProcessActionService.GetExecutableDirectory(executablePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            await ShowMessageAsync(xamlRoot, "无法打开目录", "没有可用的进程文件路径，或该目录已经不存在。");
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(xamlRoot, "无法打开目录", ex.Message);
        }
    }

    public static async Task<bool> ConfirmAndTerminateAsync(
        XamlRoot xamlRoot,
        int processId,
        string? processName,
        CancellationToken cancellationToken = default)
    {
        var risk = ProcessActionService.AssessRisk(processId, processName);
        if (risk.Level == ProcessRiskLevel.Blocked)
        {
            await ShowMessageAsync(xamlRoot, "无法终止进程", risk.Message);
            return false;
        }

        string displayName = string.IsNullOrWhiteSpace(processName) ? "未知进程" : processName;
        var content = new StackPanel { Spacing = 10, MaxWidth = 480 };
        content.Children.Add(new TextBlock
        {
            Text = $"{displayName}（PID {processId}）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = risk.Message,
            TextWrapping = TextWrapping.Wrap,
        });
        if (risk.Level == ProcessRiskLevel.Critical)
        {
            content.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Error,
                Message = "仅在你明确了解后果时继续。",
            });
        }

        bool confirmed = await ShowConfirmationAsync(
            xamlRoot,
            risk.Level == ProcessRiskLevel.Critical ? "警告：关键系统进程" : "强制终止进程？",
            content,
            "强制终止");
        if (!confirmed)
            return false;

        var result = await ProcessActionService.TerminateAsync(
            processId, processName, cancellationToken);
        if (result.Succeeded)
            return true;

        await ShowMessageAsync(xamlRoot, "终止失败", result.ErrorMessage);
        return false;
    }

    public static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        await DialogGate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            DialogGate.Release();
        }
    }

    private static async Task<bool> ShowConfirmationAsync(
        XamlRoot xamlRoot,
        string title,
        object content,
        string primaryButtonText)
    {
        await DialogGate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            DialogGate.Release();
        }
    }
}
