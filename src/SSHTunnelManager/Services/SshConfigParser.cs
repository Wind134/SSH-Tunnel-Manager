using System.IO;

namespace SSHTunnelManager.Services;

public class SshConfigEntry
{
    public string Host { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string IdentityFile { get; set; } = string.Empty;
}

public static class SshConfigParser
{
    public static List<SshConfigEntry> Parse(string configPath)
    {
        var entries = new List<SshConfigEntry>();
        if (!File.Exists(configPath))
            return entries;

        SshConfigEntry? current = null;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;
            var spaceIdx = line.IndexOfAny(new[] { ' ', '\t' });
            if (spaceIdx < 0) continue;
            var key = line.Substring(0, spaceIdx).Trim();
            var value = line.Substring(spaceIdx + 1).Trim();

            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Contains('*') || value.Contains('?')) continue;
                if (current != null) entries.Add(current);
                current = new SshConfigEntry { Host = value };
            }
            else if (current != null)
            {
                if (key.Equals("HostName", StringComparison.OrdinalIgnoreCase))
                    current.HostName = value;
                else if (key.Equals("User", StringComparison.OrdinalIgnoreCase))
                    current.User = value;
                else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                    current.Port = int.TryParse(value, out var port) ? port : 22;
                else if (key.Equals("IdentityFile", StringComparison.OrdinalIgnoreCase))
                    current.IdentityFile = ExpandTilde(value);
            }
        }
        if (current != null) entries.Add(current);
        return entries;
    }

    private static string ExpandTilde(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
        return path;
    }
}
