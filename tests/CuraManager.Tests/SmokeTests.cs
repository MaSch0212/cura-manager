using CuraManager.Models;
using Xunit;

namespace CuraManager.Tests;

public class SmokeTests
{
    [Fact]
    public void CanReferenceTheApplicationAssembly()
    {
        var settings = new AppSettings();

        Assert.True(settings.UpdateCuraProjectsOnOpen);
    }
}
