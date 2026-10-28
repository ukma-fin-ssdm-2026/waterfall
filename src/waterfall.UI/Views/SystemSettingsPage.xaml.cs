// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

/// <summary>
/// Сторінка глобальної вкладки «Системні налаштування».
/// Дозволяє налаштовувати прапорець фонового парсингу та періодичність збору.
/// </summary>
public partial class SystemSettingsPage : ContentPage
{
    private readonly SystemSettingsViewModel _viewModel;

    public SystemSettingsPage(SystemSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
}

