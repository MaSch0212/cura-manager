using CuraManager.Models;
using Xunit;

namespace CuraManager.Tests;

public class SmokeTests
{
    [Fact]
    public void CanReferenceTheApplicationAssembly()
    {
        Assert.NotNull(new AppSettings().Slicers);
    }
}
