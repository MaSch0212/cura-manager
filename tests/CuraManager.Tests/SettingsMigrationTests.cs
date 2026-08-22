using CuraManager.Services;
using Xunit;

namespace CuraManager.Tests;

public class SettingsMigrationTests
{
    private const string LegacyJson = """
        {
          "PrintsPath": "D:\\Prints",
          "CuraAppDataPath": "C:\\Users\\x\\AppData\\Roaming\\cura\\5.10",
          "CuraProgramFilesPath": "C:\\Program Files\\UltiMaker Cura 5.10.0",
          "UpdateCuraProjectsOnOpen": false,
          "ShowWebDialogWhenAddingLink": true,
          "Theme": "Dark"
        }
        """;

    [Fact]
    public void NoSettingsFile_DisablesLegacyNamingAndStampsVersion()
    {
        var (settings, migrated) = SettingsMigration.Load(null);

        Assert.False(settings.EnableLegacyCuraProjectNaming);
        Assert.Equal(SettingsMigration.CurrentVersion, settings.SettingsVersion);
        Assert.Empty(settings.Slicers);
        Assert.False(migrated);
    }

    [Fact]
    public void LegacyFile_EnablesLegacyNaming()
    {
        var (settings, migrated) = SettingsMigration.Load(LegacyJson);

        Assert.True(settings.EnableLegacyCuraProjectNaming);
        Assert.True(migrated);
    }

    [Fact]
    public void LegacyFile_FoldsCuraPathsIntoSlicerEntry()
    {
        var (settings, _) = SettingsMigration.Load(LegacyJson);

        var cura = settings.Slicers[SettingsMigration.CuraProviderId];
        Assert.True(cura.IsEnabled);
        Assert.Equal("C:\\Program Files\\UltiMaker Cura 5.10.0", cura.ProgramFilesPath);
        Assert.Equal("C:\\Users\\x\\AppData\\Roaming\\cura\\5.10", cura.AppDataPath);
        Assert.False(cura.UpdateProjectsOnOpen);
        Assert.Equal(SettingsMigration.CuraProviderId, settings.ActiveSlicerId);
    }

    [Fact]
    public void LegacyFile_PreservesUnrelatedSettings()
    {
        var (settings, _) = SettingsMigration.Load(LegacyJson);

        Assert.Equal("D:\\Prints", settings.PrintsPath);
        Assert.True(settings.ShowWebDialogWhenAddingLink);
    }

    [Fact]
    public void MigratedFileStoringFalse_IsNotFlippedBackToTrue()
    {
        var migratedJson = """
            {
              "SettingsVersion": 1,
              "EnableLegacyCuraProjectNaming": false,
              "ActiveSlicerId": "cura",
              "Slicers": { "cura": { "IsEnabled": true } }
            }
            """;

        var (settings, migrated) = SettingsMigration.Load(migratedJson);

        Assert.False(settings.EnableLegacyCuraProjectNaming);
        Assert.False(migrated);
    }

    [Fact]
    public void MigrationIsIdempotent()
    {
        var (first, _) = SettingsMigration.Load(LegacyJson);
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(first);

        var (second, migrated) = SettingsMigration.Load(json);

        Assert.False(migrated);
        Assert.True(second.EnableLegacyCuraProjectNaming);
        Assert.Single(second.Slicers);
        Assert.Equal(
            first.Slicers[SettingsMigration.CuraProviderId].ProgramFilesPath,
            second.Slicers[SettingsMigration.CuraProviderId].ProgramFilesPath
        );
    }
}
