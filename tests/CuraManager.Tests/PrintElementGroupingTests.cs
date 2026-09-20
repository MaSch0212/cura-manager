using CuraManager.Models;
using Xunit;

namespace CuraManager.Tests;

public class PrintElementGroupingTests
{
    [Fact]
    public void ModelExtensions_GoToModelFiles()
    {
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".stl"));
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".obj"));
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".x3d"));
    }

    [Fact]
    public void StepExtensions_GoToModelFiles()
    {
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".stp"));
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".step"));
    }

    [Fact]
    public void ThreeMf_NeedsSlicerInspection()
    {
        Assert.Equal(
            PrintElementFileCategory.MaybeSlicerProject,
            PrintElement.CategorizeByExtension(".3mf")
        );
    }

    [Fact]
    public void UnknownExtension_GoesToOtherFiles()
    {
        Assert.Equal(PrintElementFileCategory.Other, PrintElement.CategorizeByExtension(".txt"));
    }

    [Fact]
    public void ExtensionMatchingIsCaseInsensitive()
    {
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".STL"));
        Assert.Equal(
            PrintElementFileCategory.MaybeSlicerProject,
            PrintElement.CategorizeByExtension(".3MF")
        );
        Assert.Equal(PrintElementFileCategory.Model, PrintElement.CategorizeByExtension(".STEP"));
    }
}
