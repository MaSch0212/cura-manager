# Multi-Slicer Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace CuraManager's hardcoded Cura integration with a slicer-provider abstraction, then add Anycubic Slicer Next as a second provider.

**Architecture:** An `ISlicerProvider` interface with an `ISlicerRegistry` in front of it; `CuraSlicerProvider` implements it directly, and an abstract `OrcaFamilySlicerProvider` holds the Bambu-lineage logic so `AnycubicSlicerProvider` is a thin subclass. Settings move from flat `Cura*` properties to a per-provider dictionary with a one-time migration. The existing Cura UI-automation project naming is quarantined in its own assembly behind an opt-out toggle rather than deleted.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, MaSch.Presentation.Wpf, Newtonsoft.Json, ini-parser-netstandard, xUnit, CSharpier 1.3.0.

**Spec:** [docs/superpowers/specs/2026-08-21-multi-slicer-support-design.md](../specs/2026-08-21-multi-slicer-support-design.md)

## Global Constraints

- **Branch:** `feature/multi-slicer-support`. No pull request; the maintainer opens it.
- **No version bump.** `Version` stays `1.7.3` in `CuraManager.csproj`. 1.8.0 is released from another branch.
- **No breaking changes.** Upgrading users must keep working behaviour.
- **`dotnet build` and `dotnet test` must be green at every commit.** No commit may leave the tree non-compiling.
- **Run `dotnet csharpier format .` before every commit.** CSharpier 1.3.0 formats XAML and `.csproj` as well as C#. CI builds with `TreatWarningsAsErrors=true`, so unformatted files fail the build.
- **Provider display names are never translated.** They are product names: `UltiMaker Cura`, `Anycubic Slicer Next`.
- **Every new project sets `<IsPublishable>false</IsPublishable>`.** CI runs `dotnet publish CuraManager.slnx`.
- **Root namespace stays `CuraManager`; the executable stays `CuraManager.exe`.**
- Anycubic 3mf producer key prefix is exactly `X-ACNext-`. Cura 3mf marker is any zip entry beginning `Cura/`. Bambu-lineage marker is `Metadata/project_settings.config`.
- Anycubic save-directory config key is `app.last_export_path`; Cura's is `local_file.dialog_save_path`.

### Ordering note

This plan reorders the spec's commit list. Settings (Task 2) comes before the provider abstraction because `SlicerSettings` is a plain model with no provider dependency, and the registry (Task 4) is split out from the abstraction (Task 3) because it needs both. Task 2 is deliberately **additive** — it keeps the legacy `Cura*` properties alive so the tree still compiles — and Task 8 deletes them once the last reader is gone.

---

### Task 1: Test project and CI wiring

**Files:**
- Create: `tests/CuraManager.Tests/CuraManager.Tests.csproj`
- Create: `tests/CuraManager.Tests/SmokeTests.cs`
- Modify: `CuraManager.slnx`
- Modify: `.github/workflows/build.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: a runnable `dotnet test` target that every later task adds tests to.

- [ ] **Step 1: Generate the test project from the SDK template**

Using the template avoids hardcoding package versions that may not exist.

```bash
dotnet new xunit -o tests/CuraManager.Tests -n CuraManager.Tests
```

- [ ] **Step 2: Rewrite the generated csproj**

Replace the whole file with this. The template targets a bare TFM and is publishable; both need changing. `UseWPF`/`UseWindowsForms` are required because the referenced `CuraManager` project exposes WPF types. **Keep whatever `PackageReference` versions the template generated** — copy them from the generated file into the block below rather than inventing versions.

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <LangVersion>latest</LangVersion>
        <UseWPF>true</UseWPF>
        <UseWindowsForms>true</UseWindowsForms>
        <Nullable>disable</Nullable>
        <IsPackable>false</IsPackable>
        <IsPublishable>false</IsPublishable>
        <OutputPath>../../bin/$(Configuration)/tests</OutputPath>
        <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
        <CSharpier_UnformattedAsWarnings>true</CSharpier_UnformattedAsWarnings>
        <Configurations>Debug;Release;Debug_MaSchLocal</Configurations>
    </PropertyGroup>

    <ItemGroup>
        <!-- Versions here must match what `dotnet new xunit` generated. -->
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="__FROM_TEMPLATE__" />
        <PackageReference Include="xunit" Version="__FROM_TEMPLATE__" />
        <PackageReference Include="xunit.runner.visualstudio" Version="__FROM_TEMPLATE__" />
        <PackageReference Include="CSharpier.MsBuild" Version="1.3.0">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../../src/CuraManager/CuraManager.csproj" />
    </ItemGroup>
</Project>
```

If the template emitted `xunit.v3` instead of `xunit`, keep `xunit.v3` — the assertion API used throughout this plan (`Assert.Equal`, `Assert.True`, `Assert.Null`) is identical in both.

- [ ] **Step 3: Write the smoke test**

`tests/CuraManager.Tests/SmokeTests.cs`:

```csharp
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
```

This asserts the constructor default in `Models/CuraManagerSettings.cs`, proving the project reference resolves and the MaSch source generator ran.

- [ ] **Step 4: Register the project in the solution**

`CuraManager.slnx` is XML. Add the project element after the existing one, and add a `tests` folder entry. The existing `<Project Path="src/CuraManager/CuraManager.csproj">` element has `BuildType` children mapping `Debug_MaSchLocal` to `Debug`; the test project needs the same mapping or the `Debug_MaSchLocal` configuration will fail to build.

```xml
  <Project Path="tests/CuraManager.Tests/CuraManager.Tests.csproj">
    <BuildType Solution="Debug_MaSchLocal|x64" Project="Debug" />
    <BuildType Solution="Debug_MaSchLocal|x86" Project="Debug" />
  </Project>
```

- [ ] **Step 5: Verify the build and test run**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: build succeeds; 1 test passes.

- [ ] **Step 6: Wire CI**

In `.github/workflows/build.yml`, add `- tests/**` to the `paths:` list under **both** `push:` and `pull_request:` (each currently ends with `- src/**`). Without this, test-only changes do not trigger the workflow at all.

Then add a test step immediately **before** the `Build projects` step:

```yaml
      - name: Run tests
        run: dotnet test CuraManager.slnx -c Release -p:TreatWarningsAsErrors=true
```

It runs before publish so a test failure fails the job fast, and with `TreatWarningsAsErrors` so the test project is held to the same bar as the app.

- [ ] **Step 7: Format and commit**

```bash
dotnet csharpier format .
git add tests CuraManager.slnx .github/workflows/build.yml
git commit -m "test: add xUnit test project and wire it into CI"
```

---

### Task 2: Settings schema and migration

Additive. The legacy `Cura*` properties stay so the tree keeps compiling; Task 8 removes them.

**Files:**
- Create: `src/CuraManager/Models/SlicerSettings.cs`
- Create: `src/CuraManager/Services/SettingsMigration.cs`
- Modify: `src/CuraManager/Models/CuraManagerSettings.cs` → rename file to `AppSettings.cs`
- Modify: `src/CuraManager/Models/CuraManagerGuiSettings.cs` → rename file to `AppGuiSettings.cs`
- Modify: `src/CuraManager/Services/SettingsService.cs`
- Modify: `src/CuraManager/Services/_Interfaces/ISettingsService.cs`
- Modify: `src/CuraManager/ViewModels/Main/SettingsViewModel.cs`, `src/CuraManager/ViewModels/Main/PrintsViewModel.cs`, `src/CuraManager/Services/CuraService.cs`, `src/CuraManager/Services/_Interfaces/ICuraService.cs` (type rename only)
- Test: `tests/CuraManager.Tests/SettingsMigrationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `CuraManager.Models.AppSettings` (was `CuraManagerSettings`), `CuraManager.Models.AppGuiSettings` (was `CuraManagerGuiSettings`)
  - `CuraManager.Models.SlicerSettings` with `bool IsEnabled`, `string ProgramFilesPath`, `string AppDataPath`, `bool UpdateProjectsOnOpen`
  - `AppSettings.SettingsVersion` (int), `.ActiveSlicerId` (string), `.Slicers` (`IDictionary<string, SlicerSettings>`), `.EnableLegacyCuraProjectNaming` (bool)
  - `static SettingsMigration.CurrentVersion` (int, value `1`), `static (AppSettings Settings, bool Migrated) SettingsMigration.Load(string json)`
  - `const string SettingsMigration.CuraProviderId = "cura"`

- [ ] **Step 1: Rename the two settings types**

Pure mechanical rename, no behaviour change. `CuraManagerSettings` → `AppSettings`, `ICuraManagerSettings_Props` → `IAppSettings_Props`, `CuraManagerGuiSettings` → `AppGuiSettings`, `ICuraManagerGuiSettings_Props` → `IAppGuiSettings_Props`. Rename the files to match. Update every reference (`ISettingsService`, `SettingsService`, `SettingsViewModel`, `PrintsViewModel`, `CuraService`, `ICuraService`, `SmokeTests`).

- [ ] **Step 2: Verify the rename compiles**

Run:
```bash
dotnet build CuraManager.slnx
```
Expected: succeeds. If `AppSettings` is reported as not found in generated code, the MaSch generator caches stale output — run `dotnet build --no-incremental`.

- [ ] **Step 3: Create SlicerSettings**

`src/CuraManager/Models/SlicerSettings.cs`:

```csharp
namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface ISlicerSettings_Props
{
    bool IsEnabled { get; set; }
    string ProgramFilesPath { get; set; }
    string AppDataPath { get; set; }

    /// <summary>
    /// Ignored unless the owning provider reports <c>SupportsProfileUpdateOnOpen</c>.
    /// </summary>
    bool UpdateProjectsOnOpen { get; set; }
}

public partial class SlicerSettings : ObservableChangeTrackingObject, ISlicerSettings_Props
{
    public SlicerSettings()
    {
        _updateProjectsOnOpen = true;
    }
}
```

- [ ] **Step 4: Add the new properties to AppSettings**

In `IAppSettings_Props`, add these four alongside the existing members. Leave `CuraAppDataPath`, `CuraProgramFilesPath`, and `UpdateCuraProjectsOnOpen` in place — Task 8 removes them.

```csharp
    int SettingsVersion { get; set; }
    string ActiveSlicerId { get; set; }
    IDictionary<string, SlicerSettings> Slicers { get; set; }

    /// <summary>
    /// Legacy Cura UI-automation project naming. Removed in 2.0.
    /// </summary>
    bool EnableLegacyCuraProjectNaming { get; set; }
```

In the `AppSettings` constructor, add `_slicers = new Dictionary<string, SlicerSettings>();` so the dictionary is never null.

- [ ] **Step 5: Write the failing migration tests**

`tests/CuraManager.Tests/SettingsMigrationTests.cs`:

```csharp
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
```

`MigratedFileStoringFalse_IsNotFlippedBackToTrue` is the important one — it is the failure mode most likely to slip through.

- [ ] **Step 6: Run the tests to verify they fail**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~SettingsMigrationTests
```
Expected: compile error, `SettingsMigration` does not exist.

- [ ] **Step 7: Implement SettingsMigration**

`src/CuraManager/Services/SettingsMigration.cs`:

```csharp
using CuraManager.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CuraManager.Services;

/// <summary>
/// Upgrades persisted settings from the pre-multi-slicer schema.
/// </summary>
public static class SettingsMigration
{
    public const int CurrentVersion = 1;
    public const string CuraProviderId = "cura";

    /// <summary>
    /// Loads settings from raw JSON, migrating if needed.
    /// </summary>
    /// <param name="json">The settings file contents, or <see langword="null"/> if no file exists.</param>
    /// <returns>The settings, and whether a migration was performed and therefore needs saving.</returns>
    public static (AppSettings Settings, bool Migrated) Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            // Fresh install: the new behaviour is the default.
            return (new AppSettings { SettingsVersion = CurrentVersion }, false);
        }

        var raw = JObject.Parse(json);
        if (raw.Value<int?>(nameof(AppSettings.SettingsVersion)) >= CurrentVersion)
            return (raw.ToObject<AppSettings>(), false);

        var settings = raw.ToObject<AppSettings>();
        settings.SettingsVersion = CurrentVersion;

        // An existing file means an upgrading user, who keeps today's behaviour.
        settings.EnableLegacyCuraProjectNaming = true;

        var programFiles = raw.Value<string>("CuraProgramFilesPath");
        var appData = raw.Value<string>("CuraAppDataPath");
        if (!string.IsNullOrEmpty(programFiles) || !string.IsNullOrEmpty(appData))
        {
            settings.Slicers[CuraProviderId] = new SlicerSettings
            {
                IsEnabled = true,
                ProgramFilesPath = programFiles,
                AppDataPath = appData,
                UpdateProjectsOnOpen = raw.Value<bool?>("UpdateCuraProjectsOnOpen") ?? true,
            };
            settings.ActiveSlicerId = CuraProviderId;
        }

        return (settings, true);
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~SettingsMigrationTests
```
Expected: 6 tests pass.

