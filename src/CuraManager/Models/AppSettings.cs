using System.Collections.Specialized;
using System.ComponentModel;
using MaSch.Core.Observable.Collections;
using MaSch.Presentation.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CuraManager.Models;

[ObservablePropertyDefinition]
internal interface IAppSettings_Props
{
    string PrintsPath { get; set; }
    int? Language { get; set; }
    bool ShowWebDialogWhenAddingLink { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    DefaultTheme Theme { get; set; }

    int SettingsVersion { get; set; }
    string ActiveSlicerId { get; set; }
    IDictionary<string, SlicerSettings> Slicers { get; set; }

    /// <summary>
    /// Legacy Cura UI-automation project naming. Removed in 2.0.
    /// </summary>
    bool EnableLegacyCuraProjectNaming { get; set; }
}

public partial class AppSettings : ObservableChangeTrackingObject, IAppSettings_Props
{
    public AppSettings()
    {
        _showWebDialogWhenAddingLink = true;
        _theme = DefaultTheme.Dark;

        // A plain Dictionary would not tell us when a nested SlicerSettings changes, so
        // HasChanges would never see edits made to an already-configured slicer. Route
        // every addition/replacement/removal through the two events ObservableDictionary
        // exposes: DictionaryItemChanged fires only for `dict[key] = value` (used by
        // SlicerRegistry.GetSettings), while CollectionChanged fires only for `Add`/`Remove`
        // (used when Newtonsoft.Json populates this property from disk) - together they
        // cover every way an entry can enter or leave the dictionary.
        var slicers = new ObservableDictionary<string, SlicerSettings>();
        slicers.DictionaryItemChanged += OnSlicerItemChanged;
        slicers.CollectionChanged += OnSlicersCollectionChanged;
        _slicers = slicers;
    }

    private void OnSlicersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (KeyValuePair<string, SlicerSettings> item in e.OldItems)
                UnsubscribeSlicerSettings(item.Value);
        }

        if (e.NewItems != null)
        {
            foreach (KeyValuePair<string, SlicerSettings> item in e.NewItems)
                SubscribeSlicerSettings(item.Value);
        }
    }

    private void OnSlicerItemChanged(
        object sender,
        DictionaryItemChangedEventArgs<string, SlicerSettings> e
    )
    {
        UnsubscribeSlicerSettings(e.OldValue);
        SubscribeSlicerSettings(e.NewValue);
    }

    private void SubscribeSlicerSettings(SlicerSettings settings)
    {
        if (settings != null)
            settings.PropertyChanged += OnSlicerSettingsPropertyChanged;
    }

    private void UnsubscribeSlicerSettings(SlicerSettings settings)
    {
        if (settings != null)
            settings.PropertyChanged -= OnSlicerSettingsPropertyChanged;
    }

    private void OnSlicerSettingsPropertyChanged(object sender, PropertyChangedEventArgs e) =>
        ChangeTracker.AddFixedChange();

    /// <inheritdoc />
    /// <remarks>
    /// The base implementation only resets this object's own tracker; nothing else reaches into
    /// <see cref="Slicers"/> to reset each entry's independent tracker, so without this override a
    /// <see cref="SlicerSettings"/>'s own <c>HasChanges</c> would stay <see langword="true"/>
    /// forever after its first edit.
    /// </remarks>
    public override void ResetChangeTracking()
    {
        foreach (var slicerSettings in Slicers.Values)
            slicerSettings.ResetChangeTracking();
        base.ResetChangeTracking();
    }
}
