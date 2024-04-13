using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Data;
using CuraManager.Models;

namespace CuraManager.Converter;

public class TagsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2)
            return null;

        if (
            values[0] is not ObservableCollection<string> availableTags
            || values[1] is not ObservableCollection<string> tags
        )
            return null;

        var result = new ObservableCollection<ObservableTag>(
            availableTags.Select(x => new ObservableTag(x, tags))
        );
        availableTags.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                result.Clear();
            }
            else
            {
                if (e.OldItems != null)
                    result.RemoveWhere(x => e.OldItems.Contains(x));
                if (e.NewItems != null)
                    result.Add(e.NewItems.OfType<string>().Select(x => new ObservableTag(x, tags)));
            }
        };
        return result;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
