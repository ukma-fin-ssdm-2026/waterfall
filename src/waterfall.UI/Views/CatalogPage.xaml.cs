using System.ComponentModel;
using waterfall.UI.Resources;
using waterfall.UI.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class CatalogPage : ContentPage, IHostedPage
{
    private readonly CatalogViewModel _viewModel;
    private readonly OverlaySheetAnimator.SheetState _parseSheetAnimState = new();
    private readonly OverlaySheetAnimator.SheetState _localFilterSheetAnimState = new();
    private bool _sheetsPrewarmed;

    public CatalogPage(CatalogViewModel viewModel)
    {
        InitializeComponent();
        ParseSitePreferences.Attach(viewModel);
        // На першому відкритті тримаємо overlay-шторки закритими,
        // не змінюючи значення за замовчуванням у ViewModel (щоб тести PR #186 лишалися зеленими).
        viewModel.IsParsePanelVisible = false;
        viewModel.IsLocalFilterPanelVisible = false;
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Pager.PropertyChanged += OnPagerPropertyChanged;
        OverflowMenuLayer.PropertyChanged += OnOverlayVisibilityPropertyChanged;
        SortMenuLayer.PropertyChanged += OnOverlayVisibilityPropertyChanged;
        _viewModel.NavigateToLoginAction = async () =>
        {
            IAppNavigationHost? host = (IAppNavigationHost?)DesktopHostPage.Current ?? PhoneHostPage.Current;
            if (host is not null)
            {
                await host.NavigateToAsync("login");
            }
        };
    }

    internal bool HasOpenOverlay =>
        _viewModel.IsParsePanelVisible ||
        _viewModel.IsLocalFilterPanelVisible ||
        OverflowMenuLayer.IsVisible ||
        SortMenuLayer.IsVisible;

    internal event EventHandler? OverlayStateChanged;

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

    private void OnOverlayVisibilityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsVisible))
        {
            OverlayStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogViewModel.IsParsePanelVisible))
        {
            OverlayStateChanged?.Invoke(this, EventArgs.Empty);
            _ = AnimateSheetAsync(ParseSheetHost, "ParseSheetTemplate", _viewModel.IsParsePanelVisible, _parseSheetAnimState);
        }
        else if (e.PropertyName == nameof(CatalogViewModel.IsLocalFilterPanelVisible))
        {
            OverlayStateChanged?.Invoke(this, EventArgs.Empty);
            _ = AnimateSheetAsync(LocalFilterSheetHost, "LocalFilterSheetTemplate", _viewModel.IsLocalFilterPanelVisible, _localFilterSheetAnimState);
        }
    }

    private void OnPagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VacancyPager.CurrentPage) or nameof(VacancyPager.PageSize))
        {
            CatalogListView.ScrollToTop();
        }
    }

    private Task AnimateSheetAsync(
        ContentView host,
        string templateKey,
        bool open,
        OverlaySheetAnimator.SheetState state) =>
        OverlaySheetAnimator.AnimateAsync(host, Resources, templateKey, _viewModel, open, state);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);

        if (_sheetsPrewarmed)
        {
            return;
        }

        _sheetsPrewarmed = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
        {
            OverlaySheetAnimator.Prewarm(ParseSheetHost, Resources, "ParseSheetTemplate", _viewModel);
            OverlaySheetAnimator.Prewarm(LocalFilterSheetHost, Resources, "LocalFilterSheetTemplate", _viewModel);
        });
    }

    protected override bool OnBackButtonPressed()
    {
        if (SortMenuLayer.DismissIfOpen() || OverflowMenuLayer.DismissIfOpen())
        {
            return true;
        }

        if (_viewModel.IsParsePanelVisible)
        {
            _viewModel.IsParsePanelVisible = false;
            return true;
        }

        if (_viewModel.IsLocalFilterPanelVisible)
        {
            _viewModel.IsLocalFilterPanelVisible = false;
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnDismissStatusBanner(object? sender, EventArgs e)
    {
        _viewModel.StaleCacheText = null;
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
            SortMenuHelper.PopulateCatalog(
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

    private async void OnExportJsonTapped(object? sender, TappedEventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (_viewModel.ExportVacanciesCommand.CanExecute(null))
        {
            await _viewModel.ExportVacanciesCommand.ExecuteAsync(null);
        }
    }

    private async void OnImportJsonTapped(object? sender, TappedEventArgs e)
    {
        OverflowMenuLayer.IsVisible = false;
        if (_viewModel.ImportVacanciesCommand.CanExecute(null))
        {
            await _viewModel.ImportVacanciesCommand.ExecuteAsync(null);
        }
    }

    private void OnToggleWorkUaSpheresClicked(object? sender, EventArgs e) => ToggleWorkUaSpheres();

    private void OnToggleWorkUaSpheresTapped(object? sender, TappedEventArgs e) => ToggleWorkUaSpheres();

    private void ToggleWorkUaSpheres()
    {
        FlexLayout? spheresPanel = ParseSheetHost.Content?.FindByName<FlexLayout>("WorkUaSpheresPanel");
        Button? spheresChevron = ParseSheetHost.Content?.FindByName<Button>("WorkUaSpheresChevron");
        if (spheresPanel is null)
        {
            return;
        }

        spheresPanel.IsVisible = !spheresPanel.IsVisible;
        if (spheresChevron is not null)
        {
            spheresChevron.Text = spheresPanel.IsVisible ? Icons.ChevronUp : Icons.ChevronDown;
        }
    }

    private void OnFetchAndCloseParseSheetClicked(object? sender, EventArgs e)
    {
        _viewModel.IsParsePanelVisible = false;
        if (_viewModel.FetchFromSitesCommand.CanExecute(null))
        {
            _viewModel.FetchFromSitesCommand.Execute(null);
        }
    }

    private void OnApplyLocalFiltersClicked(object? sender, EventArgs e)
    {
        _viewModel.IsLocalFilterPanelVisible = false;
        if (_viewModel.FilterExistingCommand.CanExecute(null))
        {
            _viewModel.FilterExistingCommand.Execute(null);
        }
    }

    private async void OnGoToDatabaseClicked(object? sender, EventArgs e)
    {
        IAppNavigationHost? host = (IAppNavigationHost?)DesktopHostPage.Current ?? PhoneHostPage.Current;
        if (host is not null)
        {
            await host.NavigateToAsync("database");
        }
    }
}
