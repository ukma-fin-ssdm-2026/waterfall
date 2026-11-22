using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Waterfall.Client.Collections;

/// <summary>
/// ObservableCollection with single-notification batch replacement so filtering/sorting
/// 100+ items triggers one UI layout pass instead of N incremental passes.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