- [ ] **Step 9: Wire SettingsService to the migration**

In `src/CuraManager/Services/SettingsService.cs`, add a backup path constant next to the existing ones:

```csharp
    private static readonly string SettingsBackupFilePath = Path.Combine(
        AppDataPath,
        "settings.json.bak"
    );
```

Replace the body of `LoadSettings()` with:

```csharp
    public AppSettings LoadSettings()
    {
        var json = File.Exists(SettingFilePath) ? File.ReadAllText(SettingFilePath) : null;
        var (result, migrated) = SettingsMigration.Load(json);

        if (migrated)
        {
            // Keep the pre-migration file recoverable, once.
            if (!File.Exists(SettingsBackupFilePath))
                File.Copy(SettingFilePath, SettingsBackupFilePath);
            SaveSettings(result);
        }

        result.ResetChangeTracking();
        return result;
    }
```

`SaveSettings` already calls `ResetChangeTracking`; the trailing call covers the non-migrated path.

- [ ] **Step 10: Verify the full build and test run**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green.

- [ ] **Step 11: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: add per-slicer settings schema with migration from the flat Cura schema"
```

---

### Task 2b: Central package management and NuGet lockfiles

**Inserted mid-run at the user's request**, after Task 2 and before Task 3, so that
the third project (`CuraManager.Legacy.CuraAutomation`, Task 5) is authored under
CPM rather than retrofitted.

Full step-by-step brief:
`.superpowers/sdd/2026-08-21-multi-slicer-support/task-2b-brief.md`

**Files:** creates `Directory.Packages.props` and `Directory.Build.props`; strips
every `Version` attribute from the two csproj files; commits
`src/CuraManager/packages.lock.json` and
`tests/CuraManager.Tests/packages.lock.json`; adds a `dotnet restore --locked-mode`
step to CI and both props files to its `paths:` filters.

**Consequence for every later task:** a new `PackageReference` carries **no**
`Version` attribute. Add a `PackageVersion` entry to `Directory.Packages.props`
instead, and re-run `dotnet restore` so the lockfiles update.

`Debug_MaSchLocal` gets its own gitignored lockfile via `NuGetLockFilePath`, because
that configuration replaces the MaSch package references with project references and
would otherwise rewrite the committed lockfile. Omitting
`RestorePackagesWithLockFile` is not sufficient — NuGet also activates lock mode from
the presence of `packages.lock.json`.

### Task 3: Provider abstraction types

Types only, no registry and no provider implementations. Everything here is pure and testable without a slicer installed.

**Files:**
- Create: `src/CuraManager/Models/SlicerInstallation.cs`
- Create: `src/CuraManager/Models/SlicerLaunchRequest.cs`
- Create: `src/CuraManager/Models/SlicerMatch.cs`
- Create: `src/CuraManager/Services/Slicers/SlicerProjectFileCandidate.cs`
- Create: `src/CuraManager/Services/Slicers/_Interfaces/ISlicerProvider.cs`
- Create: `src/CuraManager/Services/_Interfaces/IFileLockInspector.cs`
- Create: `src/CuraManager/Services/WindowsFileLockInspector.cs`
- Test: `tests/CuraManager.Tests/SlicerProjectFileCandidateTests.cs`
- Test: `tests/CuraManager.Tests/TestZip.cs`

**Interfaces:**
- Consumes: `SlicerSettings` (Task 2).
- Produces:
  - `enum SlicerMatch { None, Probable, Exact }`
  - `record SlicerInstallation(Version Version, string DisplayName, string ProgramFilesPath, string AppDataPath, bool IsSupported)`
  - `record SlicerLaunchRequest(string ProjectDirectory, IReadOnlyList<string> ModelFiles, string ProjectName)`
  - `sealed class SlicerProjectFileCandidate` with `.FilePath`, `.Extension`, `.ZipEntryNames` (`IReadOnlyCollection<string>`), `.ReadEntryText(string)`, and `static SlicerProjectFileCandidate Create(string filePath, IFileLockInspector lockInspector)`; implements `IDisposable`
  - `interface ISlicerProvider` — full signature below
  - `interface IFileLockInspector { IReadOnlyList<string> GetLockingProcessNames(string filePath); }`
  - test helper `TestZip.Create(string path, params (string EntryName, string Content)[] entries)`

- [ ] **Step 1: Create the value types**

`src/CuraManager/Models/SlicerMatch.cs`:

```csharp
namespace CuraManager.Models;

/// <summary>
/// How confidently a provider claims a project file.
/// </summary>
public enum SlicerMatch
{
    /// <summary>Not this provider's file.</summary>
    None = 0,

    /// <summary>The file's family matches, but the producing application could not be identified.</summary>
    Probable = 1,

    /// <summary>The producing application was positively identified.</summary>
    Exact = 2,
}
```

`src/CuraManager/Models/SlicerInstallation.cs`:

```csharp
namespace CuraManager.Models;

public record SlicerInstallation(
    Version Version,
    string DisplayName,
    string ProgramFilesPath,
    string AppDataPath,
    bool IsSupported
);
```

`src/CuraManager/Models/SlicerLaunchRequest.cs`:

```csharp
namespace CuraManager.Models;

/// <summary>
/// A request to open a slicer with a set of models.
/// </summary>
/// <param name="ProjectName">
/// Only set when legacy Cura project naming is active. Every other provider ignores it.
/// Remove this property — not the record — when legacy naming goes away in 2.0.
/// </param>
public record SlicerLaunchRequest(
    string ProjectDirectory,
    IReadOnlyList<string> ModelFiles,
    string ProjectName
);
```

- [ ] **Step 2: Create IFileLockInspector and its Windows implementation**

`src/CuraManager/Services/_Interfaces/IFileLockInspector.cs`:

```csharp
namespace CuraManager.Services;

/// <summary>
/// Reports which processes hold a lock on a file. Windows-only today; a future
/// non-Windows port registers a no-op implementation.
/// </summary>
public interface IFileLockInspector
{
    IReadOnlyList<string> GetLockingProcessNames(string filePath);
}
```

`src/CuraManager/Services/WindowsFileLockInspector.cs`:

```csharp
namespace CuraManager.Services;

public class WindowsFileLockInspector : IFileLockInspector
{
    public IReadOnlyList<string> GetLockingProcessNames(string filePath)
    {
        var processes = Waiter.Retry(
            () => MaSch.Native.Windows.Explorer.FileInfo.WhoIsLocking(filePath),
            new RetryOptions { ThrowException = false }
        );
        return processes?.Select(x => x.ProcessName).ToArray() ?? Array.Empty<string>();
    }
}
```

This is lifted from the `catch` block of `PrintElement.IsCuraProjectFile`.

- [ ] **Step 3: Create SlicerProjectFileCandidate**

`src/CuraManager/Services/Slicers/SlicerProjectFileCandidate.cs`:

```csharp
using System.IO;
using System.IO.Compression;

namespace CuraManager.Services.Slicers;

/// <summary>
/// One file being offered to every provider for identification. The zip is opened
/// once and shared, so provider count does not multiply reads.
/// </summary>
public sealed class SlicerProjectFileCandidate : IDisposable
{
    private static readonly string[] NoEntries = Array.Empty<string>();

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _entryTextCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    private SlicerProjectFileCandidate(
        string filePath,
        ZipArchive archive,
        IReadOnlyCollection<string> entryNames,
        IReadOnlyList<string> lockingProcessNames
    )
    {
        FilePath = filePath;
        Extension = Path.GetExtension(filePath);
        _archive = archive;
        ZipEntryNames = entryNames;
        LockingProcessNames = lockingProcessNames;
    }

    public string FilePath { get; }
    public string Extension { get; }

    /// <summary>Entry names, or empty when the file is not a readable zip.</summary>
    public IReadOnlyCollection<string> ZipEntryNames { get; }

    /// <summary>
    /// Populated only when the archive could not be read. Providers use it as a
    /// last-resort hint that the slicer currently has the file open.
    /// </summary>
    public IReadOnlyList<string> LockingProcessNames { get; }

    public static SlicerProjectFileCandidate Create(
        string filePath,
        IFileLockInspector lockInspector
    )
    {
        if (
            !string.Equals(Path.GetExtension(filePath), ".3mf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(filePath)
        )
        {
            return new SlicerProjectFileCandidate(filePath, null, NoEntries, NoEntries);
        }

        try
        {
            var archive = ZipFile.OpenRead(filePath);
            return new SlicerProjectFileCandidate(
                filePath,
                archive,
                archive.Entries.Select(x => x.FullName).ToArray(),
                NoEntries
            );
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // Typically the slicer itself is holding the file open.
            return new SlicerProjectFileCandidate(
                filePath,
                null,
                NoEntries,
                lockInspector.GetLockingProcessNames(filePath)
            );
        }
    }

    public bool HasEntry(string entryName) =>
        ZipEntryNames.Any(x => string.Equals(x, entryName, StringComparison.OrdinalIgnoreCase));

    public bool HasEntryStartingWith(string prefix) =>
        ZipEntryNames.Any(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads an entry as text, or returns <see langword="null"/> if absent. Memoized.</summary>
    public string ReadEntryText(string entryName)
    {
        if (_archive == null)
            return null;
        if (_entryTextCache.TryGetValue(entryName, out var cached))
            return cached;

        var entry = _archive.GetEntry(entryName);
        string text = null;
        if (entry != null)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }

        _entryTextCache[entryName] = text;
        return text;
    }

    public void Dispose() => _archive?.Dispose();
}
```

- [ ] **Step 4: Create ISlicerProvider**

`src/CuraManager/Services/Slicers/_Interfaces/ISlicerProvider.cs`:

```csharp
using CuraManager.Models;

namespace CuraManager.Services.Slicers;

public interface ISlicerProvider
{
    /// <summary>Stable identifier, used as the settings dictionary key. Never localized.</summary>
    string Id { get; }

    /// <summary>Product name. Never translated.</summary>
    string DisplayName { get; }

    /// <summary>Key of an <c>m:Icon</c> resource in <c>Resources/Geometries.xaml</c>.</summary>
    string IconResourceKey { get; }

    /// <summary>
    /// Whether the settings page offers the "update profiles before open" option.
    /// A UI-rendering hint; the behaviour itself lives in <see cref="OpenProject"/>.
    /// </summary>
    bool SupportsProfileUpdateOnOpen { get; }

    Version LatestSupportedVersion { get; }

    IEnumerable<SlicerInstallation> FindInstallations();
    Version GetVersion(string programFilesPath);
    bool ArePathsValid(SlicerSettings settings);

    SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate);
    void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request);
    void OpenProject(SlicerSettings settings, string projectFilePath);
}
```

- [ ] **Step 5: Write the failing candidate tests**

`tests/CuraManager.Tests/TestZip.cs`:

```csharp
using System.IO;
using System.IO.Compression;

namespace CuraManager.Tests;

internal static class TestZip
{
    /// <summary>Writes a zip to <paramref name="path"/> with the given entries.</summary>
    public static string Create(string path, params (string EntryName, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    /// <summary>A directory that deletes itself at the end of a test.</summary>
    public sealed class Scope : IDisposable
    {
        public Scope()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "curamanager-tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test.
            }
        }
    }
}
```

`tests/CuraManager.Tests/SlicerProjectFileCandidateTests.cs`:

```csharp
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class SlicerProjectFileCandidateTests
{
    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    [Fact]
    public void ListsZipEntryNames()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/plugin.json", "{}"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.True(candidate.HasEntryStartingWith("Cura/"));
        Assert.False(candidate.HasEntryStartingWith("Metadata/"));
    }

    [Fact]
    public void ReadsEntryText()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/slice_info.config", "<config/>"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Equal("<config/>", candidate.ReadEntryText("Metadata/slice_info.config"));
    }

    [Fact]
    public void ReturnsNullForMissingEntry()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/plugin.json", "{}"));

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Null(candidate.ReadEntryText("Metadata/slice_info.config"));
    }

    [Fact]
    public void NonZipExtension_YieldsNoEntries()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("a.stl");
        System.IO.File.WriteAllText(path, "solid");

        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());

        Assert.Empty(candidate.ZipEntryNames);
        Assert.Equal(".stl", candidate.Extension);
    }

    [Fact]
    public void UnreadableZip_FallsBackToLockingProcessNames()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("broken.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        using var candidate = SlicerProjectFileCandidate.Create(path, new StubLocks("Cura"));

        Assert.Empty(candidate.ZipEntryNames);
        Assert.Equal(new[] { "Cura" }, candidate.LockingProcessNames);
    }

    private sealed class StubLocks(params string[] names) : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) => names;
    }
}
```

- [ ] **Step 6: Run the tests**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~SlicerProjectFileCandidateTests
```
Expected: 5 tests pass. (The implementation was written in Steps 1–4, so these pass immediately; they exist to pin the contract the later providers depend on.)

