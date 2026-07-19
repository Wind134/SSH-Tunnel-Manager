using System.Security.Cryptography;
using System.Text;

namespace SSHTunnelManager.Services;

public static class CryptoHelper
{
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("SSHTunnelManager_v2");

    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, s_entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        var data = Convert.FromBase64String(ciphertext);
        var decrypted = ProtectedData.Unprotect(data, s_entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    public static string MaskIp(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return string.Empty;

        var parts = ip.Split('.');
        if (parts.Length == 4)
            return $"{parts[0]}.{parts[1]}.***.***";
        return "***";
    }
}
