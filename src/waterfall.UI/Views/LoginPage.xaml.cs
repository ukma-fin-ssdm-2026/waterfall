using System.ComponentModel;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateMapSource();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.MapHtml))
        {
            UpdateMapSource();
        }
    }

    private void UpdateMapSource()
    {
        if (!string.IsNullOrEmpty(_viewModel.MapHtml))
        {
            MapWebView.Source = new HtmlWebViewSource { Html = _viewModel.MapHtml };
        }
    }

    /// <summary>
    /// Відкриття вкладки профілю (сесія не відновлюється автоматично, користувач входить явно).
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateMapSource();
    }
}
