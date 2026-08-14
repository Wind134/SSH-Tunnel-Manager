using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;
using Windows.Storage.Pickers;

namespace TinyTools.WinUI.Dialogs;

public sealed partial class TunnelEditorDialog : ContentDialog
{
    private readonly TunnelConfig? _editing;
    private readonly List<SshConfigEntry> _sshConfigEntries = new();

    public TunnelConfig? ResultConfig { get; private set; }

    public TunnelEditorDialog(TunnelConfig? existing)
    {
        InitializeComponent();
        _editing = existing;
        Title = existing is null ? "新增 SSH 隧道" : "编辑 SSH 隧道";

        LoadSshConfig();
        Populate(existing);
    }

    private void LoadSshConfig()
    {
        string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "config");
        try
        {
            _sshConfigEntries.AddRange(SshConfigParser.Parse(configPath));
            SshConfigBox.ItemsSource = _sshConfigEntries;
            SshConfigBox.IsEnabled = _sshConfigEntries.Count > 0;
        }
        catch
        {
            SshConfigBox.IsEnabled = false;
        }
    }

    private void Populate(TunnelConfig? config)
    {
        AuthBox.SelectedIndex = config?.AuthMethod == AuthMethod.PrivateKey ? 1 : 0;
        if (config is null)
            return;

        NameBox.Text = config.Name;
        UserBox.Text = config.SshUser;
        SshPortBox.Value = config.SshPort;
        LocalPortBox.Value = config.LocalPort;
        RemotePortBox.Value = config.RemotePort;
        KeyFileBox.Text = config.KeyFilePath;
        AutoReconnectBox.IsChecked = config.AutoReconnect;

        try { HostBox.Password = CryptoHelper.Decrypt(config.EncryptedHost); } catch { }
        try { PasswordBox.Password = CryptoHelper.Decrypt(config.EncryptedPassword); } catch { }

        TrustHint.Text = config.HostKeyTrust switch
        {
            HostKeyTrust.Trusted => $"已信任主机密钥：{config.HostKeyFingerprint}",
            HostKeyTrust.Rejected => "主机密钥曾被拒绝。保存后，下次连接将重新请求确认。",
            _ => "首次连接时将要求确认主机密钥。",
        };
    }

    private void AuthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool privateKey = (AuthBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "PrivateKey";
        if (PasswordBox is not null)
            PasswordBox.Visibility = privateKey ? Visibility.Collapsed : Visibility.Visible;
        if (KeyPanel is not null)
            KeyPanel.Visibility = privateKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ImportSshConfig_Click(object sender, RoutedEventArgs e)
    {
        if (SshConfigBox.SelectedItem is not SshConfigEntry entry)
            return;

        NameBox.Text = entry.Host;
        UserBox.Text = string.IsNullOrWhiteSpace(entry.User) ? "root" : entry.User;
        HostBox.Password = entry.HostName;
        SshPortBox.Value = entry.Port;
        if (!string.IsNullOrWhiteSpace(entry.IdentityFile) && File.Exists(entry.IdentityFile))
        {
            AuthBox.SelectedIndex = 1;
            KeyFileBox.Text = entry.IdentityFile;
        }
    }

    private async void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            KeyFileBox.Text = file.Path;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? error = ValidateInput();
        if (error is not null)
        {
            ErrorBar.Message = error;
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        ResultConfig = BuildConfig();
    }

    private string? ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return "名称不能为空。";
        if (string.IsNullOrWhiteSpace(UserBox.Text)) return "SSH 用户名不能为空。";
        if (string.IsNullOrWhiteSpace(HostBox.Password)) return "SSH 地址不能为空。";
        if (!ValidPort(SshPortBox.Value)) return "SSH 端口必须在 1–65535 之间。";
        if (!ValidPort(LocalPortBox.Value)) return "本地端口必须在 1–65535 之间。";
        if (!ValidPort(RemotePortBox.Value)) return "远程端口必须在 1–65535 之间。";

        if (SelectedAuthMethod() == AuthMethod.PrivateKey)
        {
            string path = KeyFileBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path)) return "请选择私钥文件。";
            if (!File.Exists(path)) return $"找不到私钥文件：{path}";
            try { using var stream = File.OpenRead(path); }
            catch (Exception ex) { return $"无法读取私钥文件：{ex.Message}"; }
        }

        return null;
    }

    private TunnelConfig BuildConfig()
    {
        var config = _editing?.Clone() ?? new TunnelConfig { CreatedAt = DateTime.Now };
        string host = HostBox.Password.Trim();
        string oldHost = string.Empty;
        if (_editing is not null)
        {
            try { oldHost = CryptoHelper.Decrypt(_editing.EncryptedHost); } catch { }
        }

        config.Name = NameBox.Text.Trim();
        config.SshUser = UserBox.Text.Trim();
        config.EncryptedHost = CryptoHelper.Encrypt(host);
        config.SshPort = (int)SshPortBox.Value;
        config.LocalPort = (int)LocalPortBox.Value;
        config.RemotePort = (int)RemotePortBox.Value;
        config.AuthMethod = SelectedAuthMethod();
        config.AutoReconnect = AutoReconnectBox.IsChecked == true;
        config.ModifiedAt = DateTime.Now;

        if (config.AuthMethod == AuthMethod.Password)
        {
            config.EncryptedPassword = CryptoHelper.Encrypt(PasswordBox.Password);
            config.KeyFilePath = string.Empty;
        }
        else
        {
            config.EncryptedPassword = string.Empty;
            config.KeyFilePath = KeyFileBox.Text.Trim();
        }

        bool hostChanged = _editing is not null &&
            !string.Equals(oldHost, host, StringComparison.OrdinalIgnoreCase);
        if (hostChanged || _editing?.HostKeyTrust == HostKeyTrust.Rejected)
        {
            config.HostKeyFingerprint = string.Empty;
            config.HostKeyTrust = HostKeyTrust.Unknown;
        }

        return config;
    }

    private AuthMethod SelectedAuthMethod()
        => (AuthBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "PrivateKey"
            ? AuthMethod.PrivateKey
            : AuthMethod.Password;

    private static bool ValidPort(double value)
        => !double.IsNaN(value) && value is >= 1 and <= 65535 && value == Math.Truncate(value);
}
