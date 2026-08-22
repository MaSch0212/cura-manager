using System;
using System.Collections.Generic;
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class OrcaSlicerProviderTests
{
    private const string OrcaModel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <model unit="millimeter">
          <metadata name="Application">BambuStudio-02.06.00.51</metadata>
          <metadata name="OrcaSlicer">2.4.2</metadata>
          <metadata name="BambuStudio:3mfVersion">1</metadata>
        </model>
        """;

    private const string BambuModel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <model unit="millimeter">
          <metadata name="Application">BambuStudio-02.08.02.61</metadata>
          <metadata name="BambuStudio:3mfVersion">1</metadata>
        </model>
        """;

    private const string BblSliceInfo =
        "<config><header><header_item key=\"X-BBL-Client-Type\" value=\"slicer\"/></header></config>";

    private const string AcNextSliceInfo =
        "<config><header><header_item key=\"X-ACNext-Client-Type\" value=\"slicer\"/></header></config>";

    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private static SlicerMatch Match(string path)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());
        return new OrcaSlicerProvider().IsProjectFile(candidate);
    }

    private static SlicerMatch AnycubicMatch(string path)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());
        return new AnycubicSlicerProvider().IsProjectFile(candidate);
    }

    [Fact]
    public void OrcaModelMetadata_IsExactMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", OrcaModel),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        Assert.Equal(SlicerMatch.Exact, Match(path));
    }

    [Fact]
    public void BambuFile_IsOnlyProbable_NotExact()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", BambuModel),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        // Bambu emits the same X-BBL- prefix, so Orca must not claim it exactly.
        Assert.Equal(SlicerMatch.Probable, Match(path));
    }

    [Fact]
    public void AnycubicFile_IsNotAnExactOrcaMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", AcNextSliceInfo)
        );

        Assert.Equal(SlicerMatch.Probable, Match(path));
    }

    [Fact]
    public void OrcaFile_IsNotAnExactAnycubicMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", OrcaModel),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        // The two providers must not both claim the same file exactly.
        Assert.Equal(SlicerMatch.Probable, AnycubicMatch(path));
    }

    [Fact]
    public void CuraProject_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/preferences.cfg", "[general]"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void PlainModel3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void OrcaMarkerBeyondHeaderBound_IsNotDetectedAsExact()
    {
        // The discriminator only reads a bounded prefix of 3D/3dmodel.model (see
        // OrcaFamilySlicerProvider.ModelMetadataHeaderBoundBytes) so that identifying a
        // file never has to decompress its (potentially huge) mesh in full. Padding the
        // marker past that bound must fall back to Probable via the X-BBL- slice info,
        // not silently read further and claim Exact anyway.
        using var scope = new TestZip.Scope();
        var padding = new string('x', 8_300);
        var modelWithLateMarker =
            $"""<?xml version="1.0" encoding="UTF-8"?><model unit="millimeter"><!--{padding}--><metadata name="OrcaSlicer">2.4.2</metadata></model>""";
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", modelWithLateMarker),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        Assert.Equal(SlicerMatch.Probable, Match(path));
    }
}
