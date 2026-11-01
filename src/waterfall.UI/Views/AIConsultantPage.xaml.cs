// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class AIConsultantPage : ContentPage
{
    private readonly AIConsultantViewModel _viewModel;

    public AIConsultantPage(AIConsultantViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Вихід зі вкладки зупиняє незавершений SSE-потік: відповідь, яку ніхто не бачить, не варто тримати.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CancelActiveStream();
    }
}
