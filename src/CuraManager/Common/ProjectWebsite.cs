namespace CuraManager.Common;

/// <summary>
/// Helpers for the optional URL a print project can be associated with (the page the models
/// were downloaded from). The URL is stored in the project's <c>metadata.json</c> and is opened
/// with the shell, so it has to be an absolute web address.
/// </summary>
public static class ProjectWebsite
{
    /// <summary>
    /// Determines whether the specified value can be stored as a print project's URL.
    /// </summary>
    /// <param name="url">The value to check.</param>
    /// <returns>
    /// <c>true</c> if <paramref name="url"/> is empty (meaning the project has no URL) or an
    /// absolute <c>http</c>/<c>https</c> address; otherwise, <c>false</c>.
    /// </returns>
    public static bool IsValid(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Normalizes a URL entered by the user for storage in a project's metadata.
    /// </summary>
    /// <param name="url">The value to normalize.</param>
    /// <returns>The trimmed URL, or <c>null</c> when <paramref name="url"/> is empty.</returns>
    public static string Normalize(string url)
    {
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }
}
