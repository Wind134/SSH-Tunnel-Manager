using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SSHTunnelManager.Models;

namespace SSHTunnelManager.Services;

public class ConfigStorage
{
    // Store next to the executable (not %APPDATA%) so the config survives UAC
    // elevation (running as administrator redirects %APPDATA% to the admin profile).
    // The WinUI single-file host extracts all bundled content before launch,
    // which makes AppContext.BaseDirectory point into %TEMP%. The executable
    // host configures this path at startup so data survives bundle updates.
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
                // config.json is corrupted, try backup
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
                // backup also corrupted
            }
        }

        return new ConfigFile { Version = 2 };
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
        Directory.CreateDirectory(s_appDir);

        // backup current file before overwriting
        if (File.Exists(ConfigPath))
        {
            try { File.Copy(ConfigPath, BackupPath, overwrite: true); }
            catch { /* non-critical */ }
        }

        // atomic write: write to temp file, then rename
        var json = JsonSerializer.Serialize(config, s_jsonOpts);
        File.WriteAllText(TemporaryPath, json);

        // File.Move with overwrite:true is a single atomic rename on Windows
        // (the old delete-then-move sequence left a window where a crash could
        // lose the config entirely; the .bak fallback covered it, but this is
        // strictly safer).
        File.Move(TemporaryPath, ConfigPath, overwrite: true);
    }

    public static void SaveSettings(AppSettings settings)
    {
        var config = Load();
        config.Settings = settings;
        Save(config);
    }

    public static string GetConfigPath() => ConfigPath;
}
