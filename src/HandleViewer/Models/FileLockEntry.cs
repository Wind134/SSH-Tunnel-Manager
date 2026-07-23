namespace HandleViewer.Models;

/// <summary>
/// Represents a single process that holds a lock on a queried file,
/// as reported by the Windows Restart Manager API.
/// </summary>
public class FileLockEntry
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;
}