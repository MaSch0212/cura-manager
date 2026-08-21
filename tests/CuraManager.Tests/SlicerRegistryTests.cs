using System;
using System.Collections.Generic;
using System.Linq;
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class SlicerRegistryTests
{
    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private static SlicerRegistry Build(AppSettings settings, params ISlicerProvider[] providers) =>
        new(providers, new NoLocks(), () => settings);

    private static AppSettings SettingsWith(params string[] enabledIds)
    {
        var settings = new AppSettings();
        foreach (var id in enabledIds)
            settings.Slicers[id] = new SlicerSettings { IsEnabled = true };
        return settings;
    }

    [Fact]
    public void EnabledProviders_ExcludesDisabledOnes()
    {
        var registry = Build(
            SettingsWith("a"),
            new FakeSlicerProvider("a"),
            new FakeSlicerProvider("b")
        );

        Assert.Equal(new[] { "a" }, registry.EnabledProviders.Select(x => x.Id));
    }

    [Fact]
    public void ActiveProvider_IsNullWhenNothingEnabled()
    {
        var registry = Build(new AppSettings(), new FakeSlicerProvider("a"));

        Assert.Null(registry.ActiveProvider);
    }

    [Fact]
    public void ActiveProvider_FallsBackWhenIdIsUnknown()
    {
        var settings = SettingsWith("a");
        settings.ActiveSlicerId = "nonexistent";
        var registry = Build(settings, new FakeSlicerProvider("a"));

        Assert.Equal("a", registry.ActiveProvider.Id);
    }

    [Fact]
    public void ActiveProvider_FallsBackWhenIdPointsAtDisabledProvider()
    {
        var settings = SettingsWith("b");
        settings.Slicers["a"] = new SlicerSettings { IsEnabled = false };
        settings.ActiveSlicerId = "a";
        var registry = Build(settings, new FakeSlicerProvider("a"), new FakeSlicerProvider("b"));

        Assert.Equal("b", registry.ActiveProvider.Id);
    }

    [Fact]
    public void FindProviderForFile_PrefersExactOverProbable()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/x.config", "{}"));
        var registry = Build(
            SettingsWith(),
            new FakeSlicerProvider("probable", SlicerMatch.Probable),
            new FakeSlicerProvider("exact", SlicerMatch.Exact)
        );

        Assert.Equal("exact", registry.FindProviderForFile(path).Id);
    }

    [Fact]
    public void FindProviderForFile_DetectsDisabledProviders()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/x", "y"));
        var registry = Build(new AppSettings(), new FakeSlicerProvider("a", SlicerMatch.Exact));

        Assert.Equal("a", registry.FindProviderForFile(path).Id);
    }

    [Fact]
    public void FindProviderForFile_BreaksProbableTiesToTheActiveProvider()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/x.config", "{}"));
        var settings = SettingsWith("a", "b");
        settings.ActiveSlicerId = "b";
        var registry = Build(
            settings,
            new FakeSlicerProvider("a", SlicerMatch.Probable),
            new FakeSlicerProvider("b", SlicerMatch.Probable)
        );

        Assert.Equal("b", registry.FindProviderForFile(path).Id);
    }

    [Fact]
    public void FindProviderForFile_ReturnsNullWhenNothingMatches()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));
        var registry = Build(SettingsWith("a"), new FakeSlicerProvider("a"));

        Assert.Null(registry.FindProviderForFile(path));
    }

    [Fact]
    public void ApplyFirstRunDefaults_EnablesProvidersWithInstallations()
    {
        var settings = new AppSettings();
        var installed = new FakeSlicerProvider("a");
        installed.Installations.Add(new SlicerInstallation(new Version(1, 0), "A", "p", "d", true));
        var registry = Build(settings, installed, new FakeSlicerProvider("b"));

        var changed = registry.ApplyFirstRunDefaults(settings);

        Assert.True(changed);
        Assert.True(settings.Slicers["a"].IsEnabled);
        Assert.Equal("a", settings.ActiveSlicerId);
        Assert.False(settings.Slicers.ContainsKey("b"));
    }

    [Fact]
    public void ApplyFirstRunDefaults_DoesNothingWhenSlicersAlreadyConfigured()
    {
        var settings = SettingsWith("b");
        var installed = new FakeSlicerProvider("a");
        installed.Installations.Add(new SlicerInstallation(new Version(1, 0), "A", "p", "d", true));
        var registry = Build(settings, installed);

        var changed = registry.ApplyFirstRunDefaults(settings);

        Assert.False(changed);
        Assert.False(settings.Slicers.ContainsKey("a"));
    }
}
