using System.IO;
using System.Windows;
using Microsoft.Win32;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace SSHTunnelManager.Views;

public partial class AddEditDialog : Window
{
    private readonly TunnelConfig? _editing;
    private bool _hostVisible;
    private List<SshConfigEntry> _sshConfigEntries = new();

    public AddEditDialog(TunnelConfig? existing)
    {
        InitializeComponent();
        _editing = existing;
        _hostVisible = false;

        LoadSshConfig();

        if (existing != null)
        {
            Title = "Edit Tunnel";
            NameBox.Text = existing.Name;
            SshUserBox.Text = existing.SshUser;

            try { HostBox.Password = CryptoHelper.Decrypt(existing.EncryptedHost); }
            catch { HostBox.Password = string.Empty; }

            SshPortBox.Text = existing.SshPort.ToString();

            if (existing.AuthMethod == AuthMethod.Password)
            {
                PasswordRadio.IsChecked = true;
                try { PasswordBox.Password = CryptoHelper.Decrypt(existing.EncryptedPassword); }
                catch { }
            }
            else
            {
                KeyRadio.IsChecked = true;
                KeyFileBox.Text = existing.KeyFilePath;
            }

            LocalPortBox.Text = existing.LocalPort.ToString();
            RemotePortBox.Text = existing.RemotePort.ToString();
            AutoReconnectBox.IsChecked = existing.AutoReconnect;

            UpdateAuthPanels();
        }
    }

    private void LoadSshConfig()
    {
        var sshConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh", "config");

        try
        {
            _sshConfigEntries = SshConfigParser.Parse(sshConfigPath);
            SshConfigCombo.ItemsSource = _sshConfigEntries;

            if (_sshConfigEntries.Count == 0)
            {
                SshConfigCombo.ItemsSource = new[] { new SshConfigEntry { Host = "(No SSH config found)" } };
                SshConfigCombo.SelectedIndex = 0;
                SshConfigCombo.IsEnabled = false;
            }
        }
        catch
        {
            SshConfigCombo.ItemsSource = new[] { new SshConfigEntry { Host = "(Permission denied)" } };
            SshConfigCombo.SelectedIndex = 0;
            SshConfigCombo.IsEnabled = false;
        }
    }

    private void SshConfigCombo_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // selection only; import happens on button click
    }

    private void ImportFromConfig_Click(object sender, RoutedEventArgs e)
    {
        if (SshConfigCombo.SelectedItem is not SshConfigEntry entry)
            return;

        if (string.IsNullOrEmpty(entry.Host) || entry.Host.StartsWith("("))
            return;

        NameBox.Text = entry.Host;
        SshUserBox.Text = string.IsNullOrEmpty(entry.User) ? "root" : entry.User;
        HostBox.Password = entry.HostName;
        SshPortBox.Text = entry.Port.ToString();

        if (!string.IsNullOrEmpty(entry.IdentityFile) && File.Exists(entry.IdentityFile))
        {
            KeyRadio.IsChecked = true;
            KeyFileBox.Text = entry.IdentityFile;
            UpdateAuthPanels();
        }
    }

    private void AuthMethod_Click(object sender, RoutedEventArgs e)
        => UpdateAuthPanels();

    private void UpdateAuthPanels()
    {
        bool isPassword = PasswordRadio.IsChecked == true;
        PasswordPanel.Visibility = isPassword ? Visibility.Visible : Visibility.Collapsed;
        KeyPanel.Visibility = isPassword ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleHostBtn_Click(object sender, RoutedEventArgs e)
    {
        _hostVisible = !_hostVisible;
        ToggleHostBtn.Content = _hostVisible ? "Hide" : "Show";
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Private Key Files (*.pem;*.ppk;id_*;*)|*.*",
            Title = "Select Private Key File"
        };

        if (ofd.ShowDialog() == true)
            KeyFileBox.Text = ofd.FileName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SshUserBox.Text))
        {
            MessageBox.Show("SSH user cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(HostBox.Password))
        {
            MessageBox.Show("SSH address cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate the private key early (existence + readability) so the user
        // isn't surprised by a permission/path error only when starting the tunnel.
        if (KeyRadio.IsChecked == true)
        {
            var keyPath = KeyFileBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                MessageBox.Show("Please select a private key file, or switch to password auth.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(keyPath))
            {
                MessageBox.Show($"Private key file not found:\n{keyPath}",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                using var probe = File.OpenRead(keyPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"No permission to read the key file:\n{keyPath}\n\n{ex.Message}\n\n" +
                    "Grant your account Read permission (Properties → Security), or run:\n" +
                    $"icacls \"{keyPath}\" /grant:r \"%USERNAME%:R\"",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
    }

    public TunnelConfig GetConfig()
    {
        var config = _editing ?? new TunnelConfig();

        config.Name = NameBox.Text.Trim();
        config.SshUser = SshUserBox.Text.Trim();

        var host = HostBox.Password.Trim();
        config.EncryptedHost = CryptoHelper.Encrypt(host);

        config.SshPort = int.TryParse(SshPortBox.Text, out var sshPort) ? sshPort : 22;

        config.AuthMethod = PasswordRadio.IsChecked == true
            ? AuthMethod.Password
            : AuthMethod.PrivateKey;

        if (config.AuthMethod == AuthMethod.Password)
        {
            config.EncryptedPassword = CryptoHelper.Encrypt(PasswordBox.Password);
            config.KeyFilePath = string.Empty;
        }
        else
        {
            config.KeyFilePath = KeyFileBox.Text.Trim();
            config.EncryptedPassword = string.Empty;
        }

        config.LocalPort = int.TryParse(LocalPortBox.Text, out var localPort) ? localPort : 7890;
        config.RemotePort = int.TryParse(RemotePortBox.Text, out var remotePort) ? remotePort : 1080;
        config.AutoReconnect = AutoReconnectBox.IsChecked == true;
        config.ModifiedAt = DateTime.Now;

        if (_editing == null)
            config.CreatedAt = DateTime.Now;

        if (_editing != null)
        {
            // If the user changed the SSH host, the previously trusted
            // fingerprint no longer applies — the next connect must
            // re-prompt for the host key. Otherwise we'd either silently
            // trust a different server, or auto-reconnect into a rejection
            // loop because Trust != Unknown never re-prompts the user.
            bool hostChanged;
            try
            {
                var oldHost = CryptoHelper.Decrypt(_editing.EncryptedHost);
                hostChanged = !string.Equals(
                    oldHost, host, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Couldn't read the old host back (corrupt config, etc.):
                // be safe and force a fresh verification.
                hostChanged = true;
            }

            if (hostChanged)
            {
                config.HostKeyFingerprint = string.Empty;
                config.HostKeyTrust = HostKeyTrust.Unknown;
            }
            else
            {
                config.HostKeyFingerprint = _editing.HostKeyFingerprint;
                config.HostKeyTrust = _editing.HostKeyTrust;
            }
        }

        return config;
    }
}
