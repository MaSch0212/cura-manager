using CuraManager.Models;

namespace CuraManager.Services.Slicers;

public interface ISlicerProvider
{
    /// <summary>Stable identifier, used as the settings dictionary key. Never localized.</summary>
    string Id { get; }

    /// <summary>Product name. Never translated.</summary>
    string DisplayName { get; }

    /// <summary>Key of an <c>m:Icon</c> resource in <c>Resources/Geometries.xaml</c>.</summary>
    string IconResourceKey { get; }

    /// <summary>
    /// Whether the settings page offers the "update profiles before open" option.
    /// A UI-rendering hint; the behaviour itself lives in <see cref="OpenProject"/>.
    /// </summary>
    bool SupportsProfileUpdateOnOpen { get; }

    Version LatestSupportedVersion { get; }

    IEnumerable<SlicerInstallation> FindInstallations();
    Version GetVersion(string programFilesPath);
    bool ArePathsValid(SlicerSettings settings);

    SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate);
    void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request);
    void OpenProject(SlicerSettings settings, string projectFilePath);
}
