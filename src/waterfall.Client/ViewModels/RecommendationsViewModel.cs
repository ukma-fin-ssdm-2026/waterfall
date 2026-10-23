// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Рекомендації» (FR-006, US-004): персоналізовані вакансії на основі збережених технологій.
/// </summary>
public partial class RecommendationsViewModel : ObservableObject
{
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;

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
            foreach (VacancyItemViewModel item in vm.Recommendations.Where(v => v.Id == message.VacancyId))
            {
                item.IsSaved = message.IsSaved;
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
    public partial string RecommendedBasisText { get; set; } = "Підібрано на основі ваших збережених вакансій (Python, .NET)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Повідомлення за US-004 AC-2: якщо немає збережень або рекомендацій.
    /// </summary>
    public bool IsEmpty => HasLoaded && !IsLoading && ErrorMessage is null && Recommendations.Count == 0;

    // # Mock
    [RelayCommand]
    public Task LoadRecommendationsAsync() => LoadRecommendationsInternalAsync(showSpinner: true);

    // # Mock
    private async Task LoadRecommendationsInternalAsync(bool showSpinner)
    {
        ErrorMessage = null;
        IsLoading = showSpinner;
        try
        {
            // Отримуємо з бекенду або формуємо демо-рекомендації
            PagedResult<VacancyDto> page = await _gateway.GetVacanciesAsync("Python", 1, 10);
            Recommendations.Clear();
            foreach (VacancyDto vacancy in page.Items.Where(v => !v.IsSaved))
            {
                Recommendations.Add(new VacancyItemViewModel(vacancy, _gateway, _messenger, msg => ErrorMessage = msg, _urlLauncher, _distanceService));
            }
            HasLoaded = true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
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
}
