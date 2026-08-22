namespace CuraManager.Models;

/// <summary>
/// How confidently a provider claims a project file.
/// </summary>
public enum SlicerMatch
{
    /// <summary>Not this provider's file.</summary>
    None = 0,

    /// <summary>The file's family matches, but the producing application could not be identified.</summary>
    Probable = 1,

    /// <summary>The producing application was positively identified.</summary>
    Exact = 2,
}
