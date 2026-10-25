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
        if (!_viewModel.HasLoaded && !_viewModel.IsLoading)
        {
            _viewModel.LoadRecommendationsCommand.Execute(null);
        }
    }
}
