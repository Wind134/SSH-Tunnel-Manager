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

/// <summary>
/// Result of checking either a file or a directory for lock owners.
/// </summary>
public sealed class PathLockQueryResult
{
    public string QueriedPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public IReadOnlyList<FileLockEntry> Entries { get; init; } = Array.Empty<FileLockEntry>();
    public int ScannedFileCount { get; init; }
    public int SkippedDirectoryCount { get; init; }
    public bool WasTruncated { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}
