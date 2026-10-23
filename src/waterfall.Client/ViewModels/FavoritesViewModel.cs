// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.SavedVacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Обране»: збережені вакансії, найновіші зверху.
/// Перезавантажується при кожному відкритті вкладки (нові збереження з каталогу);
/// прибрана з «Обраного» вакансія (тут чи в каталозі) зникає зі списку одразу.
/// </summary>
public partial class FavoritesViewModel : ObservableObject
{
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private CancellationTokenSource? _loadCts;

    public FavoritesViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;

        messenger.Register<FavoritesViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            if (message.IsSaved)
            {
                return; // Додані в каталозі з'являться при наступному відкритті вкладки.
            }

            foreach (VacancyItemViewModel item in vm.Vacancies.Where(v => v.Id == message.VacancyId).ToList())
            {
                vm.Vacancies.Remove(item);
            }

            vm.OnCountChanged();
        });
    }

    public ObservableCollection<VacancyItemViewModel> Vacancies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Список завантажено, але збережених вакансій немає.
    /// </summary>
    public bool IsEmpty => HasLoaded && !IsLoading && ErrorMessage is null && Vacancies.Count == 0;

    public bool ShowTotal => HasLoaded && !HasError && Vacancies.Count > 0;

    public string TotalText => $"Збережено вакансій: {Vacancies.Count}";

    /// <summary>
    /// Відкриття вкладки / «Повторити»: завантажити актуальний список.
    /// Поточні картки лишаються на екрані, доки не прийде відповідь.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadAsync() => LoadCoreAsync(showSpinner: true);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        try
        {
            await LoadCoreAsync(showSpinner: false);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task LoadCoreAsync(bool showSpinner)
    {
        _loadCts?.Cancel();
        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken ct = cts.Token;

        ErrorMessage = null;
        IsLoading = showSpinner;
        try
        {
            IReadOnlyList<SavedVacancyDto> saved = await _gateway.GetSavedVacanciesAsync(ct);
            Vacancies.Clear();
            foreach (SavedVacancyDto entry in saved.Where(s => s.Vacancy is not null).OrderByDescending(s => s.SavedAt))
            {
                // Усе в цьому списку збережене за визначенням, незалежно від прапорця у вкладеній вакансії.
                Vacancies.Add(new VacancyItemViewModel(
                    entry.Vacancy! with { IsSaved = true }, _gateway, _messenger, message => ErrorMessage = message, _urlLauncher, _distanceService));
            }

            HasLoaded = true;
        }
        catch (ApiException ex) when (!ct.IsCancellationRequested)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Замінено новішим завантаженням.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                OnCountChanged();
            }
        }
    }

    private void OnCountChanged()
    {
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(ShowTotal));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
