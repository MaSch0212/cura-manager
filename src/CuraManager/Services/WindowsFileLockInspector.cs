namespace CuraManager.Services;

public class WindowsFileLockInspector : IFileLockInspector
{
    public IReadOnlyList<string> GetLockingProcessNames(string filePath)
    {
        var processes = Waiter.Retry(
            () => MaSch.Native.Windows.Explorer.FileInfo.WhoIsLocking(filePath),
            new RetryOptions { ThrowException = false }
        );
        return processes?.Select(x => x.ProcessName).ToArray() ?? Array.Empty<string>();
    }
}
