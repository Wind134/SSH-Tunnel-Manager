using System.Windows;
using SSHTunnelManager.Services;

namespace SSHTunnelManager.Views;

public partial class HostKeyDialog : Window
{
    public HostKeyDialog(TunnelState state, string fingerprint, string algoName)
    {
        InitializeComponent();

        var host = string.Empty;
        try { host = CryptoHelper.Decrypt(state.Config.EncryptedHost); }
        catch { }

        ServerText.Text = $"{CryptoHelper.MaskIp(host)}:{state.Config.SshPort}";
        AlgoText.Text = algoName;
        FingerprintText.Text = fingerprint;
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void Trust_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
