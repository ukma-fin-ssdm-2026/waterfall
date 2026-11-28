// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Координатор режиму вибору 1–3 вакансій для групового ШІ-аналізу (FR-010).
/// Спільний для вкладок «Каталог» та «База вакансій»: зберігає вибір між перегортанням сторінок,
/// синхронізує видимі картки та скидає режим при запуску аналізу або виході з акаунта.
/// </summary>
public sealed partial class VacancySelectionCoordinator : ObservableObject, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly Func<bool> _canUseAi;
    private readonly Func<IEnumerable<VacancyItemViewModel>> _getVisibleItems;
    private bool _isExiting;

    public VacancySelectionCoordinator(
        IMessenger messenger,
        Func<bool> canUseAi,
        Func<IEnumerable<VacancyItemViewModel>> getVisibleItems)
    {
        _messenger = messenger;
        _canUseAi = canUseAi;
        _getVisibleItems = getVisibleItems;

        State.Changed += OnSelectionStateChanged;

        messenger.Register<VacancySelectionCoordinator, AnalyzeVacancyRequestedMessage>(this, static (coordinator, _) =>
        {
            if (coordinator.IsSelectionMode)
            {
                coordinator.Exit(clearSelection: true);
            }
        });
        messenger.Register<VacancySelectionCoordinator, AnalyzeVacanciesRequestedMessage>(this, static (coordinator, _) =>
        {
            if (coordinator.IsSelectionMode)
            {
                coordinator.Exit(clearSelection: true);
            }
        });
    }

    public VacancySelectionState State { get; } = new();

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    public int SelectedCount => State.Count;

    public string SelectionTrayText => $"Обрано {State.Count} з {VacancySelectionState.MaxSelected}";

    public bool HasSelection => State.Count > 0;

    partial void OnIsSelectionModeChanged(bool value)
    {
        AnalyzeSelectionCommand.NotifyCanExecuteChanged();
        ToggleSelectionModeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        if (!_canUseAi())
        {
            return;
        }

        if (IsSelectionMode)
        {
            Exit(clearSelection: true);
            return;
        }

        IsSelectionMode = true;
        foreach (VacancyItemViewModel item in _getVisibleItems())
        {
            item.IsSelectionModeActive = true;
        }

        NotifySelectionChanged();
    }

    [RelayCommand]
    private void CancelSelectionMode() => Exit(clearSelection: true);

    [RelayCommand(CanExecute = nameof(CanAnalyzeSelection))]
    private void AnalyzeSelection()
    {
        IReadOnlyList<VacancyDto> batch = State.Snapshot();
        if (batch.Count == 0)
        {
            return;
        }

        Exit(clearSelection: true);
        _messenger.Send(new AnalyzeVacanciesRequestedMessage(batch));
    }

    private bool CanAnalyzeSelection() => HasSelection;

    public void ApplyToCard(VacancyItemViewModel item) => item.IsSelectionModeActive = IsSelectionMode;

    public void Reset() => Exit(clearSelection: true);

    public void Dispose()
    {
        State.Changed -= OnSelectionStateChanged;
        _messenger.UnregisterAll(this);
    }

    private void Exit(bool clearSelection)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        try
        {
            if (clearSelection)
            {
                State.Clear();
            }

            IsSelectionMode = false;
            foreach (VacancyItemViewModel item in _getVisibleItems())
            {
                item.IsSelectionModeActive = false;
            }

            NotifySelectionChanged();
        }
        finally
        {
            _isExiting = false;
        }
    }

    private void OnSelectionStateChanged() => NotifySelectionChanged();

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionTrayText));
        OnPropertyChanged(nameof(HasSelection));
        AnalyzeSelectionCommand.NotifyCanExecuteChanged();
    }
}
