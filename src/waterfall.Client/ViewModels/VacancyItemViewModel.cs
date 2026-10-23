// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: VisibleSkills / MoreSkillsText через TDD (Lab 06); перевірено: VacancyItemSkillsTests (RED перед кожним GREEN), запуск застосунку.

using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Модель картки вакансії: готові до показу тексти та перемикач «Обране».
/// Спільна для вкладок «Каталог» та «Обране».
/// </summary>
public partial class VacancyItemViewModel : ObservableObject
{
    // Розділювач тисяч задано явно (нерозривний пробіл): дані культури uk-UA різняться між версіями ICU
    // та платформами (Windows / Android), а «4 000» не повинно переноситися на два рядки.
    private static readonly NumberFormatInfo MoneyFormat = new() { NumberGroupSeparator = "\u00A0", NumberGroupSizes = [3] };

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly Action<string> _reportError;
    private readonly IUrlLauncher _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;

    /// <param name="vacancy">Дані вакансії з API.</param>
    /// <param name="gateway">Шлюз для збереження / видалення з «Обраного».</param>
    /// <param name="messenger">Куди повідомити про зміну стану (<see cref="VacancySaveChangedMessage"/>).</param>
    /// <param name="reportError">Куди показати помилку перемикача (банер сторінки).</param>
    /// <param name="urlLauncher">Сервіс запуску посилань у браузері.</param>
    /// <param name="distanceService">Сервіс розрахунку відстані за Google Maps.</param>
    public VacancyItemViewModel(
        VacancyDto vacancy,
        IHttpGatewayService gateway,
        IMessenger messenger,
        Action<string> reportError,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null)
    {
        Vacancy = vacancy;
        _gateway = gateway;
        _messenger = messenger;
        _reportError = reportError;
        _urlLauncher = urlLauncher ?? new Waterfall.Client.Services.DefaultUrlLauncher();
        _distanceService = distanceService;
        IsSaved = vacancy.IsSaved;

        List<string> skills = CleanSkills(vacancy.Skills);
        VisibleSkills = skills.Take(MaxVisibleSkills).ToList();
        int hidden = skills.Count - VisibleSkills.Count;
        MoreSkillsText = hidden > 0 ? $"+{hidden}" : null;

        messenger.Register<VacancyItemViewModel, UserAddressChangedMessage>(this, static (vm, message) =>
        {
            _ = vm.RefreshDistanceAsync();
        });

        _ = RefreshDistanceAsync();
    }

    public VacancyDto Vacancy { get; }

    public Guid Id => Vacancy.Id;
    public string Title => Vacancy.Title;
    public string Description => Vacancy.Description;
    public IReadOnlyList<string> Skills => Vacancy.Skills;
    public bool IsActive => Vacancy.IsActive;

    /// <summary>
    /// «Компанія · Локація» (локація може бути порожньою).
    /// </summary>
    public string CompanyLine => string.IsNullOrWhiteSpace(Vacancy.Location)
        ? Vacancy.Company
        : $"{Vacancy.Company} · {Vacancy.Location}";

    public string StatusText => Vacancy.IsActive ? "Активна" : "Архівна";

    /// <summary>
    /// «4 000–5 500 USD», «від 4 000 USD», «до 5 500 USD»; null — зарплату не вказано.
    /// </summary>
    public string? SalaryText => (Vacancy.SalaryMin, Vacancy.SalaryMax) switch
    {
        ({ } min, { } max) when min == max => $"{Money(min)} {Vacancy.Currency}",
        ({ } min, { } max) => $"{Money(min)}–{Money(max)} {Vacancy.Currency}",
        ({ } min, null) => $"від {Money(min)} {Vacancy.Currency}",
        (null, { } max) => $"до {Money(max)} {Vacancy.Currency}",
        _ => null
    };

    public bool HasSalary => SalaryText is not null;

    /// <summary>
    /// Відображення заробітної плати: «Зарплата не вказана» якщо не задана (US-001 AC-3, Test-32).
    /// </summary>
    public string SalaryDisplay => SalaryText ?? "Зарплата не вказана";

