// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Рекомендації» (FR-006, US-004): персоналізовані вакансії на основі збережених вакансій та історії пошуків.
/// </summary>
public partial class RecommendationsViewModel : ObservableObject
{
    private const string InitialBasisText = "Збережіть кілька вакансій, щоб отримати персоналізовані рекомендації";

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private CancellationTokenSource? _loadCts;

    public RecommendationsViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;

        _messenger.Register<RecommendationsViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            if (message.IsSaved)
            {
                foreach (VacancyItemViewModel item in vm.Recommendations.Where(v => v.Id == message.VacancyId).ToList())
                {
                    vm.Recommendations.Remove(item);
                }
            }

            if (vm.HasLoaded)
            {
                _ = vm.LoadRecommendationsInternalAsync(showSpinner: false);
            }
        });
    }

    public ObservableCollection<VacancyItemViewModel> Recommendations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string RecommendedBasisText { get; set; } = InitialBasisText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyDescription))]
    public partial int SavedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Повідомлення за US-004 AC-2: якщо немає збережень або нових рекомендацій.
    /// </summary>
    public bool IsEmpty => HasLoaded && !IsLoading && ErrorMessage is null && Recommendations.Count == 0;

    public string EmptyTitle => SavedCount == 0
        ? "Ще немає збережених вакансій"
        : "Нових рекомендацій поки немає";

    public string EmptyDescription => SavedCount == 0
        ? "Почніть зберігати цікаві вакансії в каталозі (натисніть ♡), і ми автоматично підберемо для вас персоналізовані пропозиції"
        : "Ви вже зберегли всі підходящі пропозиції або каталог ще оновлюється. Перегляньте каталог чи збережіть інші вакансії";

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task LoadRecommendationsAsync() => LoadRecommendationsInternalAsync(showSpinner: true);

    private async Task LoadRecommendationsInternalAsync(bool showSpinner)
    {
        _loadCts?.Cancel();
        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken ct = cts.Token;

        ErrorMessage = null;
        IsLoading = showSpinner;
        try
        {
            RecommendationsResultDto result = await _gateway.GetRecommendationsAsync(ct: ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            SavedCount = result.SavedCount;
            RecommendedBasisText = FormatBasisText(result);

            Recommendations.Clear();
            foreach (VacancyDto vacancy in result.Items.Where(v => !v.IsSaved))
            {
                Recommendations.Add(new VacancyItemViewModel(
                    vacancy, _gateway, _messenger, msg => ErrorMessage = msg, _urlLauncher, _distanceService));
            }

            HasLoaded = true;
        }
        catch (ApiException ex) when (!ct.IsCancellationRequested)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
         
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                IsRefreshing = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        try
        {
            await LoadRecommendationsInternalAsync(showSpinner: false);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static string FormatBasisText(RecommendationsResultDto result)
    {
        if (result.SavedCount <= 0)
        {
            return InitialBasisText;
        }

        string keywords = string.Join(", ", result.BasisKeywords);
        string searches = string.Join(", ", result.BasisSearches);

        if (!string.IsNullOrEmpty(keywords) && !string.IsNullOrEmpty(searches))
        {
            return $"Підібрано на основі збережених вакансій ({keywords}) та пошуків ({searches})";
        }

        if (!string.IsNullOrEmpty(keywords))
        {
            return $"Підібрано на основі ваших збережених вакансій ({keywords})";
        }

        if (!string.IsNullOrEmpty(searches))
        {
            return $"Підібрано на основі ваших збережених вакансій та пошуків ({searches})";
        }

        return $"Підібрано на основі {result.SavedCount} збережених вакансій";
    }
}
