using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class RecommendationsPage : ContentPage
{
    private readonly RecommendationsViewModel _viewModel;

    public RecommendationsPage(RecommendationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadRecommendationsCommand.Execute(null);
    }
}
