namespace CuraManager.Extensions;

public static class VersionExtensions
{
    public static Version Normalize(this Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0)
        );
    }

    public static Version SafeParse(string version)
    {
        // FileVersionInfo.FileVersion/ProductVersion come back null (not "") for an
        // executable with no version resource at all -- as seen on a Microsoft
        // Store-packaged orca-slicer.exe -- and Regex.Match throws on a null input.
        if (string.IsNullOrEmpty(version))
            return null;

        var versionMatch = RegularExpressions.Version().Match(version);
        if (!versionMatch.Success)
            return null;

        version = versionMatch.Value;
        if (!version.Contains("."))
            version += ".0";

        return Version.Parse(version).Normalize();
    }
}
