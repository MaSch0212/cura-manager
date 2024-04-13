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
        var versionMatch = RegularExpressions.Version().Match(version);
        if (!versionMatch.Success)
            return null;

        version = versionMatch.Value;
        if (!version.Contains("."))
            version += ".0";

        return Version.Parse(version).Normalize();
    }
}
