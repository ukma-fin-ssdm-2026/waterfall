using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class RecommendationsPage : ContentPage, IHostedPage
{
    private readonly RecommendationsViewModel _viewModel;

    public RecommendationsPage(RecommendationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    internal void HideStandalonePhoneHeader() => PhoneHeaderBar.IsVisible = false;

    public void OnHostActivated() => OnAppearing();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);
    }
}
