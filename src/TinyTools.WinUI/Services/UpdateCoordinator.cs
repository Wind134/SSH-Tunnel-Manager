using Microsoft.UI.Dispatching;
using TinyTools.Core.Updates;

namespace TinyTools.WinUI.Services;

public enum UpdatePhase
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    Failed,
}

public sealed record UpdateStateSnapshot(
    UpdatePhase Phase,
    Version CurrentVersion,
    UpdateCheckResult? CheckResult,
    UpdateDownloadResult? DownloadResult,
    double ProgressPercentage,
    long BytesReceived,
    long TotalBytes,
    string StatusMessage);

/// <summary>
/// 应用级更新协调器：负责启动静默检查、手动检查、后台下载安装包及触发升级，
/// 生命周期与应用绑定，切换页面不会中断下载。
/// </summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly GitHubUpdateService _updateService;
    private readonly object _gate = new();
    private CancellationTokenSource? _operationCts;
    private bool _disposed;

    public UpdateStateSnapshot CurrentState { get; private set; }

    public event Action<UpdateStateSnapshot>? StateChanged;

    public UpdateCoordinator(DispatcherQueue dispatcherQueue, GitHubUpdateService? updateService = null)
    {
        _dispatcherQueue = dispatcherQueue;
        _updateService = updateService ?? new GitHubUpdateService();
        CurrentState = new UpdateStateSnapshot(
            UpdatePhase.Idle,
            GetCurrentVersion(),
            null,
            null,
            0,
            0,
            0,
            string.Empty);
    }

    public static Version GetCurrentVersion()
    {
        Version version = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>
    /// 后台清理已完成安装的旧安装包（版本号小于等于当前版本）。
    /// </summary>
    public async Task CleanupObsoleteInstallersAsync()
    {
        if (_disposed)
            return;

        try
        {
            // 等待刚结束的安装程序释放自身文件句柄
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (_disposed)
                return;

            string updatesRoot = UpdatePackageCleaner.GetUpdatesRootDirectory();
            Version current = GetCurrentVersion();
            await Task.Run(() => UpdatePackageCleaner.CleanupInstalledPackages(updatesRoot, current));
        }
        catch
        {
            // 清理缓存失败不影响主流程
        }
    }

    /// <summary>
    /// 启动后静默检查更新，仅在发现新版本时通知界面，网络异常不打扰用户。
    /// </summary>
    public async Task CheckSilentlyOnStartupAsync()
    {
        if (_disposed)
            return;

        try
        {
            // 延迟数秒避开启动高峰
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (_disposed || CurrentState.Phase != UpdatePhase.Idle)
                return;

            await CheckCoreAsync(silentOnFailure: true);
        }
        catch
        {
            // 静默检查忽略异常
        }
    }

    /// <summary>
    /// 手动检查最新版本。
    /// </summary>
    public Task CheckForUpdatesAsync() => CheckCoreAsync(silentOnFailure: false);

    private async Task CheckCoreAsync(bool silentOnFailure)
    {
        if (_disposed)
            return;

        var cts = ResetCancellation();
        Version current = GetCurrentVersion();

        if (!silentOnFailure)
        {
            PublishState(new UpdateStateSnapshot(
                UpdatePhase.Checking, current, null, null, 0, 0, 0, "正在检查新版本…"));
        }

        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(current, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            if (!result.IsUpdateAvailable)
            {
                if (silentOnFailure)
                {
                    PublishState(new UpdateStateSnapshot(
                        UpdatePhase.Idle, current, result, null, 0, 0, 0, string.Empty));
                    return;
                }

                string msg = result.CurrentVersion > result.LatestVersion
                    ? $"当前版本 {result.CurrentVersion} 高于公开版本 {result.LatestVersion}。"
                    : $"当前已是最新版本（v{result.LatestVersion}）。";
                PublishState(new UpdateStateSnapshot(
                    UpdatePhase.UpToDate, current, result, null, 0, 0, 0, msg));
                return;
            }

            string availableMsg = result.Package is null
                ? $"发现新版本 v{result.LatestVersion}，请前往发布页手动下载。"
                : $"发现新版本 v{result.LatestVersion}（{FormatSize(result.Package.Size)}），可下载安装。";
            PublishState(new UpdateStateSnapshot(
                UpdatePhase.UpdateAvailable, current, result, null, 0, 0, 0, availableMsg));
        }
        catch (OperationCanceledException)
        {
            // 已取消
        }
        catch (Exception ex)
        {
            if (!silentOnFailure)
            {
                PublishState(new UpdateStateSnapshot(
                    UpdatePhase.Failed, current, null, null, 0, 0, 0, $"检查更新失败：{ex.Message}"));
            }
        }
    }

    /// <summary>
    /// 下载当前检测到版本的安装包并校验 SHA-256。
    /// </summary>
    public async Task DownloadUpdateAsync()
    {
        UpdateCheckResult? checkResult = CurrentState.CheckResult;
        if (_disposed || checkResult?.Package is not ReleasePackage package)
            return;

        var cts = ResetCancellation();
        Version current = GetCurrentVersion();
        PublishState(new UpdateStateSnapshot(
            UpdatePhase.Downloading, current, checkResult, null, 0, 0, package.Size, "正在准备下载…"));

        try
        {
            string destinationDir = Path.Combine(
                UpdatePackageCleaner.GetUpdatesRootDirectory(),
                checkResult.LatestVersion.ToString());

            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (cts.IsCancellationRequested)
                    return;

                string text = value.TotalBytes > 0
                    ? $"正在下载安装包… {value.Percentage:0}%（{FormatSize(value.BytesReceived)} / {FormatSize(value.TotalBytes)}）"
                    : $"正在下载安装包… {FormatSize(value.BytesReceived)}";
                PublishState(new UpdateStateSnapshot(
                    UpdatePhase.Downloading,
                    current,
                    checkResult,
                    null,
                    value.Percentage,
                    value.BytesReceived,
                    value.TotalBytes,
                    text));
            });

            UpdateDownloadResult result = await _updateService.DownloadAsync(
                package, destinationDir, progress, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            PublishState(new UpdateStateSnapshot(
                UpdatePhase.ReadyToInstall,
                current,
                checkResult,
                result,
                100,
                package.Size,
                package.Size,
                "安装包下载完成且 SHA-256 校验通过，随时可以安装。"));
        }
        catch (OperationCanceledException)
        {
            PublishState(new UpdateStateSnapshot(
                UpdatePhase.UpdateAvailable,
                current,
                checkResult,
                null,
                0,
                0,
                0,
                "已取消下载。"));
        }
        catch (Exception ex)
        {
            PublishState(new UpdateStateSnapshot(
                UpdatePhase.Failed,
                current,
                checkResult,
                null,
                0,
                0,
                0,
                $"下载更新失败：{ex.Message}"));
        }
    }

    /// <summary>
    /// 取消正在进行的检查或下载。
    /// </summary>
    public void CancelCurrentOperation()
    {
        lock (_gate)
        {
            _operationCts?.Cancel();
        }
    }

    /// <summary>
    /// 启动已下载的 exe 安装程序进入升级模式。
    /// </summary>
    public bool TryLaunchDownloadedInstaller(out string errorMessage)
    {
        errorMessage = string.Empty;
        string? filePath = CurrentState.DownloadResult?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "未找到已下载的安装包，请重新下载。";
            return false;
        }

        try
        {
            string? targetDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            UpdateInstallerLauncher.LaunchUpgrade(filePath, Environment.ProcessId, targetDirectory);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"无法启动安装程序：{ex.Message}";
            return false;
        }
    }

    public static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(0, bytes) / 1024d:0.0} KB";

    private CancellationTokenSource ResetCancellation()
    {
        lock (_gate)
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            return _operationCts;
        }
    }

    private void PublishState(UpdateStateSnapshot snapshot)
    {
        CurrentState = snapshot;
        if (_dispatcherQueue.HasThreadAccess)
            StateChanged?.Invoke(snapshot);
        else
            _dispatcherQueue.TryEnqueue(() => StateChanged?.Invoke(snapshot));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_gate)
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = null;
        }
        _updateService.Dispose();
    }
}
