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
    private static readonly string s_appDir =
        Path.Combine(AppContext.BaseDirectory, "data");

    // Legacy location used before the admin-mode change; used only for one-time migration.
    private static readonly string? s_legacyAppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SSHTunnelManager");

    private static readonly string s_configPath = Path.Combine(s_appDir, "config.json");
    private static readonly string s_backupPath = Path.Combine(s_appDir, "config.json.bak");
    private static readonly string s_tmpPath = Path.Combine(s_appDir, "config.json.tmp");

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ConfigFile Load()
    {
        Directory.CreateDirectory(s_appDir);
        MigrateIfNeeded();

        if (File.Exists(s_configPath))
        {
            try
            {
                var json = File.ReadAllText(s_configPath);
                var config = JsonSerializer.Deserialize<ConfigFile>(json, s_jsonOpts);
                if (config != null)
                    return config;
            }
            catch
            {
                // config.json is corrupted, try backup
            }
        }

        if (File.Exists(s_backupPath))
        {
            try
            {
                var json = File.ReadAllText(s_backupPath);
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
        if (File.Exists(s_configPath) || s_legacyAppDir == null)
            return;

        var legacyConfig = Path.Combine(s_legacyAppDir, "config.json");
        if (!File.Exists(legacyConfig))
            return;

        try
        {
            File.Copy(legacyConfig, s_configPath, overwrite: false);
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
        if (File.Exists(s_configPath))
        {
            try { File.Copy(s_configPath, s_backupPath, overwrite: true); }
            catch { /* non-critical */ }
        }

        // atomic write: write to temp file, then rename
        var json = JsonSerializer.Serialize(config, s_jsonOpts);
        File.WriteAllText(s_tmpPath, json);

        // File.Move with overwrite:true is a single atomic rename on Windows
        // (the old delete-then-move sequence left a window where a crash could
        // lose the config entirely; the .bak fallback covered it, but this is
        // strictly safer).
        File.Move(s_tmpPath, s_configPath, overwrite: true);
    }

    public static void SaveSettings(AppSettings settings)
    {
        var config = Load();
        config.Settings = settings;
        Save(config);
    }

    public static string GetConfigPath() => s_configPath;
}
