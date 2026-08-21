using System;
using System.Collections.Generic;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class SlicerProjectFileCandidateTests
{
    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    [Fact]
    public void ListsZipEntryNames()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/plugin.json", "{}"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.True(candidate.HasEntryStartingWith("Cura/"));
        Assert.False(candidate.HasEntryStartingWith("Metadata/"));
    }

    [Fact]
    public void ReadsEntryText()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/slice_info.config", "<config/>"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Equal("<config/>", candidate.ReadEntryText("Metadata/slice_info.config"));
    }

    [Fact]
    public void ReturnsNullForMissingEntry()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/plugin.json", "{}"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Null(candidate.ReadEntryText("Metadata/slice_info.config"));
    }

    [Fact]
    public void NonZipExtension_YieldsNoEntries()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("a.stl");
        System.IO.File.WriteAllText(path, "solid");

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Empty(candidate.ZipEntryNames);
        Assert.Equal(".stl", candidate.Extension);
    }

    [Fact]
    public void UnreadableZip_FallsBackToLockingProcessNames()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("broken.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        using var candidate = SlicerProjectFileCandidate.Create(path, new StubLocks("Cura"));

        Assert.Empty(candidate.ZipEntryNames);
        Assert.Equal(new[] { "Cura" }, candidate.LockingProcessNames);
    }

    private sealed class StubLocks(params string[] names) : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) => names;
    }
}
