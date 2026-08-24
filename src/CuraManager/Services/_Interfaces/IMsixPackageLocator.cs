using CuraManager.Models;

namespace CuraManager.Services;

/// <summary>
/// Locates installed MSIX (Microsoft Store) packages. Windows-only today; a future
/// non-Windows port registers a no-op implementation.
/// </summary>
public interface IMsixPackageLocator
{
    /// <summary>Installed MSIX packages whose package name starts with <paramref name="packageNamePrefix"/>.</summary>
    IEnumerable<MsixPackage> FindPackages(string packageNamePrefix);
}
