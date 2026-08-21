using CuraManager.Models;
using Xunit;

namespace CuraManager.Tests;

public class SmokeTests
{
    [Fact]
    public void CanReferenceTheApplicationAssembly()
    {
        var settings = new CuraManagerSettings();

        Assert.True(settings.UpdateCuraProjectsOnOpen);
    }
}