    public string SourceUrl => Vacancy.SourceUrl;
    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);

    public string? FullAddress => Vacancy.FullAddress;
    public string? Schedule => Vacancy.Schedule;
    public string? Experience => Vacancy.Experience;
    public string Category => Vacancy.Category;

    public bool HasSchedule => !string.IsNullOrWhiteSpace(Schedule);
    public bool HasExperience => !string.IsNullOrWhiteSpace(Experience);
    public bool HasFullAddress => !string.IsNullOrWhiteSpace(FullAddress);

    /// <summary>
    /// Відображення повної адреси або локації вакансії під блоком компанії.
    /// </summary>
    public string AddressDisplay => !string.IsNullOrWhiteSpace(FullAddress)
        ? FullAddress
        : (!string.IsNullOrWhiteSpace(Vacancy.Location) ? Vacancy.Location : "Локація не вказана");

    public bool HasAddress => !string.IsNullOrWhiteSpace(FullAddress) || !string.IsNullOrWhiteSpace(Vacancy.Location);

    /// <summary>
    /// Відстань від поточної адреси користувача до офісу (наприклад, «1.5 км від вас»).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDistanceText))]
    public partial string? DistanceText { get; set; }

    public bool HasDistanceText => !string.IsNullOrWhiteSpace(DistanceText);

    public async Task RefreshDistanceAsync(CancellationToken ct = default)
    {
        try
        {
            if (_distanceService is null || !HasAddress || string.IsNullOrWhiteSpace(_distanceService.UserAddress))
            {
                DistanceText = null;
                return;
            }

            string target = !string.IsNullOrWhiteSpace(FullAddress) ? FullAddress : Vacancy.Location;
            double? km = await _distanceService.CalculateDistanceKmAsync(target, ct);
            if (km.HasValue)
            {
                DistanceText = km.Value < 1.0
                    ? $"{Math.Round(km.Value * 1000)} м від вас"
                    : $"{km.Value.ToString("0.0", CultureInfo.InvariantCulture)} км від вас";
            }
            else
            {
                DistanceText = null;
            }
        }
        catch
        {
            DistanceText = null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsToggleText))]
    public partial bool IsDetailsExpanded { get; set; }

    public string DetailsToggleText => IsDetailsExpanded ? "Згорнути ▲" : "Детальніше ▼";

    [RelayCommand]
    private void ToggleDetails() => IsDetailsExpanded = !IsDetailsExpanded;

    [RelayCommand]
    private async Task OpenSourceAsync()
    {
        if (HasSourceUrl && Uri.TryCreate(SourceUrl, UriKind.Absolute, out Uri? uri))
        {
            try
            {
                bool success = await _urlLauncher.OpenAsync(uri);
                if (!success)
                {
                    _reportError("Не вдалося відкрити посилання у браузері.");
                }
            }
            catch (Exception ex)
            {
                _reportError($"Не вдалося відкрити посилання: {ex.Message}");
            }
        }
    }

    // # Mock
    [RelayCommand]
    private void AnalyzeWithAi()
    {
        _reportError($"Запит на ШІ-аналіз вакансії «{Title}» передано асистенту.");
    }

    public string? SourceText => Vacancy.Source switch
    {
        JobSource.WorkUa => "Work.ua",
        JobSource.RobotaUa => "Robota.ua",
        JobSource.Djinni => "Djinni",
        JobSource.Dou => "DOU",
        _ => null
    };

    /// <summary>
    /// Скільки чипів навичок показує картка; решту згортає чип «+N».
    /// </summary>
    public const int MaxVisibleSkills = 3;

    public IReadOnlyList<string> VisibleSkills { get; }

    public string? MoreSkillsText { get; }

    public bool HasMoreSkills => MoreSkillsText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    public partial bool IsSaved { get; set; }

    public string SaveButtonText => IsSaved ? "★ Збережено" : "☆ Зберегти";

    /// <summary>
    /// Додає / прибирає вакансію з «Обраного». Поки запит триває, команда недоступна (кнопка вимкнена).
    /// Розбіжність стану з сервером (409 при збереженні, 404 при видаленні) означає,
    /// що бажаний стан уже досягнуто — просто синхронізуємо картку.
    /// Після зміни стану інші вкладки отримують <see cref="VacancySaveChangedMessage"/>.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSaveAsync()
    {
        bool target = !IsSaved;
        try
        {
            if (target)
            {
                await _gateway.SaveVacancyAsync(Id);
            }
            else
            {
                await _gateway.UnsaveVacancyAsync(Id);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == (target ? HttpStatusCode.Conflict : HttpStatusCode.NotFound))
        {
            // Сервер уже в бажаному стані.
        }
        catch (ApiException ex)
        {
            _reportError(ex.Message);
            return;
        }

        IsSaved = target;
        _messenger.Send(new VacancySaveChangedMessage(Id, target));
    }

    private static string Money(decimal value) => value.ToString("#,0", MoneyFormat);

    /// <summary>
    /// Навички зі скрапера бувають з пробілами, порожні або повторені в іншому регістрі.
    /// </summary>
    private static List<string> CleanSkills(IEnumerable<string> skills) => skills
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
