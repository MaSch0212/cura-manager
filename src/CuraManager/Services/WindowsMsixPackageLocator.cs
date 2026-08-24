using System.IO;
using System.Security;
using CuraManager.Models;
using Microsoft.Win32;

namespace CuraManager.Services;

/// <summary>
/// Reads installed MSIX packages from the per-user package repository in the
/// registry. This is the only way to discover a Microsoft Store install without
/// administrator rights: <c>C:\Program Files\WindowsApps</c> itself cannot be
/// enumerated by a normal user (<see cref="Directory.EnumerateDirectories(string)"/>
/// throws <see cref="UnauthorizedAccessException"/> there), but this registry key can
/// always be read.
/// </summary>
public class WindowsMsixPackageLocator : IMsixPackageLocator
{
    private const string PackagesKeyPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public IEnumerable<MsixPackage> FindPackages(string packageNamePrefix)
    {
        var results = new List<MsixPackage>();

        using var packagesKey = OpenKey(Registry.CurrentUser, PackagesKeyPath);
        if (packagesKey == null)
            return results;

        var subKeyNames = TryGetSubKeyNames(packagesKey);
        foreach (var subKeyName in subKeyNames)
        {
            if (!TryParsePackageFullName(subKeyName, out var name, out var version))
                continue;
            if (!name.StartsWith(packageNamePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            using var packageKey = OpenKey(packagesKey, subKeyName);
            var installLocation = TryGetInstallLocation(packageKey);
            if (string.IsNullOrEmpty(installLocation))
                continue;

            results.Add(new MsixPackage(subKeyName, installLocation, version));
        }

        return results;
    }

    /// <summary>
    /// Parses an MSIX package full name of the form
    /// <c>&lt;Name&gt;_&lt;Version&gt;_&lt;Arch&gt;__&lt;PublisherHash&gt;</c> (e.g.
    /// <c>OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g</c>), extracting the package
    /// name and version out of the identity rather than the executable -- the only
    /// place a Store package's version is recorded, since the packaged exe itself ships
    /// no version resource at all. Pure and side-effect free so it can be unit-tested
    /// directly, separate from the registry access that is not.
    /// </summary>
    /// <remarks>
    /// The naive <see cref="string.Split(char[])"/> on <c>'_'</c> is safe rather than
    /// fragile: an MSIX package Identity <c>Name</c> cannot itself contain an underscore
    /// (the platform reserves <c>_</c> as the full-name field delimiter), and package
    /// versions are always emitted four-part (<c>Major.Minor.Build.Revision</c>), so the
    /// second split segment is always a plain version string, never a name fragment that
    /// happened to look like one.
    /// </remarks>
    internal static bool TryParsePackageFullName(
        string packageFullName,
        out string name,
        out Version version
    )
    {
        name = null;
        version = null;

        if (string.IsNullOrEmpty(packageFullName))
            return false;

        var parts = packageFullName.Split('_');
        if (parts.Length < 2 || !Version.TryParse(parts[1], out var parsedVersion))
            return false;

        name = parts[0];
        version = parsedVersion;
        return true;
    }

    /// <summary>
    /// Opens a registry subkey, degrading to <see langword="null"/> instead of throwing
    /// when the key is missing or unreadable -- external registry state (a stale
    /// package entry, a permissions quirk) must never take down slicer discovery.
    /// </summary>
    private static RegistryKey OpenKey(RegistryKey parent, string subKeyName)
    {
        try
        {
            return parent.OpenSubKey(subKeyName);
        }
        catch (Exception ex)
            when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string[] TryGetSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception ex)
            when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static string TryGetInstallLocation(RegistryKey packageKey)
    {
        if (packageKey == null)
            return null;

        try
        {
            return packageKey.GetValue("PackageRootFolder") as string;
        }
        catch (Exception ex)
            when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
