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
  adding new WPF/Win32 coupling and isolates some existing coupling, but performs
  no port work.
- Removing the legacy Cura UI-automation naming. It is quarantined and made
  opt-out here; removal is 2.0 work.
- Bumping `Version`. 1.8.0 is released from another branch.

## Constraints

- **No breaking changes.** The next release is 1.8.0, a minor. Existing users
  must keep working behaviour after upgrading. Anything that removes behaviour
  waits for 2.0.
- **UI automation is quarantined, not deleted.** The Cura `System.Windows.Automation`
  + `SendKeys` project-naming code moves into its own assembly,
  `CuraManager.Legacy.CuraAutomation`, behind a runtime toggle that defaults on
  for upgrading users and off for new ones. Removing it in 2.0 becomes: delete
  the project, drop one `ProjectReference`, delete two guarded call sites.
- Aside from that one assembly, provider logic stays free of WPF and Win32
  except behind an interface with a swappable implementation. The Avalonia port
  drops the reference rather than untangling code — which is why quarantining
  serves the port better than deleting would have.

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
    void LaunchWithModels(SlicerSettings s, SlicerLaunchRequest request);
    void OpenProject(SlicerSettings s, string projectFilePath);
}

public record SlicerLaunchRequest(
    string ProjectDirectory,
    IReadOnlyList<string> ModelFiles,
    string ProjectName);      // null unless legacy Cura naming is active

public record SlicerInstallation(
    Version Version,
    string DisplayName,
    string ProgramFilesPath,
    string AppDataPath,
    bool IsSupported);
```

`SlicerInstallation` replaces `Models/CuraVersion.cs`.

`SlicerLaunchRequest` exists so the legacy-only `ProjectName` does not become a
fourth positional parameter that every provider but one ignores. Every provider
except Cura-with-legacy-enabled ignores it regardless, but the record keeps that
visible and gives future launch options somewhere to go. When legacy naming is
removed in 2.0, drop the property — not the record.

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
bool EnableLegacyCuraProjectNaming { get; set; }            // legacy; removed in 2.0

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

**`EnableLegacyCuraProjectNaming` defaults by presence of the settings file**,
decided once at the same point in `LoadSettings()`:

| situation                                   | value                  |
| ------------------------------------------- | ---------------------- |
| settings file absent (new user)              | `false`                |
| settings file present, unmigrated (upgrade)  | `true`                 |
| settings file present, already migrated      | whatever is stored     |

So upgrading users keep today's behaviour and new users start on the new one.
Because the decision is written into the migrated file, it is made exactly once
and is thereafter an ordinary user-editable setting — a later run must never
re-derive it.

Note that `SettingsVersion` is what distinguishes "unmigrated" from "already
migrated"; a migrated file that happens to have the flag `false` must not be
flipped back to `true`.

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

`SetName4x`, `SetName5x`, and `TryFindChild` move out to the legacy assembly (see
below). `LaunchWithModels` becomes "patch save path, start process", then — only
when legacy naming is enabled — `WaitForInputIdle` and a call into the legacy
automation. With the toggle off it does not block on the slicer UI at all.

`SupportsProfileUpdateOnOpen` is `true`. `LatestSupportedVersion` stays 5.10.0.

### CuraManager.Legacy.CuraAutomation

New project `src/CuraManager.Legacy.CuraAutomation/`, `net10.0-windows`,
`UseWindowsForms=true` (for `SendKeys`) plus the UIAutomation references. It
contains `SetName4x`, `SetName5x`, and `TryFindChild`, and nothing else.

`IsPublishable` is `false`, matching the test project — CI runs
`dotnet publish CuraManager.slnx` and should not treat a library as a publish
entry point. The DLL still ships, because it flows into `CuraManager`'s publish
output through the normal `ProjectReference`.

**Dependency direction: `CuraManager` → legacy, never the reverse.** The
interface lives in the legacy assembly and is expressed only in BCL types, so the
legacy project knows nothing about the application:

```csharp
namespace CuraManager.Legacy.CuraAutomation;