- [ ] **Step 7: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: add slicer provider abstraction types and zip-backed file candidate"
```

---

### Task 4: Slicer registry

**Files:**
- Create: `src/CuraManager/Services/Slicers/_Interfaces/ISlicerRegistry.cs`
- Create: `src/CuraManager/Services/Slicers/SlicerRegistry.cs`
- Test: `tests/CuraManager.Tests/SlicerRegistryTests.cs`
- Test: `tests/CuraManager.Tests/FakeSlicerProvider.cs`

**Interfaces:**
- Consumes: `ISlicerProvider`, `SlicerProjectFileCandidate`, `SlicerMatch` (Task 3); `AppSettings`, `SlicerSettings` (Task 2); `ISettingsService`.
- Produces: `ISlicerRegistry` with `AllProviders`, `EnabledProviders`, `ActiveProvider`, `GetProvider(string)`, `FindProviderForFile(string)`, `ApplyFirstRunDefaults(AppSettings)`, `GetSettings(ISlicerProvider)`.

- [ ] **Step 1: Define ISlicerRegistry**

`src/CuraManager/Services/Slicers/_Interfaces/ISlicerRegistry.cs`:

```csharp
using CuraManager.Models;

namespace CuraManager.Services.Slicers;

public interface ISlicerRegistry
{
    IReadOnlyList<ISlicerProvider> AllProviders { get; }

    /// <summary>Providers enabled in the current settings, in registration order.</summary>
    IReadOnlyList<ISlicerProvider> EnabledProviders { get; }

    /// <summary>
    /// The provider new projects are created with, or <see langword="null"/> when none is enabled.
    /// </summary>
    ISlicerProvider ActiveProvider { get; }

    ISlicerProvider GetProvider(string id);

    /// <summary>Settings for a provider, created on demand so callers never see null.</summary>
    SlicerSettings GetSettings(ISlicerProvider provider);

    /// <summary>
    /// Identifies the provider that produced a file, or <see langword="null"/>.
    /// Runs for every registered provider regardless of enabled state.
    /// </summary>
    ISlicerProvider FindProviderForFile(string filePath);

    /// <summary>
    /// On a fresh install, enables every provider that has a detectable installation.
    /// Returns whether anything changed and therefore needs saving.
    /// </summary>
    bool ApplyFirstRunDefaults(AppSettings settings);
}
```

- [ ] **Step 2: Write the failing registry tests**

`tests/CuraManager.Tests/FakeSlicerProvider.cs`:

```csharp
using CuraManager.Models;
using CuraManager.Services.Slicers;

namespace CuraManager.Tests;

internal sealed class FakeSlicerProvider : ISlicerProvider
{
    public FakeSlicerProvider(string id, SlicerMatch match = SlicerMatch.None)
    {
        Id = id;
        Match = match;
    }

    public string Id { get; }
    public string DisplayName => Id;
    public string IconResourceKey => "CuraIcon";
    public bool SupportsProfileUpdateOnOpen => false;
    public Version LatestSupportedVersion => new(1, 0);

    public SlicerMatch Match { get; set; }
    public IList<SlicerInstallation> Installations { get; } = new List<SlicerInstallation>();

    public IEnumerable<SlicerInstallation> FindInstallations() => Installations;

    public Version GetVersion(string programFilesPath) => new(1, 0);

    public bool ArePathsValid(SlicerSettings settings) => true;

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate) => Match;

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request) { }

    public void OpenProject(SlicerSettings settings, string projectFilePath) { }
}
```

`tests/CuraManager.Tests/SlicerRegistryTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~SlicerRegistryTests
```
Expected: compile error, `SlicerRegistry` does not exist.

- [ ] **Step 4: Implement SlicerRegistry**

The settings accessor is a `Func<AppSettings>` so tests can supply a fixed instance while production reads `ISettingsService` on each call, matching how `CuraService` calls `LoadSettings()` per operation today.

`src/CuraManager/Services/Slicers/SlicerRegistry.cs`:

```csharp
using CuraManager.Models;

namespace CuraManager.Services.Slicers;

public class SlicerRegistry : ISlicerRegistry
{
    private readonly IFileLockInspector _lockInspector;
    private readonly Func<AppSettings> _settingsAccessor;

    public SlicerRegistry(
        IEnumerable<ISlicerProvider> providers,
        IFileLockInspector lockInspector,
        Func<AppSettings> settingsAccessor
    )
    {
        AllProviders = providers.ToArray();
        _lockInspector = lockInspector;
        _settingsAccessor = settingsAccessor;
    }

    public SlicerRegistry(
        IEnumerable<ISlicerProvider> providers,
        IFileLockInspector lockInspector,
        ISettingsService settingsService
    )
        : this(providers, lockInspector, settingsService.LoadSettings) { }

    public IReadOnlyList<ISlicerProvider> AllProviders { get; }

    public IReadOnlyList<ISlicerProvider> EnabledProviders => GetEnabled(_settingsAccessor());

    public ISlicerProvider ActiveProvider => GetActive(_settingsAccessor());

    public ISlicerProvider GetProvider(string id) =>
        AllProviders.FirstOrDefault(x =>
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
        );

    public SlicerSettings GetSettings(ISlicerProvider provider) =>
        GetSettings(_settingsAccessor(), provider);

    public ISlicerProvider FindProviderForFile(string filePath)
    {
        using var candidate = SlicerProjectFileCandidate.Create(filePath, _lockInspector);

        ISlicerProvider best = null;
        var bestMatch = SlicerMatch.None;
        var active = GetActive(_settingsAccessor());

        foreach (var provider in AllProviders)
        {
            var match = provider.IsProjectFile(candidate);
            if (match == SlicerMatch.None)
                continue;

            if (match > bestMatch)
            {
                best = provider;
                bestMatch = match;
            }
            else if (match == bestMatch && ReferenceEquals(provider, active))
            {
                // Equal confidence: the slicer the user is working in wins.
                best = provider;
            }
        }

        return best;
    }

    public bool ApplyFirstRunDefaults(AppSettings settings)
    {
        if (settings.Slicers.Count > 0)
            return false;

        var changed = false;
        foreach (var provider in AllProviders)
        {
            var installation = provider.FindInstallations().FirstOrDefault();
            if (installation == null)
                continue;

            settings.Slicers[provider.Id] = new SlicerSettings
            {
                IsEnabled = true,
                ProgramFilesPath = installation.ProgramFilesPath,
                AppDataPath = installation.AppDataPath,
            };
            settings.ActiveSlicerId ??= provider.Id;
            changed = true;
        }

        return changed;
    }

    private static SlicerSettings GetSettings(AppSettings settings, ISlicerProvider provider)
    {
        if (!settings.Slicers.TryGetValue(provider.Id, out var slicerSettings))
        {
            slicerSettings = new SlicerSettings();
            settings.Slicers[provider.Id] = slicerSettings;
        }

        return slicerSettings;
    }

    private IReadOnlyList<ISlicerProvider> GetEnabled(AppSettings settings) =>
        AllProviders
            .Where(x => settings.Slicers.TryGetValue(x.Id, out var s) && s.IsEnabled)
            .ToArray();

