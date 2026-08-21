# Multi-Slicer Support — Design

**Date:** 2026-08-21
**Branch:** `feature/multi-slicer-support`
**Status:** Approved for implementation planning

## Goal

CuraManager supports exactly one slicer today: UltiMaker Cura, hardcoded from the
settings schema through to the file list. This design replaces that with a
provider abstraction, then adds Anycubic Slicer Next as the second provider.

Adding OrcaSlicer or Bambu Studio afterwards must cost roughly one small
subclass. PrusaSlicer is a sibling of the Orca family rather than a variant of
it, and is out of scope here.

## Non-goals

- Renaming the application, executable, root namespace, or repository. Internal
  type and property names that are no longer Cura-specific do get renamed; the
  product identity does not.
- Shipping OrcaSlicer, Bambu Studio, or PrusaSlicer providers.
- Regenerating the screenshots in `resources/images/`.
- Porting to Avalonia. The port is a stated future goal, so this design avoids
  adding new WPF/Win32 coupling and removes some existing coupling, but performs
  no port work.

## Constraints

- **No UI automation.** The Cura provider's `System.Windows.Automation` +
  `SendKeys` project-naming code is deleted rather than generalized. It is
  fragile, and it is Windows-only in a way that blocks the Avalonia port.
- **No project name.** Follows from the above: the create-project dialog becomes
  model selection only. Project names come from the folder and from whatever
  filename the user chooses when saving in the slicer.
- Provider logic stays free of WPF and Win32 except behind an interface with a
  swappable implementation.

## Research findings

