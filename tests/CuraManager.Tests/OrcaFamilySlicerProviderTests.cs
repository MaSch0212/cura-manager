using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class OrcaFamilySlicerProviderTests
{
    private sealed class FakeMsixPackageLocator(params MsixPackage[] packages) : IMsixPackageLocator
    {
        public IEnumerable<MsixPackage> FindPackages(string packageNamePrefix) =>
            packages.Where(p =>
                p.PackageFullName.StartsWith(packageNamePrefix, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static SlicerInstallation Install(string programFilesPath, string displayName = "x") =>
        new(new Version(1, 0), displayName, programFilesPath, "C:\\AppData", true);

    [Fact]
    public void DeduplicateByProgramFilesPath_DropsLaterDuplicate()
    {
        var first = Install(@"C:\Program Files\OrcaSlicer");
        var second = Install(
            @"C:\Program Files\WindowsApps\OrcaSlicer.OrcaSlicer_2.4.3.0_x64__hash"
        );
        var duplicateOfFirst = Install(@"C:\Program Files\OrcaSlicer", "different display name");

        var result = OrcaFamilySlicerProvider
            .DeduplicateByProgramFilesPath([first, second, duplicateOfFirst])
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Same(first, result[0]);
        Assert.Same(second, result[1]);
    }

    [Fact]
    public void DeduplicateByProgramFilesPath_IsCaseInsensitive()
    {
        var first = Install(@"C:\Program Files\OrcaSlicer");
        var caseVariant = Install(@"c:\program files\orcaslicer");

        var result = OrcaFamilySlicerProvider
            .DeduplicateByProgramFilesPath([first, caseVariant])
            .ToList();

        Assert.Single(result);
    }

    [Fact]
    public void DeduplicateByProgramFilesPath_EmptyInput_YieldsNothing()
    {
        var result = OrcaFamilySlicerProvider.DeduplicateByProgramFilesPath([]).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void GetVersion_ExecutableWithNoVersionResource_FallsBackToMsixPackageIdentity()
    {
        // A Store-packaged orca-slicer.exe carries no FileVersion/ProductVersion at all
        // (both come back empty from FileVersionInfo), so this is the only way to learn
        // its version -- from the package identity, matched by install location.
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("orca-slicer.exe"), [1, 2, 3, 4]);

        var package = new MsixPackage(
            "OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            scope.Path,
            new Version(2, 4, 3, 0)
        );
        var provider = new OrcaSlicerProvider(new FakeMsixPackageLocator(package));

        var version = provider.GetVersion(scope.Path);

        Assert.Equal(new Version(2, 4, 3, 0), version);
    }

    [Fact]
    public void GetVersion_ConfiguredPathHasTrailingSeparator_StillMatchesMsixPackage()
    {
        // Settings.ProgramFilesPath is a user-editable TextBox; a path pasted from
        // Explorer's address bar commonly carries a trailing '\' that InstallLocation
        // (read straight from the registry) never has. That must not break the match.
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("orca-slicer.exe"), [1, 2, 3, 4]);

        var package = new MsixPackage(
            "OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            scope.Path,
            new Version(2, 4, 3, 0)
        );
        var provider = new OrcaSlicerProvider(new FakeMsixPackageLocator(package));

        var version = provider.GetVersion(scope.Path + Path.DirectorySeparatorChar);

        Assert.Equal(new Version(2, 4, 3, 0), version);
    }

    [Fact]
    public void GetVersion_MsixInstallLocationHasTrailingSeparator_StillMatchesConfiguredPath()
    {
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("orca-slicer.exe"), [1, 2, 3, 4]);

        var package = new MsixPackage(
            "OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            scope.Path + Path.DirectorySeparatorChar,
            new Version(2, 4, 3, 0)
        );
        var provider = new OrcaSlicerProvider(new FakeMsixPackageLocator(package));

        var version = provider.GetVersion(scope.Path);

        Assert.Equal(new Version(2, 4, 3, 0), version);
    }

    [Fact]
    public void GetVersion_NoMatchingMsixPackage_ReturnsNull()
    {
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("orca-slicer.exe"), [1, 2, 3, 4]);

        var unrelatedPackage = new MsixPackage(
            "OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            @"C:\Program Files\WindowsApps\OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            new Version(2, 4, 3, 0)
        );
        var provider = new OrcaSlicerProvider(new FakeMsixPackageLocator(unrelatedPackage));

        var version = provider.GetVersion(scope.Path);

        Assert.Null(version);
    }

    [Fact]
    public void GetVersion_NoLocatorSupplied_DoesNotThrow_ReturnsNull()
    {
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("orca-slicer.exe"), [1, 2, 3, 4]);

        var provider = new OrcaSlicerProvider();

        var version = provider.GetVersion(scope.Path);

        Assert.Null(version);
    }

    [Fact]
    public void GetVersion_FlavourNotMsixDistributed_NeverConsultsLocator()
    {
        // AnycubicSlicerProvider leaves MsixPackageNamePrefix null, so even a locator
        // that would match must never be asked -- this flavour is not Store-distributed.
        using var scope = new TestZip.Scope();
        File.WriteAllBytes(scope.File("AnycubicSlicerNext.exe"), [1, 2, 3, 4]);

        var provider = new AnycubicSlicerProvider();

        var version = provider.GetVersion(scope.Path);

        Assert.Null(version);
    }
}