public interface ICuraProjectNameAutomation
{
    void SetProjectName(Process curaProcess, string executableFileName, string projectName);
}
```

`executableFileName` preserves the existing `Cura` (4.x) versus everything-else
(5.x) branch. A plain `ProjectReference` from the main app, registered in
`App.InitializeServices()` like any other service.

Rejected alternative: defining the interface in the main app and loading the
legacy assembly by reflection. That buys nothing here — a direct reference is
already trivial to delete — and costs a load-failure path that has to be handled
at runtime.

**Removal in 2.0** is then mechanical: delete the project directory, remove it
from `CuraManager.slnx` and the `ProjectReference`, delete the setting and its
"Legacy Features" group, and delete the two guarded call sites in
`CuraSlicerProvider.LaunchWithModels` and the create dialog.

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

The create dialog (`CreateCuraProjectDialog` → `CreateSlicerProjectDialog`) keeps
model selection with per-model amounts. Its project-name textbox becomes
conditional: shown, editable, and prefilled with the element name exactly as
today when legacy naming is enabled **and** the active provider is Cura; hidden
otherwise. "No breaking changes" means an upgrading user sees the dialog
unchanged, editable name field included.

With the toggle off, or for any non-Cura provider, the name has nowhere to go —
no Orca-family slicer exposes a name field or a name CLI argument — so showing
the field would be a lie.

### Legacy Features section

A `GroupBox` headed "Legacy Features" sits at the **very bottom** of the settings
page, below every slicer group, containing one `mct:Switch` bound to
`EnableLegacyCuraProjectNaming`.

Its label names Cura explicitly — something like "Set the project name in Cura
automatically after launching it" — plus a short note that the feature is
Windows-only, relies on UI automation that can break with new Cura releases, and
will be removed in 2.0. The toggle has no effect on non-Cura providers, and the
label must make that obvious rather than leaving the user to discover it.

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

New keys: `ActiveSlicer`, `EnableSlicer` ("Enable {0}"), `Msg_NoSlicerEnabled`,
`LegacyFeatures`, `EnableLegacyCuraProjectNaming`, and
`Desc_LegacyCuraProjectNaming` for the explanatory note. Provider display names
stay untranslated; they are product names.

`StringTable.de.resx` is updated in lockstep with real German translations, not
English placeholders.

**Accepted trade-off:** `Msg_CuraPathsNotConfigured` currently names `cura.cfg`
and `Cura.exe` explicitly. The generic replacement loses that detail. Acceptable
because the settings page now shows per-slicer validation inline.

`Untitled` stays — the create dialog still uses it to prefill the name field in
legacy mode. It becomes removable in 2.0.

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
- **Legacy-toggle default**, all three rows of the table above, including the
  one that actually bites: an already-migrated file storing `false` must survive
  a reload unchanged.
- **Registry resolution**: `ActiveSlicerId` unknown, or pointing at a disabled
  provider, or nothing enabled.
- **Config round-trips**: patching `cura.cfg` and the Orca `.conf` preserves every
  unrelated key. This is the highest-value test here — silently clobbering a
  user's slicer configuration is the worst failure mode in this change.

Out of scope: process launching, WPF, and anything requiring a real installation.

## Build and CI

`CuraManager.slnx` gains both new projects: `CuraManager.Legacy.CuraAutomation`
and the test project. Both set `IsPublishable=false`.

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
3. `AppSettings` schema, migration, `.bak`, legacy-toggle default (+tests)
4. `CuraManager.Legacy.CuraAutomation` project; automation moved out of
   `CuraService` behind `ICuraProjectNameAutomation`
5. Cura provider extracted, legacy call site guarded by the toggle (+tests)
6. `PrintElement` file grouping and registry detection (+tests)
7. Settings page: per-slicer view model, templated UI, Legacy Features group
8. Prints page: active-slicer combobox, conditional project-name field, string
   genericization (en + de)
9. Anycubic provider, verified against the real installation (+tests)
10. README and `docs/release-notes/next.md`

Step 4 is deliberately a pure move with no behaviour change, so that step 5's
diff shows only the guard rather than mixing relocation with logic changes.

No version bump on this branch; 1.8.0 is tagged elsewhere.

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
