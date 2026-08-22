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

    /// <summary>
    /// Settings for a provider, created on demand so callers never see null. Reads a fresh
    /// <see cref="AppSettings"/> from the settings service, so an entry this creates belongs to
    /// an object the caller does not own and will not be saved; use the two-argument overload
    /// whenever the returned object will be mutated and saved.
    /// </summary>
    SlicerSettings GetSettings(ISlicerProvider provider);

    /// <summary>
    /// Settings for a provider within a caller-supplied <see cref="AppSettings"/>, created on
    /// demand so callers never see null. Use this overload whenever the returned object will be
    /// mutated and saved — the parameterless overload reads a fresh instance from the settings
    /// service, so entries it creates belong to an object the caller does not own and will not save.
    /// </summary>
    SlicerSettings GetSettings(AppSettings settings, ISlicerProvider provider);

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
