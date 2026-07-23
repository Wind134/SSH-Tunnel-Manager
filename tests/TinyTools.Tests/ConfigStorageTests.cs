using SSHTunnelManager.Models;
using SSHTunnelManager.Services;

namespace TinyTools.Tests;

[CollectionDefinition("Config storage", DisableParallelization = true)]
public sealed class ConfigStorageCollection;

[Collection("Config storage")]
public sealed class ConfigStorageTests : IDisposable
{
    private readonly string _configPath = ConfigStorage.GetConfigPath();

    public ConfigStorageTests() => DeleteTestFiles();

    [Fact]
    public void SaveThenLoadRoundTripsConfiguration()
    {
        var id = Guid.NewGuid();
        var config = CreateConfig(id, "primary");

        ConfigStorage.Save(config);
        var loaded = ConfigStorage.Load();

        Assert.Equal(2, loaded.Version);
        Assert.Equal("Dark", loaded.Settings.Theme);
        Assert.Equal("HandleViewer", loaded.Settings.StartPage);
        Assert.True(loaded.Settings.ConfirmBeforeExit);
        Assert.Equal(10, loaded.Settings.PortAutoRefreshSeconds);
        var tunnel = Assert.Single(loaded.Tunnels);
        Assert.Equal(id, tunnel.Id);
        Assert.Equal("primary", tunnel.Name);
        Assert.Equal(AuthMethod.PrivateKey, tunnel.AuthMethod);
    }

    [Fact]
    public void CorruptPrimaryFallsBackToPreviousBackup()
    {
        ConfigStorage.Save(CreateConfig(Guid.NewGuid(), "previous"));
        ConfigStorage.Save(CreateConfig(Guid.NewGuid(), "current"));
        File.WriteAllText(_configPath, "{ invalid json");

        var loaded = ConfigStorage.Load();

        Assert.Equal("previous", Assert.Single(loaded.Tunnels).Name);
    }

    [Fact]
    public void SaveSettingsPreservesExistingTunnels()
    {
        var id = Guid.NewGuid();
        ConfigStorage.Save(CreateConfig(id, "keep-me"));

        ConfigStorage.SaveSettings(new AppSettings
        {
            Theme = "Light",
            StartPage = "Tunnel",
            ConfirmBeforeExit = false,
            PortAutoRefreshSeconds = 30
        });
        var loaded = ConfigStorage.Load();

        Assert.Equal(id, Assert.Single(loaded.Tunnels).Id);
        Assert.Equal("Light", loaded.Settings.Theme);
        Assert.Equal("Tunnel", loaded.Settings.StartPage);
        Assert.False(loaded.Settings.ConfirmBeforeExit);
        Assert.Equal(30, loaded.Settings.PortAutoRefreshSeconds);
    }

    public void Dispose() => DeleteTestFiles();

    private static ConfigFile CreateConfig(Guid id, string name) => new()
    {
        Version = 2,
        Settings = new AppSettings
        {
            AutoStartMinimized = true,
            MinimizeToTrayOnClose = true,
            ConfirmBeforeExit = true,
            ShowTrayNotifications = false,
            Theme = "Dark",
            StartPage = "HandleViewer",
            LastPage = "Tunnel",
            PortAutoRefreshSeconds = 10,
            ShowSystemProcesses = true
        },
        Tunnels =
        [
            new TunnelConfig
            {
                Id = id,
                Name = name,
                AuthMethod = AuthMethod.PrivateKey,
                KeyFilePath = @"C:\keys\id_ed25519"
            }
        ]
    };

    private void DeleteTestFiles()
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        foreach (var name in new[] { "config.json", "config.json.bak", "config.json.tmp" })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
