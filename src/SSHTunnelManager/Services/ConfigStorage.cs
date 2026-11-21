using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SSHTunnelManager.Models;

namespace SSHTunnelManager.Services;

public class ConfigStorage
{
    private static readonly object s_fileLock = new();

    // 紧邻可执行文件存放数据，兼容提权与单文件解压运行
    private static string s_appDir = Path.Combine(AppContext.BaseDirectory, "data");

    // Legacy location used before the admin-mode change; used only for one-time migration.
    private static readonly string? s_legacyAppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SSHTunnelManager");

    private static string ConfigPath => Path.Combine(s_appDir, "config.json");
    private static string BackupPath => Path.Combine(s_appDir, "config.json.bak");
    private static string TemporaryPath => Path.Combine(s_appDir, "config.json.tmp");

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void ConfigureExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        string? executableDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(executableDirectory))
            s_appDir = Path.Combine(executableDirectory, "data");
    }

    public static ConfigFile Load()
    {
        lock (s_fileLock)
        {
            Directory.CreateDirectory(s_appDir);
            MigrateIfNeeded();

            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<ConfigFile>(json, s_jsonOpts);
                    if (config != null)
                        return config;
                }
                catch
                {
                    // 主配置损坏，尝试回退备份
                }
            }

            if (File.Exists(BackupPath))
            {
                try
                {
                    var json = File.ReadAllText(BackupPath);
                    var config = JsonSerializer.Deserialize<ConfigFile>(json, s_jsonOpts);
                    if (config != null)
                        return config;
                }
                catch
                {
                    // 备份配置不可用
                }
            }

            return new ConfigFile { Version = 2 };
        }
    }

    // One-time move of an existing %APPDATA% config into the new portable location
    // so the user doesn't lose saved tunnels after switching to admin mode.
    private static void MigrateIfNeeded()
    {
        if (File.Exists(ConfigPath) || s_legacyAppDir == null)
            return;

        var legacyConfig = Path.Combine(s_legacyAppDir, "config.json");
        if (!File.Exists(legacyConfig))
            return;

        try
        {
            File.Copy(legacyConfig, ConfigPath, overwrite: false);
        }
        catch
        {
            // Non-critical: if migration fails the app just starts empty.
        }
    }

    public static void Save(ConfigFile config)
    {
        lock (s_fileLock)
        {
            Directory.CreateDirectory(s_appDir);

            // 覆盖前备份当前配置
            if (File.Exists(ConfigPath))
            {
                try { File.Copy(ConfigPath, BackupPath, overwrite: true); }
                catch { }
            }

            // 写入临时文件再原子替换
            var json = JsonSerializer.Serialize(config, s_jsonOpts);
            File.WriteAllText(TemporaryPath, json);
            File.Move(TemporaryPath, ConfigPath, overwrite: true);
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        lock (s_fileLock)
        {
            var config = Load();
            config.Settings = settings;
            Save(config);
        }
    }

    public static string GetConfigPath() => ConfigPath;
}
