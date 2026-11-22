using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class FavoritesPage : ContentPage, IHostedPage
{
    private readonly FavoritesViewModel _viewModel;

    public FavoritesPage(FavoritesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void OnHostActivated() => OnAppearing();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);
    }
}
