using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CuraManager.Tests;

public class AnycubicSlicerProviderTests
{
    private const string AcNextSliceInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <config>
          <header>
            <header_item key="X-ACNext-Client-Type" value="slicer"/>
            <header_item key="X-ACNext-Client-Version" value="1.4.1.2 20260604104233"/>
          </header>
        </config>
        """;

    private const string BblSliceInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <config>
          <header>
            <header_item key="X-BBL-Client-Type" value="slicer"/>
          </header>
        </config>
        """;

    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private static SlicerMatch Match(string path)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());
        return new AnycubicSlicerProvider().IsProjectFile(candidate);
    }

    /// <summary>
    /// Parses a written config the way the provider itself does: load only the first
    /// JSON value, ignoring whatever trailing "# MD5 checksum ..." comment follows it.
    /// </summary>
    private static JObject ParseWrittenConfig(string path)
    {
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)));
        reader.Read();
        return (JObject)JToken.Load(reader);
    }

    [Fact]
    public void AcNextHeader_IsExactMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", AcNextSliceInfo)
        );

        Assert.Equal(SlicerMatch.Exact, Match(path));
    }

    [Fact]
    public void BblHeader_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        Assert.Equal(SlicerMatch.Probable, Match(path));
    }

    [Fact]
    public void ProjectSettingsWithoutSliceInfo_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/project_settings.config", "{}"));

        Assert.Equal(SlicerMatch.Probable, Match(path));
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
    public void SetLastExportPath_NestedUnderApp_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        File.WriteAllText(
            configPath,
            """
            {
              "app": { "last_export_path": "C:\\old", "language": "en" },
              "presets": { "filament": "PLA Basic" }
            }
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var result = ParseWrittenConfig(configPath);
        Assert.Equal("D:\\Prints\\Widget", (string)result["app"]["last_export_path"]);
        Assert.Equal("en", (string)result["app"]["language"]);
        Assert.Equal("PLA Basic", (string)result["presets"]["filament"]);
    }

    [Fact]
    public void SetLastExportPath_AtRoot_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        File.WriteAllText(
            configPath,
            """
            { "last_export_path": "C:\\old", "presets": { "filament": "PLA Basic" } }
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var result = ParseWrittenConfig(configPath);
        Assert.Equal("D:\\Prints\\Widget", (string)result["last_export_path"]);
        Assert.Equal("PLA Basic", (string)result["presets"]["filament"]);
    }

    [Fact]
    public void SetLastExportPath_MissingFile_DoesNotThrow()
    {
        using var scope = new TestZip.Scope();

        OrcaFamilySlicerProvider.SetLastExportPath(scope.File("absent.conf"), "D:\\Prints");
    }

    [Fact]
    public void SetLastExportPath_TrailingChecksumComment_DoesNotThrow()
    {
        // The shipped Anycubic Slicer Next / OrcaSlicer config ends with a non-JSON
        // "# MD5 checksum ..." comment line after the closing brace.
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        File.WriteAllText(
            configPath,
            """
            { "app": { "last_export_path": "C:\\old" } }
            # MD5 checksum 39D15760CF845EB9CCBFF50FB44AF44A
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var result = ParseWrittenConfig(configPath);
        Assert.Equal("D:\\Prints\\Widget", (string)result["app"]["last_export_path"]);
    }

    [Fact]
    public void SetLastExportPath_WritesChecksumThatMatchesTheSlicersOwnAlgorithm_AndEndsWithNewline()
    {
        // AppConfig.cpp's loader computes MD5 over the config text (CRLF normalized to
        // LF, as its text-mode read would do) up to and including the last '}'. A file
        // ending in '}' with nothing after it makes the loader's own
        // substr(last_pos + 2) throw std::out_of_range on next launch, uncaught by the
        // surrounding JSON-parse-error handler — so the trailing newline is asserted
        // explicitly, not just implied by the checksum line being present.
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        File.WriteAllText(
            configPath,
            """
            { "app": { "last_export_path": "C:\\old" } }
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var written = File.ReadAllText(configPath);
        Assert.EndsWith("\n", written);

        var lines = written.TrimEnd('\n').Split('\n');
        var checksumLine = Assert.Single(lines, l => l.StartsWith("# MD5 checksum "));
        var actualHash = checksumLine["# MD5 checksum ".Length..];

        var jsonText = written[..written.IndexOf("\n# MD5 checksum ", StringComparison.Ordinal)];
        var normalized = jsonText.Replace("\r\n", "\n");
        var toHash = normalized[..(normalized.LastIndexOf('}') + 1)];
        var expectedHash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(toHash)));

        Assert.Equal(expectedHash, actualHash);
    }
}
