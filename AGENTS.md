# CuraManager — agent guide

Windows desktop app for organising 3D print projects. Each project is a folder on disk holding
model files, slicer project files and a `metadata.json`; the app indexes those folders and hands
them to a slicer (UltiMaker Cura, Anycubic Slicer Next, OrcaSlicer).

WPF on .NET 10, C#, MVVM. Windows-only — `net10.0-windows`, `SupportedOSPlatform=windows`.

## Layout

| Path | What it is |
| ---- | ---------- |
| `src/CuraManager/` | The application. Everything below lives here unless stated otherwise. |
| `src/CuraManager.Legacy.CuraAutomation/` | Cura UI-automation project naming. Legacy, removed in 2.0; a separate assembly so deleting it is a directory plus a `ProjectReference`. |
| `tests/CuraManager.Tests/` | xUnit tests. |
| `docs/release-notes/` | One file per release plus `next.md`. See [Releases](#releases). |
| `docs/superpowers/` | Historical design docs and plans. Not maintained; read for background only. |
| `CuraManager.slnx` | Solution (XML `slnx` format, not `.sln`). |

Inside `src/CuraManager/`:

- `Models/` — `PrintElement` (one project), `PrintElementMetadata` (what `metadata.json` holds),
  `AppSettings`, `SlicerSettings`, …
- `Services/` — `PrintsService`, `SettingsService`, `CachingService`, `DownloadService`, …
  Interfaces live in `Services/_Interfaces/`.
- `Services/Slicers/` — one provider per slicer plus `SlicerRegistry`.
- `Services/WebProviders/` — one provider per model site.
- `ViewModels/Main/`, `Views/`, `Views/Main/` — pages and dialogs.
- `Resources/StringTable*.resx` — translations. See [Translations](#translations).

## Commands

```bash
dotnet build CuraManager.slnx
```

```bash
dotnet test CuraManager.slnx
```

CI runs the stricter form, and this is what to run before claiming a change is green:

```bash
dotnet test CuraManager.slnx -c Release -p:TreatWarningsAsErrors=true
```

Formatting is CSharpier, restored as a local tool (`dotnet tool restore` once):

```bash
dotnet csharpier format src/CuraManager/Views/SomeFile.xaml.cs
```

## Architecture

**Service locator, not a DI container.** `App.InitializeServices` news everything up and registers
it with `ServiceContext.AddService<T>`. Consumers call `ServiceContext.GetService<T>()` or
`ServiceContext.GetService(out _field)`. Dialogs resolve their own services in their constructors;
they are not injected.

**Slicers** implement `ISlicerProvider` (`Id`, `DisplayName`, install discovery, version detection,
`IsProjectFile`, `LaunchWithModels`, `OpenProject`). `SlicerRegistry` owns the set, which slicer is
active, and per-slicer settings. Anycubic Slicer Next and OrcaSlicer share `OrcaFamilySlicerProvider`.
Microsoft Store installs are found through `IMsixPackageLocator` — `Program Files\WindowsApps` cannot
be enumerated by a normal user, so plain directory scanning will never see them.

**Web providers** implement `IWebProvider` and are composed into `DownloadService`. Thingiverse and
YouMagine are wired up; MyMiniFactory is commented out in `App.InitializeServices` because it now
requires a login for direct download links. These sites change without notice — verify before
assuming a provider still works.

**A project is a directory.** `PrintsService.GetNewPrintElements` enumerates *directories* under the
configured prints path — never files — so a project with no files at all is valid and listed.
`PrintElement` watches its own directory with a `FileSystemWatcher` and sorts files into slicer
project files / 3D models / other via `PrintElement.CategorizeByExtension`.

**Persistence.** Per-project state is `metadata.json` inside the project folder (archived flag,
website URL, tags). A `metadata-cache.json` sits in the prints root. App settings live in
`%APPDATA%\MaSch\CuraManager\` (`settings.json`, `settings.gui.json`, and a one-time
`settings.json.bak` written by `SettingsMigration`).

## Conventions that will bite you

### MaSch source generators

The `MaSch.*` packages are the author's own libraries and do a lot of the work.

Observable properties are declared as an interface, not as fields:

```csharp
[ObservablePropertyDefinition]
internal interface IMyDialog_Props
{
    string ProjectName { get; set; }
}

public partial class MyDialog : IMyDialog_Props { }
```

The generator emits the property, the backing field and a `partial void OnProjectNameChanged(string
previous, string value)` hook you may implement. `[DependsOn(nameof(X))]` on a computed property
re-raises it when `X` changes.

**The change hook fires even when the value did not change.** A two-way `TextBox` binding re-pushes
identical text on things like caret movement, so `previous` and `value` can be equal. Guard on
`!string.Equals(previous, value, StringComparison.Ordinal)` whenever the handler does something
destructive — this caused a real bug where a list rebuilt itself mid-keyboard-navigation.

### Translations

Every user-visible string goes through `ITranslationManager` (`{m:Translation Key}` in XAML,
`_translationManager.GetTranslation(nameof(StringTable.Key))` in code). Adding or removing one means
touching **three** files:

1. `Resources/StringTable.resx` (English)
2. `Resources/StringTable.de.resx` (German — the app ships both)
3. `Resources/StringTable.Designer.cs`

The designer file is generated by Visual Studio's `ResXFileCodeGenerator`, which does not run on
`dotnet build`. **Edit it by hand**, keeping properties in the same case-insensitive alphabetical
order the generator uses. After editing, check that the three files agree — the keys in the designer
must exactly match the keys in the resx, and English and German must have the same key set.

Delete strings that lose their last consumer; several already have.

### Formatting

CSharpier formats **both C# and XAML**. `CSharpier_UnformattedAsWarnings` is on in every project, so
unformatted code is a warning, and CI builds with `TreatWarningsAsErrors` — badly formatted code
fails the build.

Format only the files you touched. `dotnet csharpier format .` reformats the whole repo, and the CLI
and the MSBuild integration currently disagree about one pre-existing file
(`Services/Slicers/CuraSlicerProvider.cs`), so a repo-wide run adds unrelated diff noise.

### File encodings

Not uniform, and git will show a whole-file diff if you get it wrong:

- `.xaml`, `.resx`, and the generated `StringTable.Designer.cs` — UTF-8 **with** BOM.
- Hand-written `.cs`, `.csproj`, `.md` — UTF-8 **without** BOM.

There is no `.gitattributes`; line endings are normalised to LF in the index by `core.autocrlf`.

### Theming

Never hard-code colours. Use `{m:ThemeValue Key=…}`, and pick the key that matches the role:

- `NormalForegroundBrush` is the **text** colour. Using it for a border draws a stark white box.
- `NormalBorderBrush` is the border colour.
- For anything dropdown-shaped, use the ComboBox keys — `ComboBoxPopupBackgroundBrush`,
  `ComboBoxPopupBorderBrush`, `ComboBoxPopupBorderThickness`, `ComboBoxPopupCornerRadius`, and the
  `ComboBoxItem*` normal/hover/selected/selected-hover pairs — so custom popups match the real ones.

`m:Theming.ThemeOverrides` recolours a themed control locally (the red delete button does this).

Material Design icon names come from the `MaterialDesignIconCode` enum; a wrong name compiles in
XAML and fails at runtime, so verify the member exists rather than guessing.

### Verify UI changes by running the app

The build succeeding says nothing about whether a dialog looks right or a control behaves. WPF has no
automated UI coverage here. Launch the app and drive it.

Building while the app is running fails on a locked `bin/Debug/CuraManager.exe`; build to a scratch
`-p:OutputPath=…` instead of killing the user's instance.

The app reads its prints path from `%APPDATA%\MaSch\CuraManager\settings.json`, which points at the
user's real 3D print library. Do not create, rename or delete projects there while testing — point
the setting at a scratch folder and restore it afterwards.

## Releases

### Versioning

The version lives in `src/CuraManager/CuraManager.csproj` (`<Version>`) and is always three parts.

| Bump | When |
| ---- | ---- |
| **Major** | Breaking changes, major UI changes, or other major changes. A .NET update is *not* a breaking change for this purpose. |
| **Minor** | .NET updates and big new features. |
| **Patch** | Bugfixes, and small to medium sized features. |
| **Revision** | Never. The fourth component is not used. |

(`ISlicerProvider.LatestSupportedVersion` is a four-part `Version` for an unrelated reason — an
omitted revision is `-1` and would compare lower than a detected `x.y.z.0`. That is slicer version
detection, not the app version.)

### Release notes

`docs/release-notes/next.md` accumulates a row per change as it merges. Each row records the type
(`feature`, `bugfix`, `tech`), the user-facing description, and a link to the pull request it came
from — a commit link when there was no pull request.

Use a full markdown link (`[#5](https://github.com/MaSch0212/cura-manager/pull/5)`), not a bare
`#5`. The file is rendered both as a file in the repository and verbatim as the GitHub release body,
and a bare reference only autolinks in the second.

The `[comment]` lines at the bottom are link-reference definitions. Their text must not contain
parentheses — a nested `(...)` closes the title early and leaks the rest into the rendered page.

On a version bump: move the accumulated `next.md` content into `docs/release-notes/v<Version>.md`,
reset `next.md` to the empty template, and change `<Version>` — **all in the same commit**. CI's
*Check release notes* step fails if `v<Version>.md` does not exist for the version in the csproj.

### CI

`.github/workflows/build.yml` restores with `--locked-mode`, reads the version via
`dotnet msbuild -getProperty:Version`, checks the release-notes file exists, runs tests and publishes
`win-x64` — all with `TreatWarningsAsErrors`. Pushing a tag creates the GitHub release using
`v<Version>.md` as the body.

It triggers only on `.github/workflows/build.yml`, `.editorconfig`, `CuraManager.slnx`,
`Directory.Build.props`, `Directory.Packages.props`, `docs/release-notes/**`, `src/**` and `tests/**`.
A change outside those paths runs no checks.

### Dependencies

Central Package Management: versions go in `Directory.Packages.props`, never in a csproj. Lockfiles
(`packages.lock.json`) are committed and CI restores with `--locked-mode`, so a dependency change
must include the regenerated lockfiles.

The `Debug_MaSchLocal` configuration swaps the `MaSch.*` packages for `ProjectReference`s into a
local checkout via the `MASCH_SOURCES` environment variable, and uses its own gitignored lockfile so
it cannot rewrite the committed one. Ignore it unless working on the MaSch libraries themselves.

## Contributing

Branch from `main`, one branch per change, and open a pull request — `main` is the release branch.
Commit messages are conventional-commit style (`feat:`, `fix:`, `chore:`).