    private ISlicerProvider GetActive(AppSettings settings)
    {
        var enabled = GetEnabled(settings);
        if (enabled.Count == 0)
            return null;

        return enabled.FirstOrDefault(x =>
                string.Equals(x.Id, settings.ActiveSlicerId, StringComparison.OrdinalIgnoreCase)
            ) ?? enabled[0];
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~SlicerRegistryTests
```
Expected: 10 tests pass.

- [ ] **Step 6: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: add slicer registry with confidence-based file detection"
```

---

### Task 5: Extract the legacy Cura UI automation

A pure move. No behaviour change, no toggle yet — that is Task 6. Keeping them separate means this diff is provably behaviour-free.

**Files:**
- Create: `src/CuraManager.Legacy.CuraAutomation/CuraManager.Legacy.CuraAutomation.csproj`
- Create: `src/CuraManager.Legacy.CuraAutomation/ICuraProjectNameAutomation.cs`
- Create: `src/CuraManager.Legacy.CuraAutomation/CuraProjectNameAutomation.cs`
- Modify: `src/CuraManager/Services/CuraService.cs` (remove the automation, call the new service)
- Modify: `src/CuraManager/CuraManager.csproj` (project reference)
- Modify: `src/CuraManager/App.xaml.cs` (register the service)
- Modify: `CuraManager.slnx`

**Interfaces:**
- Consumes: nothing.
- Produces: `CuraManager.Legacy.CuraAutomation.ICuraProjectNameAutomation` with `void SetProjectName(Process curaProcess, string executableFileName, string projectName)`, and its implementation `CuraProjectNameAutomation`.

- [ ] **Step 1: Create the project file**

`src/CuraManager.Legacy.CuraAutomation/CuraManager.Legacy.CuraAutomation.csproj`. `UseWPF` brings in the UIAutomation assemblies; `UseWindowsForms` brings in `SendKeys`. It deliberately takes **no** MaSch dependency so that deleting it in 2.0 is trivial.

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <!--
            Legacy Cura UI-automation project naming. Removed in 2.0.
            Kept in its own assembly so removal is deleting a directory and a
            ProjectReference, and so a non-Windows port can simply drop it.
        -->
        <TargetFramework>net10.0-windows</TargetFramework>
        <LangVersion>latest</LangVersion>
        <UseWPF>true</UseWPF>
        <UseWindowsForms>true</UseWindowsForms>
        <Nullable>disable</Nullable>
        <IsPublishable>false</IsPublishable>
        <OutputPath>../../bin/$(Configuration)</OutputPath>
        <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
        <GenerateDocumentationFile>true</GenerateDocumentationFile>
        <NoWarn>1591</NoWarn>
        <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
        <CSharpier_UnformattedAsWarnings>true</CSharpier_UnformattedAsWarnings>
        <SupportedOSPlatform>windows</SupportedOSPlatform>
        <Configurations>Debug;Release;Debug_MaSchLocal</Configurations>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="CSharpier.MsBuild" Version="1.3.0">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
    </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the interface**

`src/CuraManager.Legacy.CuraAutomation/ICuraProjectNameAutomation.cs`:

```csharp
using System.Diagnostics;

namespace CuraManager.Legacy.CuraAutomation;

/// <summary>
/// Drives the Cura window through UI automation to set a project name, because Cura
/// exposes no command-line argument for it.
/// </summary>
/// <remarks>
/// Legacy. Windows-only, and liable to break whenever Cura changes its UI tree.
/// Scheduled for removal in 2.0.
/// </remarks>
public interface ICuraProjectNameAutomation
{
    /// <param name="curaProcess">A started Cura process that has reached input idle.</param>
    /// <param name="executableFileName">
    /// File name without extension. <c>Cura</c> selects the 4.x strategy; anything else selects 5.x.
    /// </param>
    /// <param name="projectName">The name to type into Cura.</param>
    void SetProjectName(Process curaProcess, string executableFileName, string projectName);
}
```

- [ ] **Step 3: Move the automation implementation**

`src/CuraManager.Legacy.CuraAutomation/CuraProjectNameAutomation.cs`. Move `SetName4x`, `SetName5x`, and `TryFindChild` from `src/CuraManager/Services/CuraService.cs` **verbatim** apart from the changes noted below.

```csharp
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CuraManager.Legacy.CuraAutomation;

public class CuraProjectNameAutomation : ICuraProjectNameAutomation
{
    public void SetProjectName(Process curaProcess, string executableFileName, string projectName)
    {
        if (executableFileName == "Cura")
            SetName4x(curaProcess, projectName);
        else
            SetName5x(curaProcess, projectName);
    }

    // Body moved verbatim from CuraService.OpenCura's local function SetName4x.
    // `p` becomes the `process` parameter and `printName` becomes `projectName`.
    private static void SetName4x(Process process, string projectName) { /* ... */ }

    // Body moved verbatim from CuraService.OpenCura's local function SetName5x.
    private static void SetName5x(Process process, string projectName) { /* ... */ }

    // Moved verbatim from CuraService.TryFindChild, except that MaSch's Waiter.WaitUntil
    // is replaced by the local Poll helper below to keep this assembly dependency-free.
    private static bool TryFindChild(
        AutomationElement parent,
        Func<TreeWalker, AutomationElement, bool> checkFunc,
        TimeSpan timeout,
        out AutomationElement element
    )
    {
        var treeWalker = TreeWalker.RawViewWalker;
        element = Poll(
            () =>
            {
                var e = treeWalker.GetFirstChild(parent);
                while (e != null && !checkFunc(treeWalker, e))
                    e = treeWalker.GetNextSibling(e);
                return e;
            },
            timeout
        );
        return element != null;
    }

    /// <summary>
    /// Replaces MaSch <c>Waiter.WaitUntil</c> with <c>ThrowException = false</c>: polls until
    /// the factory returns non-null or the timeout elapses, then returns null.
    /// </summary>
    private static AutomationElement Poll(Func<AutomationElement> factory, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var element = factory();
            if (element != null)
                return element;
            Thread.Sleep(250);
        } while (stopwatch.Elapsed < timeout);

        return null;
    }
}
```

Fill the two `/* ... */` bodies with the moved code. In `SetName4x` the `SendKeys.SendWait($"{printName}{{ENTER}}")` call becomes `SendKeys.SendWait($"{projectName}{{ENTER}}")`; in `SetName5x` `valuePattern.SetValue(printName)` becomes `valuePattern.SetValue(projectName)`. Both nested `CheckWindow` functions reference `p.Id` — change to `process.Id`.

- [ ] **Step 4: Reference the project and register the service**

In `src/CuraManager/CuraManager.csproj`, add to the last unconditional `ItemGroup` containing package references:

```xml
    <ItemGroup>
        <ProjectReference Include="../CuraManager.Legacy.CuraAutomation/CuraManager.Legacy.CuraAutomation.csproj" />
    </ItemGroup>
```

In `src/CuraManager/App.xaml.cs`, inside `InitializeServices()`, next to the other `ServiceContext.AddService` calls:

```csharp
        ServiceContext.AddService<ICuraProjectNameAutomation>(new CuraProjectNameAutomation());
```

Add `using CuraManager.Legacy.CuraAutomation;` to the file's usings.

- [ ] **Step 5: Call it from CuraService**

In `src/CuraManager/Services/CuraService.cs`, delete the `SetName4x`, `SetName5x`, and `TryFindChild` members and the now-unused `using System.Windows.Automation;` and `using System.Windows.Forms;`. Replace the naming block at the end of `OpenCura` — currently:

```csharp
        if (curaFileName == "Cura")
            SetName4x();
        else
            SetName5x();
```

with:

```csharp
        ServiceContext
            .GetService<ICuraProjectNameAutomation>()
            .SetProjectName(p, curaFileName, printName);
```

Add `using CuraManager.Legacy.CuraAutomation;`. Keep `p.WaitForInputIdle();` where it is.

- [ ] **Step 6: Add the project to the solution**

In `CuraManager.slnx`, add alongside the existing project entries:

```xml
  <Project Path="src/CuraManager.Legacy.CuraAutomation/CuraManager.Legacy.CuraAutomation.csproj">
    <BuildType Solution="Debug_MaSchLocal|x64" Project="Debug" />
    <BuildType Solution="Debug_MaSchLocal|x86" Project="Debug" />
  </Project>
```

- [ ] **Step 7: Verify**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green. This step has no new tests — UI automation is not unit-testable, which is part of why it is being quarantined.

- [ ] **Step 8: Format and commit**

```bash
dotnet csharpier format .
git add -A src CuraManager.slnx
git commit -m "refactor: move Cura UI-automation naming into a separate legacy assembly"
```

---

### Task 6: CuraSlicerProvider

**Files:**
- Create: `src/CuraManager/Services/Slicers/CuraSlicerProvider.cs`
- Delete: `src/CuraManager/Services/CuraService.cs`, `src/CuraManager/Services/_Interfaces/ICuraService.cs`, `src/CuraManager/Models/CuraVersion.cs`
- Modify: `src/CuraManager/App.xaml.cs`, `src/CuraManager/ViewModels/Main/PrintsViewModel.cs`, `src/CuraManager/ViewModels/Main/SettingsViewModel.cs`
- Test: `tests/CuraManager.Tests/CuraSlicerProviderTests.cs`

**Interfaces:**
- Consumes: `ISlicerProvider`, `SlicerProjectFileCandidate`, `SlicerMatch`, `SlicerInstallation`, `SlicerLaunchRequest` (Task 3); `SlicerSettings` (Task 2); `ICuraProjectNameAutomation` (Task 5).
- Produces: `CuraManager.Services.Slicers.CuraSlicerProvider` with `Id == "cura"`, `DisplayName == "UltiMaker Cura"`, `IconResourceKey == "CuraIcon"`, `SupportsProfileUpdateOnOpen == true`, `LatestSupportedVersion == new Version(5, 10, 0, 0)`, and a constructor taking `(ICuraProjectNameAutomation automation, Func<bool> isLegacyNamingEnabled)`.

- [ ] **Step 1: Write the failing detection tests**

`tests/CuraManager.Tests/CuraSlicerProviderTests.cs`:

```csharp
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class CuraSlicerProviderTests
{
    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private sealed class StubLocks(params string[] names) : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) => names;
    }

    private static CuraSlicerProvider CreateProvider() => new(automation: null, () => false);

    private static SlicerMatch Match(string path, IFileLockInspector locks = null)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, locks ?? new NoLocks());
        return CreateProvider().IsProjectFile(candidate);
    }

    [Fact]
    public void CuraFolderEntry_IsExactMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("3D/3dmodel.model", "<model/>"),
            ("Cura/preferences.cfg", "[general]")
        );

        Assert.Equal(SlicerMatch.Exact, Match(path));
    }

    [Fact]
    public void PlainModel3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void OrcaFamily3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/project_settings.config", "{}"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void NonThreeMfFile_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("a.stl");
        System.IO.File.WriteAllText(path, "solid");

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void UnreadableFileLockedByCura_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("locked.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        Assert.Equal(SlicerMatch.Probable, Match(path, new StubLocks("Cura")));
    }

    [Fact]
    public void UnreadableFileLockedBySomethingElse_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = scope.File("locked.3mf");
        System.IO.File.WriteAllText(path, "not a zip");

        Assert.Equal(SlicerMatch.None, Match(path, new StubLocks("notepad")));
    }
}
```

Note the deliberate behaviour change recorded here: the old `IsCuraProjectFile` returned a hard `true` for a Cura-locked unreadable file. It is now `Probable`, because a lock is a weaker signal than a marker and must lose to another provider's `Exact`.

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~CuraSlicerProviderTests
```
Expected: compile error, `CuraSlicerProvider` does not exist.

- [ ] **Step 3: Write CuraSlicerProvider**

Create `src/CuraManager/Services/Slicers/CuraSlicerProvider.cs`. Move these members from `CuraService` unchanged except for renames: `ProgramFilesDir`, `AppDataDir`, `GetCuraVersion` → `GetVersion`, `FindAvailableCuraVersions` → `FindInstallations` (returning `SlicerInstallation` instead of `CuraVersion`), `CheckCuraAppDataPath`, `CheckCuraProgramFilesPath`, `GetCuraExecutableFilePath`, `SetCuraSaveDialogPath` → `SetSaveDialogPath`, and `UpdateCuraProjectConfigs`. `AreCuraPathsCorrect` becomes `ArePathsValid(SlicerSettings)` reading `settings.AppDataPath` / `settings.ProgramFilesPath`.

The members that are genuinely new:

```csharp
    private static readonly string[] CuraProcessNames = ["Cura", "UltiMaker-Cura"];

    private readonly ICuraProjectNameAutomation _automation;
    private readonly Func<bool> _isLegacyNamingEnabled;

    public CuraSlicerProvider(
        ICuraProjectNameAutomation automation,
        Func<bool> isLegacyNamingEnabled
    )
    {
        _automation = automation;
        _isLegacyNamingEnabled = isLegacyNamingEnabled;
    }

    public const string ProviderId = "cura";

    public string Id => ProviderId;
    public string DisplayName => "UltiMaker Cura";
    public string IconResourceKey => "CuraIcon";
    public bool SupportsProfileUpdateOnOpen => true;
    public Version LatestSupportedVersion { get; } = new Version(5, 10, 0, 0);

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate)
    {
        if (!string.Equals(candidate.Extension, ".3mf", StringComparison.OrdinalIgnoreCase))
            return SlicerMatch.None;

        if (candidate.HasEntryStartingWith("Cura/"))
            return SlicerMatch.Exact;

        // Unreadable because Cura itself has it open: weaker than a marker, so Probable.
        if (candidate.LockingProcessNames.Any(x => CuraProcessNames.Contains(x)))
            return SlicerMatch.Probable;

        return SlicerMatch.None;
    }

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request)
    {
        SetSaveDialogPath(request.ProjectDirectory, settings);

        var curaPath =
            GetCuraExecutableFilePath(settings.ProgramFilesPath)
            ?? throw new FileNotFoundException("Could not find cura executable.");

        var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = curaPath,
                Arguments = $"\"{string.Join("\" \"", request.ModelFiles)}\"",
            }
        );

        if (string.IsNullOrEmpty(request.ProjectName) || !_isLegacyNamingEnabled())
            return;

        process.WaitForInputIdle();
        _automation.SetProjectName(
            process,
            Path.GetFileNameWithoutExtension(curaPath),
            request.ProjectName
        );
    }

    public void OpenProject(SlicerSettings settings, string projectFilePath)
    {
        SetSaveDialogPath(Path.GetDirectoryName(projectFilePath), settings);

        if (settings.UpdateProjectsOnOpen)
            UpdateCuraProjectConfigs(projectFilePath, settings);

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    GetCuraExecutableFilePath(settings.ProgramFilesPath)
                    ?? throw new FileNotFoundException("Could not find cura executable."),
                Arguments = $"\"{projectFilePath}\"",
            }
        );
    }
```

`FindInstallations` returns `SlicerInstallation` with `IsSupported: curaVersion <= LatestSupportedVersion`. `SetSaveDialogPath` and `UpdateCuraProjectConfigs` take `SlicerSettings` and read `AppDataPath` / `ProgramFilesPath` instead of `CuraAppDataPath` / `CuraProgramFilesPath`.

`CreateCuraProject` does **not** move here — showing a dialog is the view model's job. Task 9 rehomes it.

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~CuraSlicerProviderTests
```
Expected: 6 tests pass.

- [ ] **Step 4a: Expose internals to the test project**

Several things worth testing are `internal` by design. Add to `src/CuraManager/Properties/AssemblyInfo.cs`:

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CuraManager.Tests")]
```

- [ ] **Step 4b: Write the cura.cfg round-trip test**

Clobbering a user's Cura config is the worst failure mode in this change, so it gets a dedicated test. Change `SetSaveDialogPath` to `internal static void SetSaveDialogPath(string curaConfigPath, string targetPath)` — taking the config path directly rather than `SlicerSettings` — and have the instance callers pass `Path.Combine(settings.AppDataPath, "cura.cfg")`.

Add to `tests/CuraManager.Tests/CuraSlicerProviderTests.cs`:

```csharp
    [Fact]
    public void SetSaveDialogPath_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("cura.cfg");
        System.IO.File.WriteAllText(
            configPath,
            """
            [general]
            visible_settings = layer_height;infill_sparse_density
            window_maximized = True

            [local_file]
            dialog_save_path = C:\old

            [cura]
            categories_expanded = material
            """
        );

        CuraSlicerProvider.SetSaveDialogPath(configPath, "D:\\Prints\\Widget");

        var result = System.IO.File.ReadAllText(configPath);
        Assert.Contains("visible_settings = layer_height;infill_sparse_density", result);
        Assert.Contains("window_maximized = True", result);
        Assert.Contains("categories_expanded = material", result);
        Assert.Contains("D:\\Prints\\Widget", result);
        Assert.DoesNotContain("C:\\old", result);
    }
```

Note the existing implementation converts the target through `new Uri(targetPath).PathAndQuery`, so the stored value is `/D:/Prints/Widget`-shaped. Run the test, read the actual value from the failure message, and assert on that exact string rather than changing the conversion — this test pins existing behaviour, it does not redefine it.

- [ ] **Step 4c: Run the round-trip test**

Run:
```bash
dotnet test CuraManager.slnx --filter SetSaveDialogPath_PreservesEveryUnrelatedKey
```
Expected: PASS.

- [ ] **Step 5: Register the provider and registry**

In `src/CuraManager/App.xaml.cs`, replace `ServiceContext.AddService<ICuraService>(new CuraService());` with:

```csharp
        var fileLockInspector = new WindowsFileLockInspector();
        ServiceContext.AddService<IFileLockInspector>(fileLockInspector);

        var slicerRegistry = new SlicerRegistry(
            new ISlicerProvider[]
            {
                new CuraSlicerProvider(
                    new CuraProjectNameAutomation(),
                    () => settingsService.LoadSettings().EnableLegacyCuraProjectNaming
                ),
            },
            fileLockInspector,
            settingsService
        );
        ServiceContext.AddService<ISlicerRegistry>(slicerRegistry);
```

Remove the separate `ICuraProjectNameAutomation` registration added in Task 5 Step 4 — the provider now owns the instance. Then apply first-run defaults in `App_OnStartup`, after the settings are first loaded:

```csharp
        var settings = settingsService.LoadSettings();
        if (slicerRegistry.ApplyFirstRunDefaults(settings))
            settingsService.SaveSettings(settings);