The Anycubic Slicer Next source is at
[ANYCUBIC-3D/AnycubicSlicerNext](https://github.com/ANYCUBIC-3D/AnycubicSlicerNext)
(AGPL-3.0), a fork of OrcaSlicer, itself forked from Bambu Studio ← PrusaSlicer
← Slic3r.

|                | Cura                             | Anycubic Slicer Next                      |
| -------------- | -------------------------------- | ----------------------------------------- |
| 3mf marker     | entries under `Cura/`            | `Metadata/project_settings.config`         |
| Producer ID    | —                                | `Metadata/slice_info.config`, `X-ACNext-*` |
| Config file    | `<AppData>/cura.cfg` (INI)       | `%APPDATA%/<AppKey>/<AppKey>.conf` (JSON)  |
| Save-dir key   | `local_file.dialog_save_path`    | `app.last_export_path`                     |

Presetting the save directory therefore works the same way in both; only the
file format differs.

**The public repository does not match the shipped binary.** Repo `main`
([bbs_3mf.cpp:7800](https://github.com/ANYCUBIC-3D/AnycubicSlicerNext/blob/main/src/libslic3r/Format/bbs_3mf.cpp))
still emits the inherited `X-BBL-Client-Type`, while a real 3mf saved by shipped
version 1.4.1.2 contains:

```xml
<config>
  <header>
    <header_item key="X-ACNext-Client-Type" value="slicer"/>
    <header_item key="X-ACNext-Client-Version" value="1.4.1.2 20260604104233"/>
  </header>
</config>
```

Consequence: **real files on the developer's machine are authoritative, not the
source tree.** Every Anycubic-specific constant must be verified against the
actual installation during implementation.

Second consequence: older Anycubic files may carry `X-BBL-*` and be
indistinguishable from Orca or Bambu output, so detection cannot be a plain
boolean.

## Architecture

### Provider interface

New folder `src/CuraManager/Services/Slicers/`, mirroring the existing
`Services/WebProviders/` layout and its `_Interfaces` convention.

```csharp
public interface ISlicerProvider
{
    string Id { get; }                       // "cura" / "anycubic" — stable settings key
    string DisplayName { get; }              // product name, untranslated
    string IconResourceKey { get; }
    bool SupportsProfileUpdateOnOpen { get; }
    Version LatestSupportedVersion { get; }

    IEnumerable<SlicerInstallation> FindInstallations();
    Version GetVersion(string programFilesPath);
    bool ArePathsValid(SlicerSettings settings);

    SlicerMatch IsProjectFile(SlicerProjectFileCandidate candidate);
    void LaunchWithModels(SlicerSettings s, string projectDir, IEnumerable<string> models);
    void OpenProject(SlicerSettings s, string projectFilePath);
}

public record SlicerInstallation(
    Version Version,
    string DisplayName,
    string ProgramFilesPath,
    string AppDataPath,
    bool IsSupported);
```

`SlicerInstallation` replaces `Models/CuraVersion.cs`.

`SupportsProfileUpdateOnOpen` is a UI-rendering hint, and is documented as such.
The behaviour it gates lives entirely inside the Cura provider's `OpenProject`.
It exists so that one shared settings template can serve every provider. An
earlier draft used a `SlicerCapabilities` flags enum; that was cut because the
second flag was redundant (installation-count answers it) and a flags enum that
grows a member per provider-specific option does not age well. **If a second
provider-specific option appears, promote this to a declarative
`IReadOnlyList<SlicerToggleOption>` rendered by an `ItemsControl` — not before.**

### Detection

```csharp
public enum SlicerMatch { None, Probable, Exact }

public sealed class SlicerProjectFileCandidate
{
    public string FilePath { get; }
    public string Extension { get; }
    public IReadOnlyCollection<string> ZipEntryNames { get; }  // lazy, .3mf only
    public string ReadEntryText(string entryName);             // null if absent; memoized
}
```

- **Exact** — positive producer identification. Cura: an entry under `Cura/`.
  Anycubic: `Metadata/slice_info.config` contains a `header_item` whose `key`
  starts with `X-ACNext-`.
- **Probable** — Bambu-lineage markers present but no positive producer ID.
  Covers older Anycubic files and projects saved before slicing.
- **None** — no match.

The registry resolves a file by taking the highest-confidence match. Ties among
`Probable` break to the active provider, then to registration order.

The registry opens each `.3mf` **once** per detection pass and shares the open
archive across all providers, so provider count does not multiply zip reads.
Providers become pure predicates over entry names and entry text, which makes
them testable against synthesized zips with no slicer installed.

Detection runs for **all** registered providers regardless of enable state. A
project's file categorisation must not change when a settings toggle flips, and
files must never silently fall back into "Model Files".

### Registry

```csharp
public interface ISlicerRegistry
{
    IReadOnlyList<ISlicerProvider> AllProviders { get; }
    IReadOnlyList<ISlicerProvider> EnabledProviders { get; }
    ISlicerProvider ActiveProvider { get; }        // null when none enabled
    ISlicerProvider GetProvider(string id);
    ISlicerProvider FindProviderForFile(string filePath);
}
```

`ActiveProvider` resolves `AppSettings.ActiveSlicerId`, falling back to the first
enabled provider when that id is unknown or points at a disabled provider, and to
`null` when nothing is enabled. Registered in `App.InitializeServices()` alongside
the existing services. `ICuraService` and `CuraService` are deleted outright; no
compatibility shim.

### Settings

`CuraManagerSettings` becomes `AppSettings`, and `CuraManagerGuiSettings` becomes
`AppGuiSettings` for consistency — both are named after the application rather
than the slicer, and renaming one without the other would be arbitrary. The flat
`Cura*` properties are replaced by:

```csharp
int SettingsVersion { get; set; }
string ActiveSlicerId { get; set; }
IDictionary<string, SlicerSettings> Slicers { get; set; }   // keyed by provider Id

// SlicerSettings : ObservableChangeTrackingObject
//   bool IsEnabled
//   string ProgramFilesPath
//   string AppDataPath
//   bool UpdateProjectsOnOpen      // ignored unless the provider supports it
```

`PrintsPath`, `Language`, `ShowWebDialogWhenAddingLink`, and `Theme` are
unchanged.

**Migration**, in `SettingsService.LoadSettings()`: when `SettingsVersion` is
absent and legacy `Cura*` keys are present, fold them into `Slicers["cura"]` with
`IsEnabled = true`, and set `ActiveSlicerId = "cura"`. The pre-migration file is
copied to `settings.json.bak` once, before the first migrated write.

**Fresh installs** start with all providers disabled, except that any provider
whose `FindInstallations()` returns a hit is auto-enabled on first run, so a new
user does not meet a disabled button with no explanation.

**Known risk:** the unsaved-changes prompt in `SettingsViewModel.OnClose` relies
on `ObservableChangeTrackingObject.HasChanges`. Whether that propagates from
nested objects is unverified. Verify against the MaSch source during
implementation; if it does not propagate, wire child `PropertyChanged` to the
parent explicitly. Do not assume either way.

### File grouping

`PrintElement`'s three hardcoded lists become two static ones plus a dynamic
collection:

```csharp
IList<PrintElementFile> ModelFiles { get; set; }
IList<PrintElementFile> OtherFiles { get; set; }
ObservableCollection<SlicerFileGroup> SlicerProjectFiles { get; set; }

// SlicerFileGroup { ISlicerProvider Provider; string Header; ObservableCollection<PrintElementFile> Files; }
```

Groups are created on first matching file and removed when they empty, so the
`ZeroToCollapsed` visibility converter is replaced by the group's absence.
`AllFiles` becomes
`SlicerProjectFiles.SelectMany(g => g.Files).Concat(ModelFiles).Concat(OtherFiles)`.

`PrintElement` resolves `ISlicerRegistry` via `ServiceContext`, matching how
`PrintElementFile` already resolves `IFileIconCache`. The static
`PrintElement.IsCuraProjectFile` is deleted; `PrintsViewModel.ExecuteOpenProjectFile`
calls `FindProviderForFile` and falls back to `ShellExecute` when nothing matches
or the owning provider is disabled.

**Locked-file fallback.** Today, when the zip cannot be read, `IsCuraProjectFile`
guesses via `MaSch.Native.Windows` `WhoIsLocking` that a process named `Cura`
implies a Cura project. The behaviour is preserved but moved behind
`IFileLockInspector`, with a Windows implementation registered in
`App.InitializeServices()` and process names declared per provider. On a future
Linux port, register a no-op and detection degrades to "unreadable → Model Files"
rather than failing to compile.

**Not doing:** caching detection results in `MetadataCache`. Detection runs in
`FillInformation()` on `Initialize()`, which only fires when a project is
selected. Caching would buy an invalidation problem for no measured win.

## Providers

### CuraSlicerProvider

Largely a lift-and-shift from `CuraService`: `FindAvailableCuraVersions`,
`GetCuraVersion`, `AreCuraPathsCorrect`, the `cura.cfg` INI patch, and
`UpdateCuraProjectConfigs` move across intact.

Deleted: `SetName4x`, `SetName5x`, `TryFindChild`, `WaitForInputIdle`, and the
`System.Windows.Automation` and `SendKeys` usages — roughly 100 lines.
`LaunchWithModels` reduces to "patch save path, start process". Because it no
longer blocks on the slicer UI, the `Task.Run(...)` wrapper around it collapses
too.

`SupportsProfileUpdateOnOpen` is `true`. `LatestSupportedVersion` stays 5.10.0.

### OrcaFamilySlicerProvider (abstract)

Owns everything shared by the Bambu lineage: installation discovery, the
lineage 3mf markers, the JSON `.conf` save-path patch, and process launching.
`SupportsProfileUpdateOnOpen` is `false`.

Subclasses supply four values:

```csharp
protected abstract string AppKey { get; }                  // data dir and <AppKey>.conf
protected abstract string[] ExecutableFileNames { get; }
protected abstract string InstallDirNameFilter { get; }
protected abstract string SliceInfoHeaderPrefix { get; }   // "X-ACNext-"
```

### AnycubicSlicerProvider

A thin subclass supplying those four values, roughly 20 lines.

It also needs an **icon**. `Resources/Geometries.xaml` currently defines
`CuraGeometry` and `CuraIcon`; those stay as Cura's own icon. Add an
`AnycubicGeometry`/`AnycubicIcon` pair alongside them, traced from the Anycubic
branding. If a usable path cannot be produced, fall back to a neutral
MaterialDesign printer icon rather than shipping a missing-resource binding —
`IconResourceKey` must always resolve.

**Every Anycubic constant is unverified**: executable filename, install directory
name, and whether the data directory is `AnycubicSlicerNext` or something else.
Read these from the real installation during implementation and record what was
found. Do not take them from the source tree — see the `X-BBL-`/`X-ACNext-`
discrepancy above.

## User interface

### Settings page

`SettingsViewModel` currently holds `AvailableVersions`,
`SelectedAvailableVersion`, `IsLoadingVersions`, and `SelectedCuraVersion` — four
properties serving one hardcoded slicer. These move to a new
`SlicerSettingsViewModel`, one instance per provider, exposed as `Slicers` and
rendered by an `ItemsControl`. Each item is a `GroupBox`:

- header: provider `DisplayName` plus icon
- `mct:Switch` (from `MaSch.Presentation.Wpf.Controls`; it derives from
  `CheckBox`, so `IsChecked`/`Content` bind as usual) labelled "Enable {0}"
- installation combobox, reload button, busy indicator — unchanged behaviour,
  now per provider; a single-installation slicer simply gets a one-entry list
- AppData and installation path textboxes, editable only when "Custom" is
  selected, as today
- both warning texts: no version detected, and version newer than supported
- the profile-update checkbox, rendered only when `SupportsProfileUpdateOnOpen`

Path fields stay editable while the slicer is disabled; disabling must not lock
the user out of configuring it. Settings save successfully with zero slicers
enabled.

The profile-update and web-dialog options stay checkboxes. Only the
enable/disable toggle becomes a switch.

While in this file: `ExecuteBrowseDirectory` currently resolves a settings
property by reflection over a magic string and throws at runtime on a typo. The
per-slicer view model gets two explicit browse commands instead, and `PrintsPath`
keeps a dedicated one. The reflection and its failure mode both go away.

### Prints page

A slicer combobox goes in `ToolbarContent` after a separator, bound to
`ActiveSlicerId`, showing icon and name, and **collapsed when fewer than two
providers are enabled**. It lists slicers only — never versions, which are
configured per slicer on the settings page.

The create-project button and its context-menu twin take the active provider's
icon and a "Create new {0} project" tooltip. Both are **disabled with an
explanatory tooltip when no slicer is enabled**.

The create dialog (`CreateCuraProjectDialog` → `CreateSlicerProjectDialog`) loses
its project-name textbox and keeps model selection with per-model amounts.

### Strings

Every `*Cura*` key in `StringTable.resx` becomes a generic key, with a `{0}`
slicer-name placeholder only where the name genuinely belongs:

| old                                        | new                                                    |
| ------------------------------------------ | ------------------------------------------------------ |
| `CuraProjectFiles`                          | `SlicerProjectFiles` → "{0} Project Files"              |
| `ToolTip_NewCuraProject`                    | `ToolTip_NewSlicerProject` → "Create new {0} project"   |
| `Title_/Prog_/Fail_/Suc_CreateCuraProject`  | `..._CreateSlicerProject`, `{0}`-formatted              |
| `CuraSettings`                              | dropped; the group header is the provider name          |
| `CuraAppDataLocation`                       | `SlicerAppDataLocation` (no `{0}`; inside the group)    |
| `CuraProgramFilesLocation`                  | `SlicerInstallLocation` (no `{0}`; inside the group)    |
| `UpdateProfilesInCuraProjects`              | `UpdateProfilesInSlicerProjects`                        |
| `Msg_CuraPathsNotConfigured`                | `Msg_SlicerPathsNotConfigured`, `{0}`-formatted         |
| `CuraVersion`, `CustomCuraVersion`, `UnsupportedCuraVersion`, `Warn_CuraVersionNotSupported`, `Warn_NoCuraVersionFound`, `LatestSupportedCuraVersion`, `SelectedCuraVersion` | generic equivalents |

New keys: `ActiveSlicer`, `EnableSlicer` ("Enable {0}"), `Msg_NoSlicerEnabled`.
Provider display names stay untranslated; they are product names.

`StringTable.de.resx` is updated in lockstep with real German translations, not
English placeholders.

**Accepted trade-off:** `Msg_CuraPathsNotConfigured` currently names `cura.cfg`
and `Cura.exe` explicitly. The generic replacement loses that detail. Acceptable
because the settings page now shows per-slicer validation inline.

`Untitled` becomes unused once the project name is gone; remove it if nothing
else references it.

## Testing

New project `tests/CuraManager.Tests`, xUnit, `net10.0-windows`, referencing
`CuraManager.csproj`.

It must set `<IsPublishable>false</IsPublishable>`. CI runs
`dotnet publish CuraManager.slnx`, which would otherwise attempt to publish the
test project.

Coverage:

- **Detection** over synthesized 3mf zips: Cura marker, `X-ACNext-` slice_info,
  `X-BBL-` slice_info, `project_settings.config` only, a plain model 3mf, and a
  non-3mf. Asserts `Exact`/`Probable`/`None` and the registry's tie-breaking.
- **Settings migration**: legacy JSON to `AppSettings`, idempotent on re-run,
  fresh-install defaults, `.bak` written once.
- **Registry resolution**: `ActiveSlicerId` unknown, or pointing at a disabled
  provider, or nothing enabled.
- **Config round-trips**: patching `cura.cfg` and the Orca `.conf` preserves every
  unrelated key. This is the highest-value test here — silently clobbering a
  user's slicer configuration is the worst failure mode in this change.

Out of scope: process launching, WPF, and anything requiring a real installation.

## Build and CI

`CuraManager.slnx` gains the test project.

`.github/workflows/build.yml` needs two changes:

1. Add `tests/**` to the `paths:` filter of both `push` and `pull_request`, or
   test-only changes will not trigger the workflow at all.
2. Add a `dotnet test` step, otherwise the new tests never run in CI.

CSharpier 1.3.0 formats XML as well as C#, and CI builds with
`TreatWarningsAsErrors`. Run `dotnet csharpier` before every commit; unformatted
XAML fails the build.

Release notes go in `docs/release-notes/next.md`, the accumulator for unreleased
changes. Do not bump `Version`; that is the maintainer's call at release time.

## Commit plan

Branch `feature/multi-slicer-support` off `main`. No pull request; the maintainer
opens it.

1. Test project scaffold and `.slnx` wiring; CI `paths:` and `dotnet test` step
2. Provider abstraction, `SlicerMatch`, registry (+tests)
3. `AppSettings` schema, migration, `.bak` (+tests)
4. Cura provider extracted; UI automation and project name removed (+tests)
5. `PrintElement` file grouping and registry detection (+tests)
6. Settings page: per-slicer view model and templated UI
7. Prints page: active-slicer combobox and string genericization (en + de)
8. Anycubic provider, verified against the real installation (+tests)
9. README and `docs/release-notes/next.md`

`dotnet build` and `dotnet test` must be green at every commit.

The README currently opens "Application to manage 3D Prints using the Ultimaker
Cura Slicer" and documents Cura-only setup; step 9 generalises it.

## Open questions for implementation

These are resolved by looking, not by guessing:

1. Does `ObservableChangeTrackingObject.HasChanges` propagate from nested
   objects? Determines whether `SlicerSettings` changes need manual wiring.
2. Anycubic executable filename, install directory name, and data directory /
   app key — read from the real installation.
3. Does the shipped Anycubic build write `app.last_export_path` at the JSON root
   or under an `app` object? The source shows both spellings in different
   overloads.
4. Anycubic's `LatestSupportedVersion` — 1.4.1.2 is the known-good version.
