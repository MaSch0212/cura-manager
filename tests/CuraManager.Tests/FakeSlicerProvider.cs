using System;
using System.Collections.Generic;
using CuraManager.Models;
using CuraManager.Services.Slicers;

namespace CuraManager.Tests;

internal sealed class FakeSlicerProvider : ISlicerProvider
{
    public FakeSlicerProvider(string id, SlicerMatch match = SlicerMatch.None)
    {
        Id = id;
        Match = match;
    }

    public string Id { get; }
    public string DisplayName => Id;
    public string IconResourceKey => "CuraIcon";
    public bool SupportsProfileUpdateOnOpen => false;
    public Version LatestSupportedVersion => new(1, 0);

    public SlicerMatch Match { get; set; }
    public IList<SlicerInstallation> Installations { get; } = new List<SlicerInstallation>();

    public IEnumerable<SlicerInstallation> FindInstallations() => Installations;

    public Version GetVersion(string programFilesPath) => new(1, 0);

    public bool ArePathsValid(SlicerSettings settings) => true;

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate) => Match;

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request) { }

    public void OpenProject(SlicerSettings settings, string projectFilePath) { }
}
