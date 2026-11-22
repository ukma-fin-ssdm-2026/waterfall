using System.ComponentModel;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class DatabasePage : ContentPage, IHostedPage
{
    private readonly DatabaseViewModel _viewModel;
    private readonly OverlaySheetAnimator.SheetState _filterSheetAnimState = new();

    public DatabasePage(DatabaseViewModel viewModel)
    {
        InitializeComponent();
        viewModel.IsFilterPanelVisible = false;
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        OverflowMenuLayer.PropertyChanged += OnOverlayVisibilityPropertyChanged;
        SortMenuLayer.PropertyChanged += OnOverlayVisibilityPropertyChanged;
    }

    internal bool HasOpenOverlay =>
        _viewModel.IsFilterPanelVisible ||
        OverflowMenuLayer.IsVisible ||
        SortMenuLayer.IsVisible;

    internal event EventHandler? OverlayStateChanged;

    private void OnOverlayVisibilityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsVisible))
        {
            OverlayStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatabaseViewModel.IsFilterPanelVisible))
        {
            OverlayStateChanged?.Invoke(this, EventArgs.Empty);
            _ = AnimateSheetAsync(FilterSheetHost, "FilterSheetTemplate", _viewModel.IsFilterPanelVisible, _filterSheetAnimState);
        }
    }

    private Task AnimateSheetAsync(
        ContentView host,
        string templateKey,
        bool open,
        OverlaySheetAnimator.SheetState state) =>
        OverlaySheetAnimator.AnimateAsync(host, Resources, templateKey, _viewModel, open, state);

    internal void ToggleOverflowFromHost()
    {
        SortMenuLayer.IsVisible = false;
        OverflowMenuLayer.IsVisible = !OverflowMenuLayer.IsVisible;
    }

    internal void CloseOverflowFromHost()
    {
        OverflowMenuLayer.IsVisible = false;
        SortMenuLayer.IsVisible = false;
    }

    public void OnHostActivated() => OnAppearing();

    public bool OnHostBackPressed() => OnBackButtonPressed();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);
    }

    protected override bool OnBackButtonPressed()
    {
        if (SortMenuLayer.DismissIfOpen() || OverflowMenuLayer.DismissIfOpen())
        {
            return true;
        }

        if (_viewModel.IsFilterPanelVisible)
        {
            _viewModel.IsFilterPanelVisible = false;
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnOverflowClicked(object? sender, EventArgs e)
    {
        SortMenuLayer.IsVisible = false;
        OverflowMenuLayer.IsVisible = !OverflowMenuLayer.IsVisible;
    }

    private void OnSortMenuClicked(object? sender, EventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (!SortMenuLayer.IsVisible)
        {
            SortMenuHelper.PopulateDatabase(
                SortOptionsStack,
                _viewModel.SelectedSortOption,
                option =>
                {
                    _viewModel.SelectedSortOption = option;
                    SortMenuLayer.IsVisible = false;
                });
            SortMenuLayer.IsVisible = true;
        }
        else
        {
            SortMenuLayer.IsVisible = false;
        }
    }

    private async void OnRefreshDatabaseTapped(object? sender, TappedEventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (_viewModel.LoadDatabaseCommand.CanExecute(null))
        {
            await _viewModel.LoadDatabaseCommand.ExecuteAsync(null);
        }
    }

    private async void OnExportJsonTapped(object? sender, TappedEventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (_viewModel.ExportDatabaseCommand.CanExecute(null))
        {
            await _viewModel.ExportDatabaseCommand.ExecuteAsync(null);
        }
    }

    private async void OnImportJsonTapped(object? sender, TappedEventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (_viewModel.ImportDatabaseCommand.CanExecute(null))
        {
            await _viewModel.ImportDatabaseCommand.ExecuteAsync(null);
        }
    }

    private void OnDismissStatusBanner(object? sender, EventArgs e)
    {
        _viewModel.StatusBannerText = null;
    }

    private void OnApplyAndCloseFilterSheetClicked(object? sender, EventArgs e)
    {
        _viewModel.IsFilterPanelVisible = false;
        if (_viewModel.ApplyFiltersCommand.CanExecute(null))
        {
            _viewModel.ApplyFiltersCommand.Execute(null);
        }
    }
}
