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