```

- [ ] **Step 6: Update the view models and delete the dead files**

In `PrintsViewModel` and `SettingsViewModel`, replace the `ICuraService _curaService` field and its `ServiceContext.GetService` call with `ISlicerRegistry _slicerRegistry`. Then:

- `PrintsViewModel.ExecuteNewCuraProject` → `_slicerRegistry.ActiveProvider`; the full rework is Task 9, so for now guard with `if (_slicerRegistry.ActiveProvider == null) return;` and call `LaunchWithModels` with `ProjectName: project.Name`.
- `PrintsViewModel.ExecuteOpenProjectFile` → `_slicerRegistry.FindProviderForFile(file.FilePath)`; if non-null call `provider.OpenProject(_slicerRegistry.GetSettings(provider), file.FilePath)`, else `ShellExecute` as today.
- `SettingsViewModel` → `_slicerRegistry.GetProvider("cura")` for `LatestSupportedVersion`, `GetVersion`, and `FindInstallations`. Task 8 replaces this wholesale; here just keep it compiling.

Delete `Services/CuraService.cs`, `Services/_Interfaces/ICuraService.cs`, and `Models/CuraVersion.cs`. Replace `CuraVersion` with `SlicerInstallation` in `SettingsViewModel` and `SettingsView.xaml` (the `DataType="{x:Type models:CuraVersion}"` attribute).

Collapse the duplicated id constant: change `SettingsMigration.CuraProviderId` to `public const string CuraProviderId = Slicers.CuraSlicerProvider.ProviderId;` so `"cura"` is spelled once in the codebase.

- [ ] **Step 7: Verify the whole build and test run**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green.

- [ ] **Step 8: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: replace CuraService with CuraSlicerProvider behind the registry"
```

---

### Task 7: Per-slicer file grouping in PrintElement

**Files:**
- Create: `src/CuraManager/Models/SlicerFileGroup.cs`
- Modify: `src/CuraManager/Models/PrintElement.cs`
- Modify: `src/CuraManager/Views/Main/PrintsView.xaml:455-485`
- Modify: `src/CuraManager/Views/Main/PrintsView.xaml.cs:74`
- Test: `tests/CuraManager.Tests/PrintElementGroupingTests.cs`

**Interfaces:**
- Consumes: `ISlicerRegistry` (Task 4), `ISlicerProvider` (Task 3).
- Produces: `SlicerFileGroup` with `ISlicerProvider Provider`, `string Header`, `ObservableCollection<PrintElementFile> Files`; `PrintElement.SlicerProjectFiles` (`ObservableCollection<SlicerFileGroup>`).

- [ ] **Step 1: Create SlicerFileGroup**

`src/CuraManager/Models/SlicerFileGroup.cs`:

```csharp
using System.Collections.ObjectModel;
using CuraManager.Resources;
using CuraManager.Services.Slicers;
using MaSch.Presentation.Translation;

namespace CuraManager.Models;

/// <summary>
/// The project files in one print element that belong to a single slicer.
/// </summary>
public class SlicerFileGroup
{
    public SlicerFileGroup(ISlicerProvider provider)
    {
        Provider = provider;
        Files = new ObservableCollection<PrintElementFile>();
        Header = string.Format(
            ServiceContext
                .GetService<ITranslationManager>()
                .GetTranslation(nameof(StringTable.SlicerProjectFiles)),
            provider.DisplayName
        );
    }

    public ISlicerProvider Provider { get; }
    public string Header { get; }
    public ObservableCollection<PrintElementFile> Files { get; }
}
```

`StringTable.SlicerProjectFiles` is added in Task 9. To keep this task's commit building, add the resx entry now — in `StringTable.resx` add `<data name="SlicerProjectFiles" xml:space="preserve"><value>{0} Project Files</value></data>` and in `StringTable.de.resx` `<value>{0}-Projektdateien</value>`. Task 9 removes the old `CuraProjectFiles` key.

- [ ] **Step 2: Write the failing grouping tests**

`tests/CuraManager.Tests/PrintElementGroupingTests.cs`:

```csharp
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
    }
}
```

`PrintElement` itself needs a real directory and a `FileSystemWatcher` to construct, so the categorisation decision is extracted into a static pure method that can be tested directly. Everything else in `PrintElement` stays integration-tested by running the app.

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~PrintElementGroupingTests
```
Expected: compile error, `CategorizeByExtension` and `PrintElementFileCategory` do not exist.

- [ ] **Step 4: Rework PrintElement**

In `src/CuraManager/Models/PrintElement.cs`:

Add the enum at namespace scope:

```csharp
public enum PrintElementFileCategory
{
    Model,
    MaybeSlicerProject,
    Other,
}
```

Add the pure classifier:

```csharp
    internal static PrintElementFileCategory CategorizeByExtension(string extension)
    {
        if (IsExt(".stl", ".obj", ".x3d"))
            return PrintElementFileCategory.Model;
        if (IsExt(".3mf"))
            return PrintElementFileCategory.MaybeSlicerProject;
        return PrintElementFileCategory.Other;

        bool IsExt(params string[] e) =>
            e.Any(x => string.Equals(extension, x, StringComparison.OrdinalIgnoreCase));
    }
```

In `IPrintElement_Props`, replace `IList<PrintElementFile> CuraProjectFiles { get; set; }` with `IList<SlicerFileGroup> SlicerProjectFiles { get; set; }`, and initialise it to `new ObservableCollection<SlicerFileGroup>()` in the constructor.

Replace `AllFiles`:

```csharp
    public IEnumerable<PrintElementFile> AllFiles =>
        SlicerProjectFiles.SelectMany(x => x.Files).Concat(ModelFiles).Concat(OtherFiles);
