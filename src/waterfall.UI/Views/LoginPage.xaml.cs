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
    /// Відкриття вкладки профілю (сесія не відновлюється автоматично, користувач входить явно).
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
    }
}
