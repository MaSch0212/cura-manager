namespace CuraManager.Services;

/// <summary>
/// Reports which processes hold a lock on a file. Windows-only today; a future
/// non-Windows port registers a no-op implementation.
/// </summary>
public interface IFileLockInspector
{
    IReadOnlyList<string> GetLockingProcessNames(string filePath);
}
