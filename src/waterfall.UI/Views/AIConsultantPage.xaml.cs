// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Specialized;
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
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    /// <summary>
    /// Нове повідомлення (зокрема аналіз, запущений з картки каталогу) прокручуємо в поле зору.
    /// </summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _viewModel.Messages.Count == 0)
        {
            return;
        }

        object last = _viewModel.Messages[^1];
        Dispatcher.Dispatch(() => MessagesView.ScrollTo(last, position: ScrollToPosition.Start, animate: true));
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
