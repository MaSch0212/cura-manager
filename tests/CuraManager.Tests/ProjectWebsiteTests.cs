using CuraManager.Common;
using Xunit;

namespace CuraManager.Tests;

public class ProjectWebsiteTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyUrl_IsValid_BecauseProjectsMayHaveNoUrl(string url)
    {
        Assert.True(ProjectWebsite.IsValid(url));
    }

    [Theory]
    [InlineData("https://www.thingiverse.com/thing:123")]
    [InlineData("http://example.com")]
    [InlineData("  https://example.com/a/b?c=d  ")]
    public void AbsoluteWebUrls_AreValid(string url)
    {
        Assert.True(ProjectWebsite.IsValid(url));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("www.thingiverse.com")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file.stl")]
    [InlineData("file:///C:/models")]
    public void NonWebOrRelativeUrls_AreInvalid(string url)
    {
        Assert.False(ProjectWebsite.IsValid(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_TurnsEmptyValuesIntoNull(string url)
    {
        Assert.Null(ProjectWebsite.Normalize(url));
    }

    [Fact]
    public void Normalize_TrimsSurroundingWhitespace()
    {
        Assert.Equal("https://example.com", ProjectWebsite.Normalize("  https://example.com  "));
    }
}
