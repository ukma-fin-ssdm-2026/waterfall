// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class DatabasePage : ContentPage
{
    private readonly DatabaseViewModel _viewModel;

    public DatabasePage(DatabaseViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.EnsureLoadedCommand.Execute(null);
    }
}
