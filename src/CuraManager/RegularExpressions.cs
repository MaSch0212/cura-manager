namespace CuraManager
{
    internal static partial class RegularExpressions
    {
        [GeneratedRegex("""CuraVersion\s*=\s*\"?(?<version>\d+(\.\d+){0,3})\"?""")]
        public static partial Regex ExtractCuraVersionFromPython();

        [GeneratedRegex("""release-(?<version>\d+(\.\d+){0,3})$""")]
        public static partial Regex ExtractVersionFromReleaseTag();

        [GeneratedRegex("""(?<id>\d+)\/?\Z""")]
        public static partial Regex MyMiniFactoryId();

        [GeneratedRegex("""https?:\/\/(www\.)?thingiverse\.com\/thing:\d+""")]
        public static partial Regex ThingiverseUrl();

        [GeneratedRegex("""\d+(\.\d+){0,3}""")]
        public static partial Regex Version();
    }
}
