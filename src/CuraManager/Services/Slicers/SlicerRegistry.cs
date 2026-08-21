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
