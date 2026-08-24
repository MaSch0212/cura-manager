using System;
using CuraManager.Services;
using Xunit;

namespace CuraManager.Tests;

public class WindowsMsixPackageLocatorTests
{
    [Fact]
    public void TryParsePackageFullName_RealOrcaSlicerPackage_ParsesNameAndVersion()
    {
        var success = WindowsMsixPackageLocator.TryParsePackageFullName(
            "OrcaSlicer.OrcaSlicer_2.4.3.0_x64__3qd7h69xpne0g",
            out var name,
            out var version
        );

        Assert.True(success);
        Assert.Equal("OrcaSlicer.OrcaSlicer", name);
        Assert.Equal(new Version(2, 4, 3, 0), version);
    }

    [Fact]
    public void TryParsePackageFullName_NoUnderscores_Fails()
    {
        var success = WindowsMsixPackageLocator.TryParsePackageFullName(
            "SomePackageWithNoUnderscores",
            out var name,
            out var version
        );

        Assert.False(success);
        Assert.Null(name);
        Assert.Null(version);
    }

    [Fact]
    public void TryParsePackageFullName_MiddleSegmentIsNotAVersion_Fails()
    {
        var success = WindowsMsixPackageLocator.TryParsePackageFullName(
            "OrcaSlicer.OrcaSlicer_notaversion_x64__3qd7h69xpne0g",
            out var name,
            out var version
        );

        Assert.False(success);
        Assert.Null(name);
        Assert.Null(version);
    }

    [Fact]
    public void TryParsePackageFullName_EmptyString_Fails()
    {
        var success = WindowsMsixPackageLocator.TryParsePackageFullName(
            string.Empty,
            out var name,
            out var version
        );

        Assert.False(success);
        Assert.Null(name);
        Assert.Null(version);
    }
}
