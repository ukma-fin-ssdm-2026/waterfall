using System.Collections;
using System.Windows.Input;

namespace waterfall.UI.Controls;

/// <summary>
/// Shared vacancy feed list hosting a virtualized <see cref="CollectionView"/> with <see cref="VacancyCard"/> items,
/// optionally wrapped in <see cref="RefreshView"/> when pull-to-refresh fetches new server data.
/// When pull-to-refresh is disabled, the <see cref="RefreshView"/> is omitted so native overscroll stretch works on both edges.
/// </summary>
[ContentProperty(nameof(EmptyViewContent))]
public sealed class VacancyListView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(VacancyListView),
            propertyChanged: static (bindable, _, newValue) =>
                ((VacancyListView)bindable)._collectionView.ItemsSource = (IEnumerable?)newValue);

    public static readonly BindableProperty EmptyViewContentProperty =
        BindableProperty.Create(
            nameof(EmptyViewContent),
            typeof(object),
            typeof(VacancyListView),
            propertyChanged: static (bindable, _, newValue) =>
                ((VacancyListView)bindable)._collectionView.EmptyView = newValue);

    public static readonly BindableProperty EnablePullToRefreshProperty =
        BindableProperty.Create(
            nameof(EnablePullToRefresh),
            typeof(bool),
            typeof(VacancyListView),
            true,
            propertyChanged: static (bindable, _, _) => ((VacancyListView)bindable).RebuildTree());

    public static readonly BindableProperty IsRefreshingProperty =
        BindableProperty.Create(
            nameof(IsRefreshing),
            typeof(bool),
            typeof(VacancyListView),
            false,
            BindingMode.TwoWay);

    public static readonly BindableProperty RefreshCommandProperty =
        BindableProperty.Create(
            nameof(RefreshCommand),
            typeof(ICommand),
            typeof(VacancyListView));

    private readonly CollectionView _collectionView;
    private readonly RefreshView _refreshView;

    public VacancyListView()
    {
        _collectionView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
            ItemTemplate = new DataTemplate(static () => new VacancyCard())
        };

        _refreshView = new RefreshView();
        _refreshView.SetDynamicResource(RefreshView.RefreshColorProperty, "Primary");
        _refreshView.SetBinding(
            RefreshView.IsRefreshingProperty,
            static (VacancyListView v) => v.IsRefreshing,
            BindingMode.TwoWay,
            source: this);
        _refreshView.SetBinding(
            RefreshView.CommandProperty,
            static (VacancyListView v) => v.RefreshCommand,
            BindingMode.OneWay,
            source: this);

        RebuildTree();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? EmptyViewContent
    {
        get => GetValue(EmptyViewContentProperty);
        set => SetValue(EmptyViewContentProperty, value);
    }

    public bool EnablePullToRefresh
    {
        get => (bool)GetValue(EnablePullToRefreshProperty);
        set => SetValue(EnablePullToRefreshProperty, value);
    }

    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    private void RebuildTree()
    {
        if (EnablePullToRefresh)
        {
            Content = null;
            _refreshView.Content = _collectionView;
            Content = _refreshView;
        }
        else
        {
            _refreshView.Content = null;
            Content = _collectionView;
        }
    }
}
