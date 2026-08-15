using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Services;
using TinyTools.WinUI.Dialogs;
using Windows.ApplicationModel.DataTransfer;

namespace TinyTools.WinUI.Pages;

public sealed partial class TunnelsPage : Page
{
    private TunnelManager Manager => App.Services.TunnelManager;

    public TunnelsPage()
    {
        InitializeComponent();
        TunnelList.ItemsSource = Manager.TunnelStates;
        LogList.ItemsSource = App.Services.LogEntries;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateSummary();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Manager.TunnelStates.CollectionChanged += OnCollectionChanged;
        foreach (var state in Manager.TunnelStates)
            state.PropertyChanged += OnTunnelPropertyChanged;
        UpdateSummary();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Manager.TunnelStates.CollectionChanged -= OnCollectionChanged;
        foreach (var state in Manager.TunnelStates)
            state.PropertyChanged -= OnTunnelPropertyChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TunnelState state in e.OldItems)
                state.PropertyChanged -= OnTunnelPropertyChanged;
        if (e.NewItems is not null)
            foreach (TunnelState state in e.NewItems)
                state.PropertyChanged += OnTunnelPropertyChanged;
        UpdateSummary();
    }

    private void OnTunnelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
            UpdateSummary();
        else
            DispatcherQueue.TryEnqueue(UpdateSummary);
    }

    private void UpdateSummary()
    {
        int total = Manager.TunnelStates.Count;
        int running = Manager.TunnelStates.Count(state =>
            state.Status == SSHTunnelManager.Models.TunnelStatus.Connected);
        SummaryText.Text = $"{total} 条隧道 · {running} 条运行中";
        EmptyHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        TunnelList.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TunnelEditorDialog(null) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.ResultConfig is null)
            return;

        Manager.AddTunnel(dialog.ResultConfig);
        App.Services.SaveTunnels();
        ShowMessage("已新增隧道。", InfoBarSeverity.Success);
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TunnelState state)
            return;

        var dialog = new TunnelEditorDialog(state.Config) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.ResultConfig is null)
            return;

        Manager.UpdateTunnel(dialog.ResultConfig);
        App.Services.SaveTunnels();
        ShowMessage("隧道配置已保存。", InfoBarSeverity.Success);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not TunnelState state)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"删除“{state.Config.Name}”？",
            Content = "如果隧道正在运行，它会先被停止。此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await Manager.RemoveTunnelAsync(state.Config.Id);
        App.Services.SaveTunnels();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is TunnelState state)
            await Manager.StartTunnel(state);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is TunnelState state)
            await Manager.StopTunnel(state);
    }

    private async void StartAll_Click(object sender, RoutedEventArgs e)
        => await Manager.StartAllAsync();

    private async void StopAll_Click(object sender, RoutedEventArgs e)
        => await Manager.StopAllAsync();

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.LogEntries.Count == 0)
            return;

        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, App.Services.LogEntries.Reverse()));
        Clipboard.SetContent(package);
        ShowMessage("日志已复制。", InfoBarSeverity.Success);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
        => App.Services.LogEntries.Clear();

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        PageMessage.Message = message;
        PageMessage.Severity = severity;
        PageMessage.IsOpen = true;
    }
}
