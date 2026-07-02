using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CanfarDesktop.Helpers;

/// <summary>
/// ObservableCollection with a single-notification ReplaceAll, so repopulating a
/// bound list raises one Reset instead of a Clear plus one event per item
/// (which makes ListViews rebuild containers and lose scroll position repeatedly).
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
