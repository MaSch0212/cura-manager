namespace CuraManager.Models;

/// <summary>
/// An installed MSIX (Microsoft Store) package, resolved from the per-user package
/// repository in the registry rather than by filesystem enumeration.
/// </summary>
public record MsixPackage(string PackageFullName, string InstallLocation, Version Version);
