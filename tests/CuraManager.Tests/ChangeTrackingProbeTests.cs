using CuraManager.Models;
using Newtonsoft.Json;
using Xunit;

namespace CuraManager.Tests;

/// <summary>
/// Investigation probes for Task 8, Step 1: does <see cref="AppSettings"/>'s change tracking
/// propagate from a nested <see cref="SlicerSettings"/> entry? Both fail without the manual
/// wiring added to <see cref="AppSettings"/>'s constructor, confirming Step 2 was necessary.
/// </summary>
public class ChangeTrackingProbeTests
{
    [Fact]
    public void NestedSlicerSettingsChange_MarksAppSettingsDirty()
    {
        var settings = new AppSettings();
        settings.Slicers["cura"] = new SlicerSettings();
        settings.ResetChangeTracking();

        settings.Slicers["cura"].ProgramFilesPath = "C:\\changed";

        Assert.True(settings.HasChanges);
    }

    /// <summary>
    /// The realistic path: an entry that arrived via Newtonsoft.Json populating the dictionary
    /// (which calls <c>Add</c>, not the indexer) must still be tracked once mutated.
    /// </summary>
    [Fact]
    public void NestedSlicerSettingsChange_MarksAppSettingsDirty_AfterJsonDeserialization()
    {
        var original = new AppSettings();
        original.Slicers["cura"] = new SlicerSettings { ProgramFilesPath = "C:\\original" };
        var json = JsonConvert.SerializeObject(original);

        var settings = JsonConvert.DeserializeObject<AppSettings>(json);
        settings.ResetChangeTracking();

        settings.Slicers["cura"].ProgramFilesPath = "C:\\changed";

        Assert.True(settings.HasChanges);
    }
}
