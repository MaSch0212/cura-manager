using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace CuraManager.Models;

public class ObservableTag : ObservableObject
{
    private readonly ObservableCollection<string> _tags;

    private bool _isSet;

    public bool IsSet
    {
        get => _isSet;
        set
        {
            if (value)
                _tags.AddIfNotExists(Name);
            else
                _tags.TryRemove(Name);
        }
    }

    public string Name { get; }

    public ObservableTag(string tag, ObservableCollection<string> tags)
    {
        Name = tag;
        _tags = tags;
        _tags.CollectionChanged += Tags_CollectionChanged;
        _isSet = _tags.Contains(Name);
    }

    private void Tags_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        var set = _tags.Contains(Name);
        if (IsSet != set)
            SetProperty(ref _isSet, set, nameof(IsSet));
    }
}
