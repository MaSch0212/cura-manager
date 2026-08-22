using System;
using System.Collections.Generic;
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class CuraSlicerProviderTests
{
    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private sealed class StubLocks(params string[] names) : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) => names;
    }

    private static CuraSlicerProvider CreateProvider() => new(automation: null, () => false);

    private static SlicerMatch Match(string path, IFileLockInspector locks = null)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, locks ?? new NoLocks());
        return CreateProvider().IsProjectFile(candidate);
    }

    [Fact]
    public void CuraFolderEntry_IsExactMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", "<model/>"),
            ("Cura/preferences.cfg", "[general]")
        );

        Assert.Equal(SlicerMatch.Exact, Match(path));
    }

    [Fact]
    public void PlainModel3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void OrcaFamily3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/project_settings.config", "{}"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void NonThreeMfFile_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("a.stl");
        System.IO.File.WriteAllText(path, "solid");

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void UnreadableFileLockedByCura_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("locked.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        Assert.Equal(SlicerMatch.Probable, Match(path, new StubLocks("Cura")));
    }

    [Fact]
    public void UnreadableFileLockedBySomethingElse_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("locked.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        Assert.Equal(SlicerMatch.None, Match(path, new StubLocks("notepad")));
    }

    [Fact]
    public void SetSaveDialogPath_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("cura.cfg");
        System.IO.File.WriteAllText(
            configPath,
            """
            [general]
            visible_settings = layer_height;infill_sparse_density
            window_maximized = True

            [local_file]
            dialog_save_path = C:\old

            [cura]
            categories_expanded = material
            """
        );

        CuraSlicerProvider.SetSaveDialogPath(configPath, "D:\\Prints\\Widget");

        var result = System.IO.File.ReadAllText(configPath);
        Assert.Contains("visible_settings = layer_height;infill_sparse_density", result);
        Assert.Contains("window_maximized = True", result);
        Assert.Contains("categories_expanded = material", result);
        Assert.Contains("dialog_save_path = D:/Prints/Widget", result);
        Assert.DoesNotContain("C:\\old", result);
    }

    [Fact]
    public void SetSaveDialogPath_MissingConfigFile_DoesNotThrow()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("cura.cfg"); // never written: simulates an unconfigured install

        var exception = Record.Exception(() =>
            CuraSlicerProvider.SetSaveDialogPath(configPath, "D:\\Prints\\Widget")
        );

        Assert.Null(exception);
    }

    [Fact]
    public void SetSaveDialogPath_NullConfigPath_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            CuraSlicerProvider.SetSaveDialogPath(null, "D:\\Prints\\Widget")
        );

        Assert.Null(exception);
    }
}
