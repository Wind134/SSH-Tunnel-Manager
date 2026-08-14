using System.Windows;
using System.Windows.Data;
using HandleViewer.Services;
using SSHTunnelManager.Converters;
using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.Tests;

public class CoreBehaviorTests
{
    [Fact]
    public void AppSettingsHaveSafeDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal("System", settings.Theme);
        Assert.Equal("LastUsed", settings.StartPage);
        Assert.Equal("Tunnel", settings.LastPage);
        Assert.True(settings.MinimizeToTrayOnClose);
        Assert.True(settings.ConfirmBeforeExit);
        Assert.True(settings.ShowTrayNotifications);
        Assert.Equal(0, settings.PortAutoRefreshSeconds);
        Assert.False(settings.ShowSystemProcesses);
    }

    [Fact]
    public void TunnelConfigCloneCopiesAllValues()
    {
        var createdAt = new DateTime(2026, 7, 20, 10, 30, 0);
        var modifiedAt = createdAt.AddHours(1);
        var source = new TunnelConfig
        {
            Id = Guid.NewGuid(),
            Name = "production",
            LocalPort = 7891,
            RemotePort = 1081,
            SshUser = "deploy",
            EncryptedHost = "encrypted-host",
            SshPort = 2222,
            AuthMethod = AuthMethod.PrivateKey,
            EncryptedPassword = "encrypted-password",
            KeyFilePath = @"C:\keys\id_ed25519",
            HostKeyFingerprint = "SHA256:test",
            HostKeyTrust = HostKeyTrust.Trusted,
            AutoReconnect = false,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt
        };

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.Equal(source.Id, clone.Id);
        Assert.Equal(source.Name, clone.Name);
        Assert.Equal(source.LocalPort, clone.LocalPort);
        Assert.Equal(source.RemotePort, clone.RemotePort);
        Assert.Equal(source.SshUser, clone.SshUser);
        Assert.Equal(source.EncryptedHost, clone.EncryptedHost);
        Assert.Equal(source.SshPort, clone.SshPort);
        Assert.Equal(source.AuthMethod, clone.AuthMethod);
        Assert.Equal(source.EncryptedPassword, clone.EncryptedPassword);
        Assert.Equal(source.KeyFilePath, clone.KeyFilePath);
        Assert.Equal(source.HostKeyFingerprint, clone.HostKeyFingerprint);
        Assert.Equal(source.HostKeyTrust, clone.HostKeyTrust);
        Assert.Equal(source.AutoReconnect, clone.AutoReconnect);
        Assert.Equal(source.CreatedAt, clone.CreatedAt);
        Assert.Equal(source.ModifiedAt, clone.ModifiedAt);
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.***.***")]
    [InlineData("server.example.com", "***")]
    [InlineData("", "")]
    public void MaskIpHidesSensitiveAddressParts(string input, string expected)
        => Assert.Equal(expected, CryptoHelper.MaskIp(input));

    [Fact]
    public void DpapiRoundTripPreservesPlaintext()
    {
        const string plaintext = "sensitive-配置-123";

        var encrypted = CryptoHelper.Encrypt(plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, CryptoHelper.Decrypt(encrypted));
    }

    [Fact]
    public void OneWayConvertersRejectConvertBackWithoutThrowing()
    {
        var converters = new IValueConverter[]
        {
            new StatusToColorConverter(),
            new StatusToDotConverter(),
            new BoolToVisibilityConverter(),
            new InverseBoolToVisibilityConverter()
        };

        foreach (var converter in converters)
        {
            Assert.Same(
                Binding.DoNothing,
                converter.ConvertBack(null, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void VisibilityConvertersMapBooleanValues()
    {
        var normal = new BoolToVisibilityConverter();
        var inverse = new InverseBoolToVisibilityConverter();

        Assert.Equal(Visibility.Visible, normal.Convert(true, typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Collapsed, normal.Convert(false, typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Collapsed, inverse.Convert(true, typeof(Visibility), null, null!));
        Assert.Equal(Visibility.Visible, inverse.Convert(false, typeof(Visibility), null, null!));
    }

    [Fact]
    public void MissingFileHasNoLockOwners()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.missing");

        Assert.Empty(FileLockInspector.GetFileLockers(missingPath));
        Assert.Equal("路径不存在", FileLockInspector.GetPathLockers(missingPath).ErrorMessage);
    }

    [Fact]
    public void EmptyDirectoryCanBeQueried()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var result = FileLockInspector.GetPathLockers(directory);

            Assert.True(result.IsDirectory);
            Assert.Equal(0, result.ScannedFileCount);
            Assert.Empty(result.Entries);
            Assert.Empty(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DirectoryQueryScansNestedFiles()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string childDirectory = Directory.CreateDirectory(Path.Combine(directory, "child")).FullName;
            File.WriteAllText(Path.Combine(directory, "root.txt"), "root");
            File.WriteAllText(Path.Combine(childDirectory, "child.txt"), "child");

            var result = FileLockInspector.GetPathLockers(directory);

            Assert.True(result.IsDirectory);
            Assert.Equal(2, result.ScannedFileCount);
            Assert.False(result.WasTruncated);
            Assert.Empty(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DirectoryQueryFindsProcessHoldingNestedFile()
    {
        string directory = CreateTemporaryDirectory();
        string filePath = Path.Combine(directory, "held.txt");
        File.WriteAllText(filePath, "held");

        try
        {
            using var heldFile = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = FileLockInspector.GetPathLockers(directory);

            Assert.Empty(result.ErrorMessage);
            Assert.Contains(result.Entries, entry => entry.Pid == Environment.ProcessId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TinyTools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
