using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class CatalogPage : ContentPage
{
    private readonly CatalogViewModel _viewModel;

    public CatalogPage(CatalogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.NavigateToLoginAction = async () =>
        {
            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync("//login");
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);
    }
}
