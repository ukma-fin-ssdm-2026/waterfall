using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class AIConsultantPage : ContentPage
{
    public AIConsultantPage(AIConsultantViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