```

Replace `GetCorrectListForFile` with a version that consults the registry:

```csharp
    private IList<PrintElementFile> GetCorrectListForFile(string filePath)
    {
        if (
            string.Equals(
                Path.GetFileName(filePath),
                "metadata.json",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return null;

        switch (CategorizeByExtension(Path.GetExtension(filePath)))
        {
            case PrintElementFileCategory.Model:
                return ModelFiles;
            case PrintElementFileCategory.MaybeSlicerProject:
                var provider = ServiceContext
                    .GetService<ISlicerRegistry>()
                    .FindProviderForFile(filePath);
                return provider == null ? ModelFiles : GetOrCreateGroup(provider);
            default:
                return OtherFiles;
        }
    }

    private IList<PrintElementFile> GetOrCreateGroup(ISlicerProvider provider)
    {
        var group = SlicerProjectFiles.FirstOrDefault(x => x.Provider.Id == provider.Id);
        if (group == null)
        {
            group = new SlicerFileGroup(provider);
            SlicerProjectFiles.Add(group);
        }

        return group.Files;
    }
```

In `FillInformation`, replace `CuraProjectFiles.Clear();` with `SlicerProjectFiles.Clear();`. In `GetFile`, replace the `CuraProjectFiles.TryFirst` branch with a scan over the groups:

```csharp
        foreach (var group in SlicerProjectFiles)
        {
            if (group.Files.TryFirst(Predicate, out result))
            {
                list = group.Files;
                return result;
            }
        }
```

In `OnFileDeleted`, after removing the file, drop the group if it is now empty:

```csharp
        var emptyGroup = SlicerProjectFiles.FirstOrDefault(x => x.Files.Count == 0);
        if (emptyGroup != null)
            SlicerProjectFiles.Remove(emptyGroup);
```

Delete the `IsCuraProjectFile` method entirely. Add `using CuraManager.Services.Slicers;`.

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~PrintElementGroupingTests
```
Expected: 4 tests pass.

- [ ] **Step 6: Update the view**

In `src/CuraManager/Views/Main/PrintsView.xaml`, delete the `<CollectionViewSource x:Key="CuraProjectFiles" ...>` resource and replace the `CuraProjectFiles` `GroupBox` with an `ItemsControl` over the groups. Sorting moves onto the group itself, so the `CollectionViewSource` is no longer needed:

```xml
<ItemsControl ItemsSource="{Binding SelectedElement.SlicerProjectFiles}">
  <ItemsControl.ItemTemplate>
    <DataTemplate DataType="{x:Type models:SlicerFileGroup}">
      <GroupBox Header="{Binding Header}">
        <ItemsControl
          ItemTemplate="{StaticResource PrintElementFileTemplate}"
          ItemsSource="{Binding Files}"
        />
      </GroupBox>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

Place it where the old `GroupBox` was — above the `ModelFiles` group. `PrintsView.xaml.cs:74` uses `viewModel.SelectedElement.AllFiles`, which still compiles unchanged.

- [ ] **Step 7: Verify**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green.

- [ ] **Step 8: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: group project files per slicer instead of hardcoding a Cura section"
```

---

### Task 8: Settings page

**Files:**
- Create: `src/CuraManager/ViewModels/Main/SlicerSettingsViewModel.cs`
- Modify: `src/CuraManager/ViewModels/Main/SettingsViewModel.cs`
- Modify: `src/CuraManager/Views/Main/SettingsView.xaml`
- Modify: `src/CuraManager/Models/AppSettings.cs` (delete the legacy `Cura*` properties)

**Interfaces:**
- Consumes: `ISlicerRegistry`, `ISlicerProvider`, `SlicerSettings`, `SlicerInstallation`.
- Produces: `SlicerSettingsViewModel` with `Provider`, `Settings`, `AvailableInstallations` (`SlicerInstallation[]`), `SelectedInstallation`, `DetectedVersion` (`Version`), `IsSupportedVersionSelected` (`bool?`), `IsLoadingVersions`, `ReloadCommand`, `BrowseAppDataCommand`, `BrowseInstallCommand`; `SettingsViewModel.Slicers` (`SlicerSettingsViewModel[]`).

- [ ] **Step 1: Determine whether change tracking propagates**

The spec flags this as unverified and it decides Step 2. Write a temporary test:

```csharp
[Fact]
public void NestedSlicerSettingsChange_MarksAppSettingsDirty()
{
    var settings = new AppSettings();
    settings.Slicers["cura"] = new SlicerSettings();
    settings.ResetChangeTracking();

    settings.Slicers["cura"].ProgramFilesPath = "C:\\changed";

    Assert.True(settings.HasChanges);
}
```

Run:
```bash
dotnet test CuraManager.slnx --filter NestedSlicerSettingsChange
```

**If it passes**, delete the test — the framework handles it — and skip Step 2. **If it fails**, keep the test and do Step 2 to make it pass. Record which happened in the commit message.

- [ ] **Step 2: Wire nested change tracking (only if Step 1 failed)**

In `AppSettings`, subscribe to each `SlicerSettings` and re-raise. Add to the constructor:

```csharp
        _slicers = new ObservableDictionary<string, SlicerSettings>();
```

If `ObservableDictionary` is unavailable in MaSch.Core, use a plain `Dictionary` and instead have `SlicerSettingsViewModel` (Step 3) call `AppSettings.SetChanged()` explicitly whenever its `Settings` raises `PropertyChanged`. Prefer whichever the MaSch API actually supports — check `ObservableChangeTrackingObject` for a protected method that marks the object dirty before choosing.

- [ ] **Step 3: Create SlicerSettingsViewModel**

`src/CuraManager/ViewModels/Main/SlicerSettingsViewModel.cs`:

```csharp
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using CuraManager.Models;
using CuraManager.Services.Slicers;
using MaSch.Presentation.Wpf.Commands;
using Application = System.Windows.Application;

namespace CuraManager.ViewModels.Main;

[ObservablePropertyDefinition]
internal interface ISlicerSettingsViewModel_Props
{
    SlicerInstallation[] AvailableInstallations { get; set; }
    SlicerInstallation SelectedInstallation { get; set; }
    Version DetectedVersion { get; set; }
    bool IsLoadingVersions { get; set; }
}

public partial class SlicerSettingsViewModel : ObservableObject, ISlicerSettingsViewModel_Props
{
    public SlicerSettingsViewModel(ISlicerProvider provider, SlicerSettings settings)
    {
        Provider = provider;
        Settings = settings;

        ReloadCommand = new AsyncDelegateCommand(() => ReloadInstallationsAsync(true));
        BrowseAppDataCommand = new DelegateCommand(() =>
            Browse(
                Settings.AppDataPath,
                DefaultAppDataRoot,
                path => Settings.AppDataPath = path
            )
        );
        BrowseInstallCommand = new DelegateCommand(() =>
            Browse(
                Settings.ProgramFilesPath,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                path => Settings.ProgramFilesPath = path
            )
        );

        settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SlicerSettings.ProgramFilesPath))
                DetectedVersion = Provider.GetVersion(Settings.ProgramFilesPath);
        };
        DetectedVersion = Provider.GetVersion(Settings.ProgramFilesPath);
    }

    public ISlicerProvider Provider { get; }
    public SlicerSettings Settings { get; }

    public string DisplayName => Provider.DisplayName;
    public bool SupportsProfileUpdateOnOpen => Provider.SupportsProfileUpdateOnOpen;
    public Version LatestSupportedVersion => Provider.LatestSupportedVersion;

    [DependsOn(nameof(DetectedVersion))]
    public bool? IsSupportedVersionSelected =>
        DetectedVersion == null ? null : DetectedVersion <= Provider.LatestSupportedVersion;

    public ICommand ReloadCommand { get; }
    public ICommand BrowseAppDataCommand { get; }
    public ICommand BrowseInstallCommand { get; }

    private string DefaultAppDataRoot =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        + Path.DirectorySeparatorChar;

    public async Task ReloadInstallationsAsync(bool force)
    {
        if (AvailableInstallations != null && !force)
            return;

        IsLoadingVersions = true;
        try
        {
            AvailableInstallations = await Task.Run(() =>
                Provider
                    .FindInstallations()
                    .Prepend(new SlicerInstallation(null, null, null, null, true))
                    .ToArray()
            );
            SelectedInstallation =
                AvailableInstallations.FirstOrDefault(x =>
                    string.Equals(
                        x.AppDataPath,
                        Settings.AppDataPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        x.ProgramFilesPath,
                        Settings.ProgramFilesPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                ) ?? AvailableInstallations[0];
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    partial void OnSelectedInstallationChanged(
        SlicerInstallation previous,
        SlicerInstallation value
    )
    {
        if (value?.Version == null)
            return;

        Settings.AppDataPath = value.AppDataPath;
        Settings.ProgramFilesPath = value.ProgramFilesPath;
    }

    private static void Browse(string current, string fallback, Action<string> apply)
    {
        var dialog = new FolderBrowserDialog
        {
            SelectedPath = !string.IsNullOrWhiteSpace(current) ? current : fallback,
        };

        NativeWindow owner = null;
        if (Application.Current.MainWindow != null)
        {
            owner = new NativeWindow();
            owner.AssignHandle(new WindowInteropHelper(Application.Current.MainWindow).Handle);
        }

        if (dialog.ShowDialog(owner) == DialogResult.OK)
            apply(dialog.SelectedPath);
    }
}
```

The null-`Version` first entry preserves today's "Custom Version" sentinel from `RebuildAvailableVersionsAsync`.

- [ ] **Step 4: Rework SettingsViewModel**

Delete `SelectedCuraVersion`, `AvailableVersions`, `SelectedAvailableVersion`, `IsLoadingVersions`, `LatestSupportedCuraVersion`, `IsSupportedCuraVersionSelected`, `ReloadAvailableVersionsCommand`, `RebuildAvailableVersionsAsync`, `OnSelectedAvailableVersionChanged`, and `Settings_PropertyChanged`.

Add `SlicerSettingsViewModel[] Slicers { get; set; }` to `ISettingsViewModel_Props`. Build it in `OnSettingsChanged`:

```csharp
    partial void OnSettingsChanged(AppSettings previous, AppSettings value)
    {
        Slicers = _slicerRegistry
            .AllProviders.Select(x => new SlicerSettingsViewModel(
                x,
                _slicerRegistry.GetSettings(x)
            ))
            .ToArray();
    }
```

In `OnOpen`, after `Settings = _settingsService.LoadSettings();`, replace the `RebuildAvailableVersionsAsync` call with:

```csharp
        await Task.WhenAll(Slicers.Select(x => x.ReloadInstallationsAsync(false)));
```

Reduce `ExecuteBrowseDirectory` to `PrintsPath` only — it no longer needs reflection:

```csharp
    private void ExecuteBrowsePrintsPath()
    {
        // same FolderBrowserDialog body, writing Settings.PrintsPath directly
    }
```

Change `BrowseDirectoryCommand` to `new DelegateCommand(ExecuteBrowsePrintsPath)` and drop its `CommandParameter="PrintsPath"` in the XAML.

- [ ] **Step 5: Rebuild the settings XAML**

In `src/CuraManager/Views/Main/SettingsView.xaml`, replace the whole `<GroupBox Header="{m:Translation CuraSettings}">` block with an `ItemsControl` over `Slicers`, then add the Legacy Features group as the **last** child of the root `StackPanel`.

```xml
<ItemsControl ItemsSource="{Binding Slicers}">
  <ItemsControl.ItemTemplate>
    <DataTemplate DataType="{x:Type vm:SlicerSettingsViewModel}">
      <GroupBox Header="{Binding DisplayName}">
        <StackPanel>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" MaxWidth="350" />
            </Grid.ColumnDefinitions>
            <StackPanel>
              <mct:Switch
                Margin="0,0,0,10"
                Content="{m:Translation EnableSlicer}"
                IsChecked="{Binding Settings.IsEnabled}"
              />

              <Label Content="{m:Translation SlicerVersion}" />
              <!-- installation combobox, reload button and busy indicator:
                   copy the existing Grid verbatim, changing
                   ItemsSource        -> {Binding AvailableInstallations}
                   SelectedItem       -> {Binding SelectedInstallation}
                   Command            -> {Binding ReloadCommand}
                   DataType           -> models:SlicerInstallation
                   CustomCuraVersion  -> CustomSlicerVersion
                   UnsupportedCuraVersion -> UnsupportedSlicerVersion -->

              <Label Content="{m:Translation SlicerAppDataLocation}" />
              <mct:TextBox
                IsEnabled="{Binding SelectedInstallation.Version, Converter={StaticResource IsNull}}"
                Text="{Binding Settings.AppDataPath}"
              >
                <mct:TextBox.EndContent>
                  <mct:IconButton
                    Command="{Binding BrowseAppDataCommand}"
                    Icon="{m:MaterialDesignIcon Icon=DotsHorizontal}"
                  />
                </mct:TextBox.EndContent>
              </mct:TextBox>

              <Label Content="{m:Translation SlicerInstallLocation}" />
              <mct:TextBox
                IsEnabled="{Binding SelectedInstallation.Version, Converter={StaticResource IsNull}}"
                Text="{Binding Settings.ProgramFilesPath}"
              >
                <mct:TextBox.EndContent>
                  <mct:IconButton
                    Command="{Binding BrowseInstallCommand}"
                    Icon="{m:MaterialDesignIcon Icon=DotsHorizontal}"
                  />
                </mct:TextBox.EndContent>
              </mct:TextBox>
            </StackPanel>
          </Grid>

          <!-- the two warning TextBlocks, copied verbatim except:
               SelectedCuraVersion binding    -> DetectedVersion
               IsSupportedCuraVersionSelected -> IsSupportedVersionSelected
               LatestSupportedCuraVersion     -> LatestSupportedVersion
               Warn_NoCuraVersionFound        -> Warn_NoSlicerVersionFound
               Warn_CuraVersionNotSupported   -> Warn_SlicerVersionNotSupported -->

          <CheckBox
            Margin="0,10,0,0"
            Content="{m:Translation UpdateProfilesInSlicerProjects}"
            IsChecked="{Binding Settings.UpdateProjectsOnOpen}"
            Visibility="{Binding SupportsProfileUpdateOnOpen, Converter={StaticResource BoolToVisibility}}"
          />
        </StackPanel>
      </GroupBox>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

`CuraVersionNotSupportedStyle` in the page `Resources` binds to the old view-model property; move that trigger inline into the warning `TextBlock` inside the template, because a page-level style cannot see the per-item DataContext.

`ShowWebDialogWhenAddingLink` currently lives inside the Cura group. Move it up into the `ApplicationSettings` group — it is not slicer-specific.

Then the Legacy Features group, last in the root `StackPanel`:

```xml
<GroupBox Header="{m:Translation LegacyFeatures}">
  <StackPanel>
    <mct:Switch
      Content="{m:Translation EnableLegacyCuraProjectNaming}"
      IsChecked="{Binding Settings.EnableLegacyCuraProjectNaming}"
    />
    <TextBlock
      Margin="0,8,0,0"
      Opacity="0.7"
      Text="{m:Translation Desc_LegacyCuraProjectNaming}"
      TextWrapping="Wrap"
    />
  </StackPanel>
</GroupBox>
```

- [ ] **Step 6: Delete the legacy settings properties**

Nothing reads `CuraAppDataPath`, `CuraProgramFilesPath`, or `UpdateCuraProjectsOnOpen` any more. Remove all three from `IAppSettings_Props` and remove `_updateCuraProjectsOnOpen = true;` from the `AppSettings` constructor. `SettingsMigration` reads them off the raw `JObject`, not off the typed model, so migration is unaffected.

Update `SmokeTests.CanReferenceTheApplicationAssembly` — it asserts `UpdateCuraProjectsOnOpen`. Change it to `Assert.NotNull(new AppSettings().Slicers);`.

- [ ] **Step 7: Verify**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green.

- [ ] **Step 8: Run the app and check the settings page**

Run:
```bash
dotnet run --project src/CuraManager/CuraManager.csproj
```
Confirm: one group per slicer with a working enable switch, the installation combobox populated for Cura, and a Legacy Features group at the bottom. Settings save with every slicer disabled.

- [ ] **Step 9: Format and commit**

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: render one settings group per slicer plus a Legacy Features section"
```

---

### Task 9: Prints page, create dialog, and strings

**Files:**
- Rename: `src/CuraManager/Views/CreateCuraProjectDialog.xaml{,.cs}` → `CreateSlicerProjectDialog.xaml{,.cs}`
- Modify: `src/CuraManager/ViewModels/Main/PrintsViewModel.cs`
- Modify: `src/CuraManager/Views/Main/PrintsView.xaml`
- Modify: `src/CuraManager/Resources/StringTable.resx`, `src/CuraManager/Resources/StringTable.de.resx`

**Interfaces:**
- Consumes: `ISlicerRegistry`, `ISlicerProvider`, `SlicerLaunchRequest`.
- Produces: `CreateSlicerProjectDialog` with `IList<PrintElementFileSelection> Models`, `string ProjectName`, `bool ShowProjectName`; `PrintsViewModel.ActiveSlicer` (`ISlicerProvider`), `.EnabledSlicers`, `.IsSlicerSelectionVisible` (`bool`).

- [ ] **Step 1: Rewrite the string tables**

Apply this table to **both** `StringTable.resx` and `StringTable.de.resx`. Rename the key, and replace the value with the English / German text given. Keys not listed are untouched.

| key | English | German |
|---|---|---|
| `SlicerProjectFiles` (added Task 7) | `{0} Project Files` | `{0}-Projektdateien` |
| `Title_CreateSlicerProject` | `Create {0} Project` | `{0}-Projekt erstellen` |
| `ToolTip_NewSlicerProject` | `Create new {0} project` | `Neues {0}-Projekt erstellen` |
| `Prog_CreateSlicerProject` | `Creating {0} project...` | `{0}-Projekt wird erstellt...` |
| `Suc_CreateSlicerProject` | `{0} project has been created` | `{0}-Projekt wurde erstellt` |
| `Fail_CreateSlicerProject` | `Creating {0} project failed` | `Erstellen des {0}-Projekts fehlgeschlagen` |
| `SlicerAppDataLocation` | `AppData location` | `AppData-Speicherort` |
| `SlicerInstallLocation` | `Installation location` | `Installationsverzeichnis` |
| `SlicerVersion` | `Version` | `Version` |
| `CustomSlicerVersion` | `Custom Version` | `Benutzerdefinierte Version` |
| `UnsupportedSlicerVersion` | `This version is not officially supported.` | `Diese Version wird nicht offiziell unterstützt.` |
| `LatestSupportedSlicerVersion` | `Latest supported version` | `Neueste unterstützte Version` |
| `DetectedSlicerVersion` | `Version found in the provided path` | `Im angegebenen Pfad gefundene Version` |
| `Warn_NoSlicerVersionFound` | `The version could not be detected using the provided path.` | `Die Version konnte im angegebenen Pfad nicht ermittelt werden.` |
| `Warn_SlicerVersionNotSupported` | `The version installed in the provided path is higher than the latest supported version. This might not be an issue, but be aware that there might be unforeseen issues when creating a new project. You can also check if there is a new version of CuraManager available that supports that version.` | `Die im angegebenen Pfad installierte Version ist neuer als die neueste unterstützte Version. Das muss kein Problem sein, es kann beim Erstellen neuer Projekte aber zu unvorhergesehenen Fehlern kommen. Prüfen Sie gegebenenfalls, ob eine neue Version von CuraManager verfügbar ist, die diese Version unterstützt.` |
| `UpdateProfilesInSlicerProjects` | `Update profile and machine settings in the project before open. This prevents unwanted changes to your profiles when opening projects.` | `Profil- und Geräteeinstellungen im Projekt vor dem Öffnen aktualisieren. Das verhindert ungewollte Änderungen an Ihren Profilen beim Öffnen von Projekten.` |
| `Msg_SlicerPathsNotConfigured` | `The paths for {0} are not configured correctly. Please check the settings page.` | `Die Pfade für {0} sind nicht korrekt konfiguriert. Bitte prüfen Sie die Einstellungsseite.` |
| `EnableSlicer` | `Enabled` | `Aktiviert` |
| `ActiveSlicer` | `Active slicer` | `Aktiver Slicer` |
| `Msg_NoSlicerEnabled` | `No slicer is enabled. Enable one on the settings page to create projects.` | `Es ist kein Slicer aktiviert. Aktivieren Sie einen auf der Einstellungsseite, um Projekte zu erstellen.` |
| `LegacyFeatures` | `Legacy Features` | `Veraltete Funktionen` |
| `EnableLegacyCuraProjectNaming` | `Set the project name in Cura automatically` | `Projektnamen in Cura automatisch setzen` |
| `Desc_LegacyCuraProjectNaming` | `After launching Cura, type the project name into its name field using UI automation. Only affects Cura, only works on Windows, and can break when Cura changes its interface. This option will be removed in version 2.0.` | `Nach dem Start von Cura wird der Projektname per UI-Automatisierung in das Namensfeld eingetragen. Betrifft nur Cura, funktioniert nur unter Windows und kann bei Oberflächenänderungen in Cura fehlschlagen. Diese Option wird in Version 2.0 entfernt.` |

Delete these keys from both files: `CuraProjectFiles`, `Title_CreateCuraProject`, `ToolTip_NewCuraProject`, `Prog_CreateCuraProject`, `Suc_CreateCuraProject`, `Fail_CreateCuraProject`, `CuraAppDataLocation`, `CuraProgramFilesLocation`, `CuraVersion`, `CustomCuraVersion`, `UnsupportedCuraVersion`, `LatestSupportedCuraVersion`, `SelectedCuraVersion`, `Warn_NoCuraVersionFound`, `Warn_CuraVersionNotSupported`, `UpdateProfilesInCuraProjects`, `Msg_CuraPathsNotConfigured`, `CuraSettings`.

Keep `Untitled` — the create dialog still prefills with it in legacy mode.

- [ ] **Step 2: Rename and adjust the create dialog**

Rename both files and the class to `CreateSlicerProjectDialog`, and `ICreateCuraProjectDialog_Props` to `ICreateSlicerProjectDialog_Props`. Add `bool ShowProjectName` to the props interface and take it plus the provider in the constructor:

```csharp
    public CreateSlicerProjectDialog(PrintElement element, ISlicerProvider provider, bool showProjectName)
    {
        ServiceContext.GetService(out _translationManager);

        Models = new ObservableCollection<PrintElementFileSelection>(
            element?.ModelFiles.Select(x => new PrintElementFileSelection(x))
                ?? new List<PrintElementFileSelection>()
        );
        ProjectName =
            element?.Name ?? _translationManager.GetTranslation(nameof(StringTable.Untitled));
        ShowProjectName = showProjectName;
        SlicerIconResourceKey = provider.IconResourceKey;
        Title = string.Format(
            _translationManager.GetTranslation(nameof(StringTable.Title_CreateSlicerProject)),
            provider.DisplayName
        );

        InitializeComponent();
    }
```

In the XAML: drop the `Title="{m:Translation Title_CreateCuraProject}"` attribute (set in code now), and wrap the project-name `Label` and `TextBox` so both collapse together:

```xml
<StackPanel Visibility="{Binding ShowProjectName, RelativeSource={RelativeSource AncestorType=local:CreateSlicerProjectDialog}, Converter={StaticResource BoolToVisibility}}">
  <Label Margin="0" Content="{m:Translation ProjectName}" />
  <TextBox
    KeyDown="ProjectNameTextBoxOnKeyDown"
    Text="{Binding ProjectName, RelativeSource={RelativeSource AncestorType=local:CreateSlicerProjectDialog}}"
  />
</StackPanel>
```

Update the two other `AncestorType=local:CreateCuraProjectDialog` bindings to the new type name. Leave `CustomIcon` as `CuraIcon` for now; binding it to the provider is optional polish.

- [ ] **Step 3: Rework PrintsViewModel**

Add to `IPrintsViewModel_Props`:

```csharp
    ISlicerProvider ActiveSlicer { get; set; }
```

Add these members:

```csharp
    public IReadOnlyList<ISlicerProvider> EnabledSlicers => _slicerRegistry.EnabledProviders;

    [DependsOn(nameof(ActiveSlicer))]
    public bool IsSlicerSelectionVisible => EnabledSlicers.Count > 1;

    [DependsOn(nameof(ActiveSlicer))]
    public string NewSlicerProjectToolTip =>
        ActiveSlicer == null
            ? _translationManager.GetTranslation(nameof(StringTable.Msg_NoSlicerEnabled))
            : string.Format(
                _translationManager.GetTranslation(nameof(StringTable.ToolTip_NewSlicerProject)),
                ActiveSlicer.DisplayName
            );

    partial void OnActiveSlicerChanged(ISlicerProvider previous, ISlicerProvider value)
    {
        if (value == null)
            return;

        var settings = _settingsService.LoadSettings();
        settings.ActiveSlicerId = value.Id;
        _settingsService.SaveSettings(settings);
    }
```

Set `ActiveSlicer = _slicerRegistry.ActiveProvider;` at the end of `OnOpen`, and change `NewSlicerProjectCommand`'s can-execute to `x => x != null && _slicerRegistry.ActiveProvider != null`.

Rename `NewCuraProjectCommand` to `NewSlicerProjectCommand` and rewrite its handler — this is where `CuraService.CreateCuraProject` lands:

```csharp
    private async Task ExecuteNewSlicerProject(PrintElement project)
    {
        var provider = _slicerRegistry.ActiveProvider;
        if (provider == null)
            return;

        var slicerSettings = _slicerRegistry.GetSettings(provider);
        if (!provider.ArePathsValid(slicerSettings))
        {
            MessageBox.Show(
                string.Format(
                    _translationManager.GetTranslation(
                        nameof(StringTable.Msg_SlicerPathsNotConfigured)
                    ),
                    provider.DisplayName
                ),
                "CuraManager",
                AlertButton.Ok,
                AlertImage.Warning
            );
            return;
        }

        var useLegacyNaming =
            _settingsService.LoadSettings().EnableLegacyCuraProjectNaming
            && provider.SupportsProfileUpdateOnOpen;

        var dialog = new CreateSlicerProjectDialog(project, provider, useLegacyNaming)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
            return;

        var modelFiles = (
            from x in dialog.Models
            where x.IsEnabled && x.Amount > 0
            from _ in Enumerable.Range(0, x.Amount)
            select x.Element.FilePath
        ).ToArray();

        await ExecuteLoadingAction(
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Prog_CreateSlicerProject)),
                provider.DisplayName
            ),
            () =>
                Task.Run(() =>
                    provider.LaunchWithModels(
                        slicerSettings,
                        new SlicerLaunchRequest(
                            project.DirectoryLocation,
                            modelFiles,
                            useLegacyNaming ? dialog.ProjectName : null
                        )
                    )
                ),
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Suc_CreateSlicerProject)),
                provider.DisplayName
            ),
            string.Format(
                _translationManager.GetTranslation(nameof(StringTable.Fail_CreateSlicerProject)),
                provider.DisplayName
            )
        );
    }
```

`provider.SupportsProfileUpdateOnOpen` is being used as "is this Cura". That is accidental coupling — instead compare `provider.Id == "cura"` via a `const string CuraSlicerProvider.ProviderId = "cura"` added in Task 6, and reference `CuraSlicerProvider.ProviderId` here. Legacy naming is Cura-specific by definition, so naming it explicitly is honest.

- [ ] **Step 4: Add the toolbar combobox**

In `src/CuraManager/Views/Main/PrintsView.xaml`, inside `ToolbarContent` after the reload button, add a separator and the combobox:

```xml
<Separator Visibility="{Binding IsSlicerSelectionVisible, Converter={StaticResource BoolToVisibility}}" />
<ComboBox
  MinWidth="160"
  VerticalAlignment="Center"
  DisplayMemberPath="DisplayName"
  ItemsSource="{Binding EnabledSlicers}"
  SelectedItem="{Binding ActiveSlicer}"
  ToolTip="{m:Translation ActiveSlicer}"
  Visibility="{Binding IsSlicerSelectionVisible, Converter={StaticResource BoolToVisibility}}"
/>
```

Update the two `NewCuraProjectCommand` references (lines ~214 and ~334) to `NewSlicerProjectCommand`, and change both `ToolTip="{m:Translation ToolTip_NewCuraProject}"` to `ToolTip="{Binding NewSlicerProjectToolTip}"`. Leave `Icon="{StaticResource CuraIcon}"` — Task 10 makes it follow the active provider.

- [ ] **Step 5: Verify**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green. A missing resx key surfaces here as a `StringTable` compile error.

- [ ] **Step 6: Run the app**

Run:
```bash
dotnet run --project src/CuraManager/CuraManager.csproj
```
Confirm: with only Cura enabled the combobox is hidden; the create button is disabled when no slicer is enabled and its tooltip explains why; with legacy naming on, the create dialog shows the name field; with it off, the field is gone.

- [ ] **Step 7: Format and commit**

```bash
dotnet csharpier format .
git add -A src
git commit -m "feat: add active-slicer selection and genericize all slicer-facing strings"
```

---

### Task 10: Anycubic Slicer Next provider

**Files:**
- Create: `src/CuraManager/Services/Slicers/OrcaFamilySlicerProvider.cs`
- Create: `src/CuraManager/Services/Slicers/AnycubicSlicerProvider.cs`
- Modify: `src/CuraManager/Resources/Geometries.xaml`
- Modify: `src/CuraManager/App.xaml.cs`
- Modify: `src/CuraManager/Views/Main/PrintsView.xaml`
- Test: `tests/CuraManager.Tests/AnycubicSlicerProviderTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–4.
- Produces: `abstract OrcaFamilySlicerProvider : ISlicerProvider` with protected abstract `AppKey`, `ExecutableFileNames`, `InstallDirNameFilter`, `SliceInfoHeaderPrefix`; `AnycubicSlicerProvider` with `Id == "anycubic"`, `DisplayName == "Anycubic Slicer Next"`.

- [ ] **Step 1: Read the real installation**

The public repo does not match the shipped binary, so read these off the machine. Run:

```bash
ls "C:/Program Files" | grep -i -E "anycubic|slicer"
```

then for the directory found:

```bash
ls "C:/Program Files/<dir>"/*.exe && ls "$APPDATA" | grep -i anycubic
```

Record: the install directory name, the executable file name, and the AppData directory name (the `AppKey`). Confirm the `.conf` file inside the AppData directory is JSON and note whether `last_export_path` sits at the JSON root or nested under an `app` object — the C++ has both spellings in different overloads, and the shipped build only uses one.

**Write the four values into the commit message.** If Anycubic is not installed, stop and ask; do not guess.

- [ ] **Step 2: Write the failing detection tests**

`tests/CuraManager.Tests/AnycubicSlicerProviderTests.cs`:

```csharp
using CuraManager.Models;
using CuraManager.Services;
using CuraManager.Services.Slicers;
using Xunit;

namespace CuraManager.Tests;

public class AnycubicSlicerProviderTests
{
    private const string AcNextSliceInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <config>
          <header>
            <header_item key="X-ACNext-Client-Type" value="slicer"/>
            <header_item key="X-ACNext-Client-Version" value="1.4.1.2 20260604104233"/>
          </header>
        </config>
        """;

    private const string BblSliceInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <config>
          <header>
            <header_item key="X-BBL-Client-Type" value="slicer"/>
          </header>
        </config>
        """;

    private sealed class NoLocks : IFileLockInspector
    {
        public IReadOnlyList<string> GetLockingProcessNames(string filePath) =>
            Array.Empty<string>();
    }

    private static SlicerMatch Match(string path)
    {
        using var candidate = SlicerProjectFileCandidate.Create(path, new NoLocks());
        return new AnycubicSlicerProvider().IsProjectFile(candidate);
    }

    [Fact]
    public void AcNextHeader_IsExactMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", AcNextSliceInfo)
        );

        Assert.Equal(SlicerMatch.Exact, Match(path));
    }

    [Fact]
    public void BblHeader_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(
            scope.File("a.3mf"),
            ("Metadata/project_settings.config", "{}"),
            ("Metadata/slice_info.config", BblSliceInfo)
        );

        Assert.Equal(SlicerMatch.Probable, Match(path));
    }

    [Fact]
    public void ProjectSettingsWithoutSliceInfo_IsProbableMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Metadata/project_settings.config", "{}"));

        Assert.Equal(SlicerMatch.Probable, Match(path));
    }

    [Fact]
    public void CuraProject_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("Cura/preferences.cfg", "[general]"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }

    [Fact]
    public void PlainModel3mf_IsNoMatch()
    {
        using var scope = new TestZip.Scope();
        var path = TestZip.Create(scope.File("a.3mf"), ("3D/3dmodel.model", "<model/>"));

        Assert.Equal(SlicerMatch.None, Match(path));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~AnycubicSlicerProviderTests
```
Expected: compile error, `AnycubicSlicerProvider` does not exist.

- [ ] **Step 4: Implement OrcaFamilySlicerProvider**

`src/CuraManager/Services/Slicers/OrcaFamilySlicerProvider.cs`:

```csharp
using System.IO;
using CuraManager.Extensions;
using CuraManager.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CuraManager.Services.Slicers;

/// <summary>
/// Shared behaviour for slicers descended from Bambu Studio (OrcaSlicer, Anycubic
/// Slicer Next and friends): the same 3mf layout and the same JSON app config.
/// </summary>
public abstract class OrcaFamilySlicerProvider : ISlicerProvider
{
    private const string ProjectSettingsEntry = "Metadata/project_settings.config";
    private const string SliceInfoEntry = "Metadata/slice_info.config";

    private static readonly string ProgramFilesDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ProgramFiles
    );
    private static readonly string AppDataDir = Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData
    );

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string IconResourceKey { get; }
    public abstract Version LatestSupportedVersion { get; }

    /// <summary>No Orca-family slicer rewrites profiles on open the way Cura does.</summary>
    public bool SupportsProfileUpdateOnOpen => false;

    /// <summary>Names the AppData directory and the <c>&lt;AppKey&gt;.conf</c> file inside it.</summary>
    protected abstract string AppKey { get; }

    protected abstract string[] ExecutableFileNames { get; }

    /// <summary>Matched case-insensitively against Program Files directory names.</summary>
    protected abstract string InstallDirNameFilter { get; }

    /// <summary>Producer key prefix in slice_info.config, for example <c>X-ACNext-</c>.</summary>
    protected abstract string SliceInfoHeaderPrefix { get; }

    public SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate)
    {
        if (!string.Equals(candidate.Extension, ".3mf", StringComparison.OrdinalIgnoreCase))
            return SlicerMatch.None;

        var sliceInfo = candidate.ReadEntryText(SliceInfoEntry);
        if (sliceInfo != null && sliceInfo.Contains(SliceInfoHeaderPrefix, StringComparison.Ordinal))
            return SlicerMatch.Exact;

        // Bambu lineage, but produced by an unidentified sibling. Older files from this
        // slicer also land here, because they emitted the inherited X-BBL- keys.
        if (candidate.HasEntry(ProjectSettingsEntry) || sliceInfo != null)
            return SlicerMatch.Probable;

        return SlicerMatch.None;
    }

    public IEnumerable<SlicerInstallation> FindInstallations()
    {
        if (!Directory.Exists(ProgramFilesDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(ProgramFilesDir))
        {
            var name = Path.GetFileName(dir);
            if (!name.Contains(InstallDirNameFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (GetExecutablePath(dir) == null)
                continue;

            var version = GetVersion(dir);
            if (version == null)
                continue;

            var appDataPath = Path.Combine(AppDataDir, AppKey);
            if (!Directory.Exists(appDataPath))
                continue;

            yield return new SlicerInstallation(
                version,
                name,
                dir,
                appDataPath,
                version <= LatestSupportedVersion
            );
        }
    }

    public Version GetVersion(string programFilesPath)
    {
        var exePath = GetExecutablePath(programFilesPath);
        if (exePath == null)
            return null;

        return VersionExtensions.SafeParse(FileVersionInfo.GetVersionInfo(exePath).FileVersion);
    }

    public bool ArePathsValid(SlicerSettings settings) =>
        GetExecutablePath(settings.ProgramFilesPath) != null
        && File.Exists(GetConfigFilePath(settings.AppDataPath));

    public void LaunchWithModels(SlicerSettings settings, SlicerLaunchRequest request)
    {
        SetLastExportPath(settings.AppDataPath, request.ProjectDirectory);
        Start(settings, request.ModelFiles);
    }

    public void OpenProject(SlicerSettings settings, string projectFilePath)
    {
        SetLastExportPath(settings.AppDataPath, Path.GetDirectoryName(projectFilePath));
        Start(settings, new[] { projectFilePath });
    }

    private void Start(SlicerSettings settings, IReadOnlyList<string> files)
    {
        var exePath =
            GetExecutablePath(settings.ProgramFilesPath)
            ?? throw new FileNotFoundException($"Could not find the {DisplayName} executable.");

        Process.Start(
            new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"\"{string.Join("\" \"", files)}\"",
            }
        );
    }

    private string GetExecutablePath(string programFilesPath)
    {
        if (string.IsNullOrEmpty(programFilesPath) || !Directory.Exists(programFilesPath))
            return null;

        return ExecutableFileNames
            .Select(x => Path.Combine(programFilesPath, x))
            .FirstOrDefault(File.Exists);
    }

    private string GetConfigFilePath(string appDataPath) =>
        string.IsNullOrEmpty(appDataPath) ? null : Path.Combine(appDataPath, $"{AppKey}.conf");

    /// <summary>
    /// Points the slicer's save dialog at the project folder, the JSON equivalent of
    /// Cura's <c>dialog_save_path</c>. Every unrelated key is preserved.
    /// </summary>
    private void SetLastExportPath(string appDataPath, string targetPath)
    {
        var configPath = GetConfigFilePath(appDataPath);
        if (configPath == null || !File.Exists(configPath))
            return;

        var config = JObject.Parse(File.ReadAllText(configPath));
        if (config["app"] is JObject app)
            app["last_export_path"] = targetPath;
        else
            config["last_export_path"] = targetPath;

        File.WriteAllText(configPath, config.ToString(Formatting.Indented));
    }
}
```

`SetLastExportPath` writes whichever shape the file already has, which is why Step 1 only has to confirm rather than change code.

- [ ] **Step 5: Implement AnycubicSlicerProvider**

`src/CuraManager/Services/Slicers/AnycubicSlicerProvider.cs`. **Substitute the values recorded in Step 1** — the ones below are the expected defaults, not verified fact.

```csharp
namespace CuraManager.Services.Slicers;

public class AnycubicSlicerProvider : OrcaFamilySlicerProvider
{
    public const string ProviderId = "anycubic";

    public override string Id => ProviderId;
    public override string DisplayName => "Anycubic Slicer Next";
    public override string IconResourceKey => "AnycubicIcon";

    // Known-good shipped version at the time of writing.
    public override Version LatestSupportedVersion { get; } = new Version(1, 4, 1, 2);

    protected override string AppKey => "AnycubicSlicerNext";

    protected override string[] ExecutableFileNames => ["AnycubicSlicerNext.exe"];

    protected override string InstallDirNameFilter => "anycubic";

    protected override string SliceInfoHeaderPrefix => "X-ACNext-";
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~AnycubicSlicerProviderTests
```
Expected: 5 tests pass.

- [ ] **Step 6a: Write the .conf round-trip test**

The JSON equivalent of the Cura test, and equally important — this file holds the user's printer and filament presets.

Change `SetLastExportPath` to `internal static void SetLastExportPath(string configPath, string targetPath)`, taking the resolved config path, and have the instance callers pass `GetConfigFilePath(settings.AppDataPath)` (returning early when it is null or missing).

Add to `tests/CuraManager.Tests/AnycubicSlicerProviderTests.cs`:

```csharp
    [Fact]
    public void SetLastExportPath_NestedUnderApp_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        System.IO.File.WriteAllText(
            configPath,
            """
            {
              "app": { "last_export_path": "C:\\old", "language": "en" },
              "presets": { "filament": "PLA Basic" }
            }
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var result = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(configPath));
        Assert.Equal("D:\\Prints\\Widget", (string)result["app"]["last_export_path"]);
        Assert.Equal("en", (string)result["app"]["language"]);
        Assert.Equal("PLA Basic", (string)result["presets"]["filament"]);
    }

    [Fact]
    public void SetLastExportPath_AtRoot_PreservesEveryUnrelatedKey()
    {
        using var scope = new TestZip.Scope();
        var configPath = scope.File("AnycubicSlicerNext.conf");
        System.IO.File.WriteAllText(
            configPath,
            """
            { "last_export_path": "C:\\old", "presets": { "filament": "PLA Basic" } }
            """
        );

        OrcaFamilySlicerProvider.SetLastExportPath(configPath, "D:\\Prints\\Widget");

        var result = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(configPath));
        Assert.Equal("D:\\Prints\\Widget", (string)result["last_export_path"]);
        Assert.Equal("PLA Basic", (string)result["presets"]["filament"]);
    }

    [Fact]
    public void SetLastExportPath_MissingFile_DoesNotThrow()
    {
        using var scope = new TestZip.Scope();

        OrcaFamilySlicerProvider.SetLastExportPath(scope.File("absent.conf"), "D:\\Prints");
    }
```

Two shapes are tested because Step 1 only confirms which one the shipped build uses; handling both means the provider survives Anycubic changing it.

- [ ] **Step 6b: Run the round-trip tests**

Run:
```bash
dotnet test CuraManager.slnx --filter FullyQualifiedName~AnycubicSlicerProviderTests
```
Expected: 8 tests pass.

- [ ] **Step 7: Add the icon**

In `src/CuraManager/Resources/Geometries.xaml`, add an `AnycubicGeometry` path and an `AnycubicIcon` next to the Cura pair, following the same structure. If no usable path data can be produced, use a neutral printer glyph instead of leaving the binding unresolved:

```xml
  <m:Icon
    x:Key="AnycubicIcon"
    Stretch="Uniform"
    Type="MaterialDesign"
    Value="Printer3d"
  />
```

Verify the resource resolves by launching the app — an unresolved `StaticResource` throws at window load, so a successful launch is the check.

- [ ] **Step 8: Register the provider and bind the button icon**

In `src/CuraManager/App.xaml.cs`, add `new AnycubicSlicerProvider(),` to the provider array in the `SlicerRegistry` construction.

Add to `PrintsViewModel`:

```csharp
    [DependsOn(nameof(ActiveSlicer))]
    public object ActiveSlicerIcon =>
        ActiveSlicer == null
            ? null
            : Application.Current.TryFindResource(ActiveSlicer.IconResourceKey);
```

In `PrintsView.xaml`, change both `Icon="{StaticResource CuraIcon}"` occurrences to `Icon="{Binding ActiveSlicerIcon}"`.

- [ ] **Step 9: Verify end to end**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
dotnet run --project src/CuraManager/CuraManager.csproj
```
Confirm in the app: Anycubic appears on the settings page and its installation is detected; enabling it makes the toolbar combobox appear; switching to it changes the create-button icon and tooltip; an existing Anycubic 3mf shows under an "Anycubic Slicer Next Project Files" heading; "create project" launches Anycubic with the models, and its save dialog defaults to the project folder.

- [ ] **Step 10: Format and commit**

Include the Step 1 findings in the message.

```bash
dotnet csharpier format .
git add -A src tests
git commit -m "feat: add Anycubic Slicer Next provider on a shared Orca-family base"
```

---

### Task 11: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/release-notes/next.md`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing.

- [ ] **Step 1: Update the README**

Change the subtitle from `Application to manage 3D Prints using the Ultimaker Cura Slicer.` to `Application to manage 3D Prints using UltiMaker Cura or Anycubic Slicer Next.`

In "Getting Started", replace the Cura-only bullet list with per-slicer wording: enable the slicers you use on the settings page, pick an installation per slicer, and choose the active one on the Print Projects page when more than one is enabled. Note that the "Create new project" button is disabled until at least one slicer is enabled.

Add a short "Legacy features" subsection explaining that automatic project naming in Cura is on for upgrading users, off for new installs, toggleable at the bottom of the settings page, and removed in 2.0.

Screenshots in `resources/images/` are now stale. Do not regenerate them; add a line noting they show an older version.

- [ ] **Step 2: Add release notes**

Append to the table in `docs/release-notes/next.md`:

```markdown
| feature | CuraManager now supports multiple slicers. Anycubic Slicer Next is supported alongside UltiMaker Cura, and can be enabled and configured on the settings page. |
| feature | When more than one slicer is enabled, the active one can be selected on the "Print Projects" page. |
| feature | Project files are now grouped by the slicer that produced them. |
| tech | Automatic project naming in Cura moved behind a "Legacy features" option. It stays enabled for existing installations and is disabled for new ones. It will be removed in version 2.0. |
```

- [ ] **Step 3: Verify and commit**

Run:
```bash
dotnet build CuraManager.slnx && dotnet test CuraManager.slnx
```
Expected: all green.

```bash
dotnet csharpier format .
git add README.md docs/release-notes/next.md
git commit -m "docs: document multi-slicer support and the legacy naming option"
```

---

## Verification checklist

Before handing the branch over:

- [ ] `dotnet build CuraManager.slnx` succeeds
- [ ] `dotnet test CuraManager.slnx` — all tests pass
- [ ] `dotnet csharpier check .` reports no unformatted files
- [ ] `dotnet publish CuraManager.slnx -c Release -r win-x64 --no-self-contained -p:TreatWarningsAsErrors=true` succeeds and produces exactly one zip
- [ ] `git log --oneline main..HEAD` shows one commit per task, none of them a version bump
- [ ] Upgrading an existing `settings.json`: legacy naming on, Cura enabled, paths preserved, `settings.json.bak` written
- [ ] Deleting `settings.json` and starting fresh: legacy naming off, installed slicers auto-enabled
