using System.ComponentModel;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class LoginPage : ContentPage, IHostedPage
{
    private readonly LoginViewModel _viewModel;
    private WebView? _mapWebView;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateMapSource();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.MapHtml) ||
            e.PropertyName == nameof(LoginViewModel.HasSelectedCoordinates))
        {
            UpdateMapSource();
        }
    }

    private void UpdateMapSource()
    {
        if (_viewModel.HasSelectedCoordinates && !string.IsNullOrEmpty(_viewModel.MapHtml))
        {
            if (_mapWebView is null)
            {
                _mapWebView = new WebView
                {
                    HeightRequest = 220,
                    HorizontalOptions = LayoutOptions.Fill
                };
                MapWebViewHost.Content = _mapWebView;
            }

            _mapWebView.Source = new HtmlWebViewSource { Html = _viewModel.MapHtml };
        }
        else
        {
            ReleaseMapWebView();
        }
    }

    private void ReleaseMapWebView()
    {
        if (_mapWebView is not null)
        {
            MapWebViewHost.Content = null;
            _mapWebView = null;
        }
    }

    public void OnHostActivated() => OnAppearing();

    public void OnHostDeactivated() => ReleaseMapWebView();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateMapSource();
        if (_viewModel.IsLoggedIn && _viewModel.LoadPersonalStatsCommand.CanExecute(null))
        {
            _viewModel.LoadPersonalStatsCommand.Execute(null);
        }
    }

    protected override void OnDisappearing()
    {
        ReleaseMapWebView();
        base.OnDisappearing();
    }
}
