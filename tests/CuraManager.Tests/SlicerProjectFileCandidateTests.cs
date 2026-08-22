using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [Fact]
    public void CentralDirectoryTruncatedAfterOpenSucceeds_FallsBackToLockingProcessNames()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/slice_info.config", "<config/>"));

        // ZipArchive.Entries parses the central directory lazily, on first access -- not
        // inside ZipFile.OpenRead. To exercise that path, keep every local file header/data
        // byte intact, drop only the back half of the central directory's file header
        // records, and reattach the original End Of Central Directory record unmodified.
        // The EOCD is then internally consistent (so OpenRead succeeds), but its declared
        // entry count cannot be satisfied by the truncated central directory, so accessing
        // Entries throws only once Create reaches that line.
        var bytes = File.ReadAllBytes(path);
        const int eocdSize = 22;
        var eocd = bytes[^eocdSize..];
        var centralDirectoryOffset = (int)BitConverter.ToUInt32(eocd, 16);
        var centralDirectorySize = (int)BitConverter.ToUInt32(eocd, 12);
        var truncated = bytes[..(centralDirectoryOffset + (centralDirectorySize / 2))]
            .Concat(eocd)
            .ToArray();
        File.WriteAllBytes(path, truncated);

        var candidate = SlicerProjectFileCandidate.Create(path, new StubLocks("Cura"));

        Assert.Empty(candidate.ZipEntryNames);
        Assert.Equal(new[] { "Cura" }, candidate.LockingProcessNames);

        candidate.Dispose();

        // ZipFile.OpenRead holds the file with FileShare.Read, which excludes delete
        // sharing. If Create leaked the archive on the lazy-Entries throw, this delete
        // fails with IOException. It is the only externally observable difference
        // between the fixed and unfixed code, since both return the same fallback.
        System.IO.File.Delete(path);
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void ReadEntryTextPrefix_TruncatesToMaxBytes()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "0123456789"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Equal("01234", candidate.ReadEntryTextPrefix("3D/3dmodel.model", 5));
    }

    [Fact]
    public void ReadEntryTextPrefix_EntryShorterThanBound_ReturnsWholeEntry()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "hi"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Equal("hi", candidate.ReadEntryTextPrefix("3D/3dmodel.model", 5_000));
    }

    [Fact]
    public void ReadEntryTextPrefix_ReturnsNullForMissingEntry()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/plugin.json", "{}"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Null(candidate.ReadEntryTextPrefix("3D/3dmodel.model", 5_000));
    }

    [Fact]
    public void ReadEntryText_CorruptEntryData_ReturnsNullInsteadOfThrowing()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/slice_info.config", "<config/>"));
        CorruptEntryCompressionMethod(path, "Metadata/slice_info.config");

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        // The central directory is untouched -- the entry is still listed -- but the
        // compression method it declares is now bogus, so opening/reading it throws
        // InvalidDataException. That must be swallowed, not left to escape to the caller.
        Assert.True(candidate.HasEntry("Metadata/slice_info.config"));
        Assert.Null(candidate.ReadEntryText("Metadata/slice_info.config"));

        // Memoized: a second call must not re-attempt the decompression either.
        Assert.Null(candidate.ReadEntryText("Metadata/slice_info.config"));
    }

    [Fact]
    public void ReadEntryTextPrefix_CorruptEntryData_ReturnsNullInsteadOfThrowing()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));
        CorruptEntryCompressionMethod(path, "3D/3dmodel.model");

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Null(candidate.ReadEntryTextPrefix("3D/3dmodel.model", 100));
    }

    private sealed class StubLocks(params string[] names) : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) => names;
    }

    /// <summary>
    /// Patches the compression-method field, in both the local file header and the
    /// central directory record, for a single-entry zip. This leaves the central
    /// directory (and therefore <c>ZipArchive.Entries</c>/<c>GetEntry</c>) perfectly
    /// readable while making <see cref="System.IO.Compression.ZipArchiveEntry.Open"/>
    /// throw <see cref="System.IO.InvalidDataException"/> for that entry -- reproducing
    /// "valid central directory, corrupt entry stream" without needing a real damaged
    /// 3mf on disk.
    /// </summary>
    private static void CorruptEntryCompressionMethod(string path, string entryName)
    {
        var bytes = File.ReadAllBytes(path);
        const int eocdSize = 22;
        var eocd = bytes[^eocdSize..];
        var centralDirectoryOffset = (int)BitConverter.ToUInt32(eocd, 16);

        // Single-entry archive: the one central directory record starts right there, and
        // its "relative offset of local header" field (at +42) points back to the local
        // file header this entry's compressed data lives after.
        var localHeaderOffset = (int)BitConverter.ToUInt32(bytes, centralDirectoryOffset + 42);

        var bogusMethod = BitConverter.GetBytes((ushort)99);
        bogusMethod.CopyTo(bytes, centralDirectoryOffset + 10); // central record: compression method
        bogusMethod.CopyTo(bytes, localHeaderOffset + 8); // local file header: compression method

        File.WriteAllBytes(path, bytes);
    }
}
