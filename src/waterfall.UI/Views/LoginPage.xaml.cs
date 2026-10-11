using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>
    /// Кожне відкриття вкладки перевіряє збережений токен (міг спливти, поки вкладка була прихована).
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RestoreSessionCommand.Execute(null);
    }
}
