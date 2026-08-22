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
            Browse(Settings.AppDataPath, DefaultAppDataRoot, path => Settings.AppDataPath = path)
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

        // Matching in ReloadInstallationsAsync is case-insensitive, so a stored path that only
        // differs from the detected one by casing is still "the same" installation. Assigning
        // unconditionally would still raise PropertyChanged for that no-op (SlicerSettings'
        // change tracking compares ordinally), arming AppSettings.HasChanges - and therefore the
        // unsaved-changes prompt - the moment the page opens, with no actual user edit.
        if (
            !string.Equals(
                Settings.AppDataPath,
                value.AppDataPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
            Settings.AppDataPath = value.AppDataPath;
        if (
            !string.Equals(
                Settings.ProgramFilesPath,
                value.ProgramFilesPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
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
