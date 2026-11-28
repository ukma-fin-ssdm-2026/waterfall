// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: картка повідомляє каталог про перегляд (VacancyViewedMessage) для фільтра «Приховати переглянуті» (issue #62, на прохання Дениса); перевірено: тести CatalogViewModelTests, самоперевірка автора (§8)
// AI: VisibleSkills / MoreSkillsText через TDD (Lab 06); перевірено: VacancyItemSkillsTests (RED перед кожним GREEN), запуск застосунку.

using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Helpers;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Repository.Normalization;

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
    private readonly Action<Guid, double?>? _onDistanceCalculated;

    private bool _distanceLoadAttempted;
    private string? _distanceLoadedForAddress;
    private string? _distanceInFlightAddress;
    private int _isDistanceLoading;
    private int _distanceOperationId;

    /// <param name="vacancy">Дані вакансії з API.</param>
    /// <param name="gateway">Шлюз для збереження / видалення з «Обраного».</param>
    /// <param name="messenger">Куди повідомити про зміну стану (<see cref="VacancySaveChangedMessage"/>).</param>
    /// <param name="reportError">Куди показати помилку перемикача (банер сторінки).</param>
    /// <param name="urlLauncher">Сервіс запуску посилань у браузері.</param>
    /// <param name="distanceService">Сервіс розрахунку реальної відстані до офісу вакансії.</param>
    /// <param name="onDistanceCalculated">Зворотний виклик при завершенні розрахунку відстані (id, km).</param>
    /// <param name="initialDistanceKm">Попередньо розрахована відстань у км (якщо доступна з кешу).</param>
    /// <param name="canMarkViewed">Чи дозволено відстежувати перегляд вакансії (лише роль JobSeeker за data-model.md §6).</param>
    /// <param name="notes">Нотатка користувача до збереженої вакансії (FR-005, лише читання).</param>
    public VacancyItemViewModel(
        VacancyDto vacancy,
        IHttpGatewayService gateway,
        IMessenger messenger,
        Action<string> reportError,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null,
        Action<Guid, double?>? onDistanceCalculated = null,
        double? initialDistanceKm = null,
        bool canMarkViewed = true,
        string? notes = null)
    {
        Vacancy = vacancy;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        _gateway = gateway;
        _messenger = messenger;
        _reportError = reportError;
        _urlLauncher = urlLauncher ?? new Waterfall.Client.Services.DefaultUrlLauncher();
        _distanceService = distanceService;
        _onDistanceCalculated = onDistanceCalculated;
        IsSaved = vacancy.IsSaved;
        CanMarkViewed = canMarkViewed;
        IsViewed = canMarkViewed && vacancy.IsViewed;
        VacancySelectionState.Register(this);
        Category = string.IsNullOrWhiteSpace(vacancy.Category) ? "Інше" : vacancy.Category;
        CategoryGroup = !string.IsNullOrWhiteSpace(vacancy.CategoryGroup)
            ? vacancy.CategoryGroup
            : VacancyCategoryMap.Resolve(Category);

        CanShowOnMap = HasExactOfficeAddress(GetMapTarget());

        if (initialDistanceKm.HasValue && ShouldResolveDistance(_distanceService?.UserAddress))
        {
            DistanceKm = initialDistanceKm;
            DistanceText = initialDistanceKm.Value < 1.0
                ? $"{Math.Round(initialDistanceKm.Value * 1000)} м від вас"
                : $"{initialDistanceKm.Value.ToString("0.0", CultureInfo.InvariantCulture)} км від вас";
            _distanceLoadAttempted = true;
            _distanceLoadedForAddress = _distanceService?.UserAddress;
        }

        List<string> skills = CleanSkills(vacancy.Skills);
        VisibleSkills = skills.Take(MaxVisibleSkills).ToList();
        int hidden = skills.Count - VisibleSkills.Count;
        MoreSkillsText = hidden > 0 ? $"+{hidden}" : null;

        messenger.Register<VacancyItemViewModel, UserAddressChangedMessage>(this, static (vm, message) =>
        {
            if (vm._distanceLoadAttempted)
            {
                _ = vm.RefreshDistanceAsync();
            }
            else
            {
                vm._distanceLoadedForAddress = null;
                vm.DistanceKm = null;
                vm.DistanceText = null;
            }
        });
    }

    public VacancyDto Vacancy { get; }

    public Guid Id => Vacancy.Id;
    public string Title => Vacancy.Title;
    public string Company => Vacancy.Company;
    public string Description => Vacancy.Description;
    public IReadOnlyList<string> Skills => Vacancy.Skills;
    public bool IsActive => Vacancy.IsActive;

    /// <summary>
    /// Нотатка користувача до збереженої вакансії (FR-005); null — нотатки немає.
    /// </summary>
    public string? Notes { get; }

    public bool HasNotes => !string.IsNullOrEmpty(Notes);

    /// <summary>
    /// Готовий до показу рядок нотатки на картці (FR-005).
    /// </summary>
    public string? NotesDisplay => HasNotes ? $"Нотатка: {Notes}" : null;

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

    public string Category { get; }

    public string CategoryGroup { get; }

    /// <summary>Читабельний рядок категорії та її канонічної групи (наприклад, «.NET · Software Development»).</summary>
    public string CategoryDisplay =>
        string.IsNullOrWhiteSpace(Category)
            ? CategoryGroup
            : string.Equals(Category, CategoryGroup, StringComparison.OrdinalIgnoreCase)
                ? Category
                : $"{Category} · {CategoryGroup}";

    public bool HasSchedule => !string.IsNullOrWhiteSpace(Schedule);
    public bool HasExperience => !string.IsNullOrWhiteSpace(Experience);
    public bool HasFullAddress => !string.IsNullOrWhiteSpace(FullAddress);

    /// <summary>
    /// Відображення повної адреси або локації вакансії під блоком компанії.
    /// </summary>
    public string AddressDisplay => !string.IsNullOrWhiteSpace(FullAddress)
        ? FullAddress
        : (!string.IsNullOrWhiteSpace(Vacancy.Location) ? $"{Vacancy.Location}, не вказано точну адресу" : "Не вказано точну адресу");

    public bool HasAddress => !string.IsNullOrWhiteSpace(FullAddress) || !string.IsNullOrWhiteSpace(Vacancy.Location);

    /// <summary>
    /// Відстань від поточної адреси користувача до офісу (наприклад, «1.5 км від вас»).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDistanceText))]
    public partial string? DistanceText { get; set; }

    /// <summary>
    /// Розрахована числова відстань у км від поточної адреси користувача до офісу.
    /// </summary>
    [ObservableProperty]
    public partial double? DistanceKm { get; set; }

    public bool HasDistanceText => !string.IsNullOrWhiteSpace(DistanceText);

    /// <summary>
    /// Чи має картка взагалі розраховувати відстань: потрібна точна адреса офісу,
    /// а розпізнані міста вакансії та користувача мають збігатися.
    /// Нерозпізнане місто (передмістя) не блокує розрахунок.
    /// </summary>
    private bool ShouldResolveDistance(string? userAddress)
    {
        if (!CanShowOnMap)
        {
            return false;
        }

        string? targetCity = GeoDistanceService.ExtractCityKey(GetMapTarget());
        string? userCity = GeoDistanceService.ExtractCityKey(userAddress);
        return targetCity is null
            || userCity is null
            || string.Equals(targetCity, userCity, StringComparison.Ordinal);
    }

    /// <summary>
    /// Мовчки прибирає відстань, не звертаючись до сервісу: картка не підлягає розрахунку,
    /// але спроба фіксується, щоб повторні виклики не запускали її знову.
    /// </summary>
    private void ClearDistanceWithoutResolution(string? userAddress)
    {
        _distanceLoadAttempted = true;
        _distanceLoadedForAddress = userAddress;
        DistanceKm = null;
        DistanceText = null;
        _onDistanceCalculated?.Invoke(Vacancy.Id, null);
    }

    public Task EnsureDistanceLoadedAsync(CancellationToken ct = default)
    {
        string? currentAddress = _distanceService?.UserAddress;
        if (_distanceService is null || !HasAddress || string.IsNullOrWhiteSpace(currentAddress))
        {
            return Task.CompletedTask;
        }

        if (!ShouldResolveDistance(currentAddress))
        {
            ClearDistanceWithoutResolution(currentAddress);
            return Task.CompletedTask;
        }

        if ((Volatile.Read(ref _isDistanceLoading) == 1 &&
             string.Equals(_distanceInFlightAddress, currentAddress, StringComparison.OrdinalIgnoreCase)) ||
            (_distanceLoadAttempted &&
             string.Equals(_distanceLoadedForAddress, currentAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        return RefreshDistanceAsync(ct);
    }

    public async Task RefreshDistanceAsync(CancellationToken ct = default)
    {
        _distanceLoadAttempted = true;
        string? userAddress = _distanceService?.UserAddress;
        int opId = Interlocked.Increment(ref _distanceOperationId);
        _distanceInFlightAddress = userAddress;
        Volatile.Write(ref _isDistanceLoading, 1);

        try
        {
            if (_distanceService is null ||
                !HasAddress ||
                string.IsNullOrWhiteSpace(userAddress))
            {
                _distanceLoadedForAddress = userAddress;
                DistanceKm = null;
                DistanceText = null;
                _onDistanceCalculated?.Invoke(Vacancy.Id, null);
                return;
            }

            if (!ShouldResolveDistance(userAddress))
            {
                ClearDistanceWithoutResolution(userAddress);
                return;
            }

            string target = GetMapTarget();
            if (string.IsNullOrWhiteSpace(target) ||
                target.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("Віддалено", StringComparison.OrdinalIgnoreCase))
            {
                _distanceLoadedForAddress = userAddress;
                DistanceKm = null;
                DistanceText = null;
                _onDistanceCalculated?.Invoke(Vacancy.Id, null);
                return;
            }

            try
            {
                double? km = await _distanceService.CalculateDistanceKmAsync(target, ct);
                if (opId != Volatile.Read(ref _distanceOperationId))
                {
                    return;
                }

                _distanceLoadedForAddress = userAddress;
                DistanceKm = km;
                _onDistanceCalculated?.Invoke(Vacancy.Id, km);
                if (km.HasValue)
                {
                    DistanceText = km.Value < 1.0
                        ? $"{Math.Round(km.Value * 1000)} м від вас"
                        : $"{km.Value.ToString("0.0", CultureInfo.InvariantCulture)} км від вас";
                }
                else
                {
                    DistanceText = "Не вдалося розрахувати";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (opId == Volatile.Read(ref _distanceOperationId))
                {
                    _distanceLoadedForAddress = null;
                }
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                if (opId != Volatile.Read(ref _distanceOperationId))
                {
                    return;
                }

                _distanceLoadedForAddress = userAddress;
                DistanceKm = null;
                _onDistanceCalculated?.Invoke(Vacancy.Id, null);
                DistanceText = "Немає інтернету";
            }
            catch
            {
                if (opId != Volatile.Read(ref _distanceOperationId))
                {
                    return;
                }

                _distanceLoadedForAddress = userAddress;
                DistanceKm = null;
                _onDistanceCalculated?.Invoke(Vacancy.Id, null);
                DistanceText = "Не вдалося розрахувати";
            }
        }
        catch
        {
            if (opId == Volatile.Read(ref _distanceOperationId))
            {
                DistanceKm = null;
                _onDistanceCalculated?.Invoke(Vacancy.Id, null);
                DistanceText = null;
            }
        }
        finally
        {
            if (opId == Volatile.Read(ref _distanceOperationId))
            {
                Volatile.Write(ref _isDistanceLoading, 0);
            }
        }
    }

    private static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException ||
        ex is System.Net.Sockets.SocketException ||
        (ex is TaskCanceledException tc && !tc.CancellationToken.IsCancellationRequested) ||
        ex.InnerException is HttpRequestException ||
        ex.InnerException is System.Net.Sockets.SocketException ||
        ex.Message.Contains("інтернет", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("internet", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapToggleText))]
    public partial bool IsMapExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMap { get; set; }

    [ObservableProperty]
    public partial string? MapHtml { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapCoordinates))]
    [NotifyPropertyChangedFor(nameof(MapCoordinatesDisplay))]
    public partial double? MapLatitude { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapCoordinates))]
    [NotifyPropertyChangedFor(nameof(MapCoordinatesDisplay))]
    public partial double? MapLongitude { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapError))]
    public partial string? MapErrorMessage { get; set; }

    public bool HasMapCoordinates => MapLatitude.HasValue && MapLongitude.HasValue;

    public string MapCoordinatesDisplay => HasMapCoordinates
        ? $"{MapLatitude!.Value.ToString("F4", CultureInfo.InvariantCulture)}, {MapLongitude!.Value.ToString("F4", CultureInfo.InvariantCulture)}"
        : string.Empty;

    public string MapToggleText => IsMapExpanded ? "Приховати мапу" : "Показати на мапі";

    public bool HasMapError => !string.IsNullOrWhiteSpace(MapErrorMessage);

    public bool CanShowOnMap { get; }

    private string GetMapTarget() =>
        !string.IsNullOrWhiteSpace(FullAddress) ? FullAddress : (Vacancy.Location ?? string.Empty);

    private static readonly string[] ExactAddressMarkers =
    [
        "вул.", "вул ", "вулиц", "просп", "пр-т", "пр.", "пл.", "площ",
        "бульв", "б-р", "пров.", "провулок", "набережн", "шосе", "узвіз",
        "алея", "тупик", "мікрорайон", "мкр", "бц ", "бц«", "трц", "тц ",
        "офіс", "метро", "ст. м.", "unit.city", "gulliver", "parus",
        "horizon", "eurasia", "ocean plaza", "dream town",
        "st.", "str.", "street", "ave", "blvd", "sq.", "square", "plaza"
    ];

    private static bool HasExactOfficeAddress(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string trimmed = target.Trim();
        if (trimmed.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("віддален", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("дистанц", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("за кордоном", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("abroad", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Equals("україна", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ukraine", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("вся україна", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("європа", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("europe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        foreach (string marker in ExactAddressMarkers)
        {
            if (trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [RelayCommand]
    public async Task ToggleMapAsync()
    {
        if (IsMapExpanded)
        {
            IsMapExpanded = false;
            return;
        }

        IsMapExpanded = true;

        if (!string.IsNullOrWhiteSpace(MapHtml))
        {
            return;
        }

        string target = GetMapTarget();

        if (target.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("Віддалено", StringComparison.OrdinalIgnoreCase))
        {
            MapErrorMessage = "Вакансія віддалена (Remote), фізичний офіс відсутній.";
            return;
        }

        if (string.IsNullOrWhiteSpace(target) || !CanShowOnMap)
        {
            MapErrorMessage = "Точну адресу офісу не вказано.";
            return;
        }

        IsLoadingMap = true;
        MapErrorMessage = null;

        try
        {
            (double Lat, double Lon)? coords = null;
            if (_distanceService != null)
            {
                coords = await _distanceService.ResolveCoordinatesAsync(target);
            }
            else
            {
                coords = Waterfall.Client.Services.GeoDistanceService.LookupKnownLocation(target);
            }

            if (coords.HasValue)
            {
                MapLatitude = coords.Value.Lat;
                MapLongitude = coords.Value.Lon;
                string label = !string.IsNullOrWhiteSpace(FullAddress) ? FullAddress : $"{Vacancy.Company} ({Vacancy.Location})";
                MapHtml = MapHtmlHelper.GenerateMapHtml(coords.Value.Lat, coords.Value.Lon, label);
                MapErrorMessage = null;
            }
            else
            {
                MapErrorMessage = "Не вдалося визначити координати адреси на карті.";
            }
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            MapErrorMessage = "Немає підключення до інтернету для завантаження карти.";
        }
        catch
        {
            MapErrorMessage = "Помилка при спробі визначити координати адреси.";
        }
        finally
        {
            IsLoadingMap = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsToggleText))]
    public partial bool IsDetailsExpanded { get; set; }

    public string DetailsToggleText => IsDetailsExpanded ? "Згорнути ▲" : "Детальніше ▼";

    [RelayCommand]
    private void ToggleDetails()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
        if (IsDetailsExpanded && CanMarkViewed)
        {
            IsViewed = true;
            _ = _gateway.RecordVacancyViewAsync(Id);
            _messenger.Send(new VacancyViewedMessage(Id, true));
        }
    }

    [RelayCommand]
    private async Task OpenSourceAsync()
    {
        if (CanMarkViewed)
        {
            IsViewed = true;
            _ = _gateway.RecordVacancyViewAsync(Id);
            _messenger.Send(new VacancyViewedMessage(Id, true));
        }

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

    /// <summary>
    /// Чи дозволено позначати вакансію як переглянуту (лише авторизований JobSeeker за data-model.md §6).
    /// </summary>
    [ObservableProperty]
    public partial bool CanMarkViewed { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewedButtonText))]
    public partial bool IsViewed { get; set; }

    /// <summary>
    /// true під час короткої анімації приховування картки (коли увімкнено «Без переглянутих» і натиснуто кнопку перегляду).
    /// </summary>
    [ObservableProperty]
    public partial bool IsDismissing { get; set; }

    public string ViewedButtonText => IsViewed ? "Зняти позначку переглянуто" : "Позначити як переглянуто";

    /// <summary>
    /// Відписує картку від messenger, коли сторінка списку її відпускає (лінива матеріалізація).
    /// </summary>
    internal void Detach() => _messenger.UnregisterAll(this);

    /// <summary>
    /// Чи позначено картку для групового ШІ-аналізу (FR-010, до 3 вакансій).
    /// </summary>
    public bool IsSelected => VacancySelectionState.Contains(Id);

    /// <summary>
    /// Скільки вакансій уже вибрано; у режимі вибору показується в кнопці ШІ-аналізу.
    /// </summary>
    public int SelectedCount => VacancySelectionState.Count;

    /// <summary>
    /// Ліміт вибору досягнуто, а ця картка ще не вибрана — чекбокс неактивний.
    /// </summary>
    public bool IsSelectionLocked => !IsSelected && VacancySelectionState.Count >= VacancySelectionState.MaxSelected;

    /// <summary>
    /// Текст кнопки ШІ-аналізу: для вибраної картки показує розмір групи.
    /// </summary>
    public string SelectionBadgeText => IsSelected && VacancySelectionState.Count > 1
        ? $"ШІ-аналіз ({VacancySelectionState.Count})"
        : "ШІ-аналіз";

    public string SelectionHintText => IsSelected
        ? "Зняти вибір вакансії для ШІ-аналізу"
        : "Вибрати вакансію для групового ШІ-аналізу (до 3)";

    /// <summary>
    /// Позначає / знімає позначку вибору картки. Понад три вакансії вибрати не можна (FR-010).
    /// </summary>
    [RelayCommand]
    private void ToggleSelection()
    {
        if (!VacancySelectionState.Toggle(Vacancy))
        {
            _reportError($"Для ШІ-аналізу можна вибрати не більше {VacancySelectionState.MaxSelected} вакансій.");
        }
    }

    internal void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsSelectionLocked));
        OnPropertyChanged(nameof(SelectionBadgeText));
        OnPropertyChanged(nameof(SelectionHintText));
    }

    [RelayCommand]
    private async Task ToggleViewedAsync()
    {
        if (!CanMarkViewed)
        {
            return;
        }

        bool target = !IsViewed;
        IsViewed = target;
        _messenger.Send(new VacancyViewedMessage(Id, target, Explicit: true));

        if (target)
        {
            await _gateway.RecordVacancyViewAsync(Id);
        }
        else
        {
            await _gateway.UnmarkVacancyViewAsync(Id);
        }
    }

    public string? PublishedAtDisplay => Vacancy.PublishedAt != default
        ? $"Опубл. {Vacancy.PublishedAt.ToLocalTime():dd.MM.yyyy}"
        : null;

    public string? ParsedAtDisplay
    {
        get
        {
            DateTimeOffset parsedAt = Vacancy.LastSeenAt != default ? Vacancy.LastSeenAt : Vacancy.FirstSeenAt;
            return parsedAt != default
                ? $"Спаршено {parsedAt.ToLocalTime():dd.MM.yyyy}"
                : null;
        }
    }

    public string DatesDisplay => (PublishedAtDisplay, ParsedAtDisplay) switch
    {
        ({ } pub, { } parsed) => $"{pub} · {parsed}",
        ({ } pub, null) => pub,
        (null, { } parsed) => parsed,
        _ => string.Empty
    };

    public bool HasDatesDisplay => !string.IsNullOrEmpty(DatesDisplay);

    /// <summary>
    /// Передає вакансію ШІ-консультанту (FR-010): вкладка ШІ відкриється й покаже аналіз.
    /// Якщо картка входить до групи вибраних (2–3 вакансії), надсилається вся група.
    /// </summary>
    [RelayCommand]
    private void AnalyzeWithAi()
    {
        bool isSelected = VacancySelectionState.Contains(Id);
        bool isGroup = isSelected && VacancySelectionState.Count > 1;
        IReadOnlyList<VacancyDto> batch = isGroup ? VacancySelectionState.Snapshot() : [Vacancy];
        if (isSelected)
        {
            // Після запуску аналізу режим вибору скидається, щоб старі позначки не потрапили в наступний запит.
            VacancySelectionState.Clear();
        }

        if (isGroup)
            _messenger.Send(new AnalyzeVacanciesRequestedMessage(batch));
        else
            _messenger.Send(new AnalyzeVacancyRequestedMessage(Vacancy));
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

/// <summary>
/// Спільний для всіх вкладок стан вибору вакансій для ШІ-аналізу (FR-010).
/// Сервер приймає 1–3 вакансії за один запит, тож четверту позначку картка просто ігнорує.
/// </summary>
public static class VacancySelectionState
{
    public const int MaxSelected = 3;

    private static readonly List<VacancyDto> s_selected = [];
    private static readonly List<WeakReference<VacancyItemViewModel>> s_listeners = [];
    private static readonly object s_sync = new();

    public static int Count
    {
        get
        {
            lock (s_sync)
            {
                return s_selected.Count;
            }
        }
    }

    public static bool Contains(Guid vacancyId)
    {
        lock (s_sync)
        {
            return s_selected.Any(v => v.Id == vacancyId);
        }
    }

    /// <summary>
    /// Додає або прибирає вакансію. Повертає false, якщо ліміт у 3 вакансії вже вичерпано.
    /// </summary>
    public static bool Toggle(VacancyDto vacancy)
    {
        lock (s_sync)
        {
            int index = s_selected.FindIndex(v => v.Id == vacancy.Id);
            if (index >= 0)
            {
                s_selected.RemoveAt(index);
            }
            else if (s_selected.Count >= MaxSelected)
            {
                return false;
            }
            else
            {
                s_selected.Add(vacancy);
            }
        }

        NotifyListeners();
        return true;
    }

    public static IReadOnlyList<VacancyDto> Snapshot()
    {
        lock (s_sync)
        {
            return s_selected.ToArray();
        }
    }

    public static void Clear()
    {
        lock (s_sync)
        {
            if (s_selected.Count == 0)
            {
                return;
            }

            s_selected.Clear();
        }

        NotifyListeners();
    }

    internal static void Register(VacancyItemViewModel viewModel)
    {
        lock (s_sync)
        {
            PruneDeadListeners();
            s_listeners.Add(new WeakReference<VacancyItemViewModel>(viewModel));
        }
    }

    private static void NotifyListeners()
    {
        List<VacancyItemViewModel> listeners = [];
        lock (s_sync)
        {
            PruneDeadListeners();
            foreach (WeakReference<VacancyItemViewModel> reference in s_listeners)
            {
                if (reference.TryGetTarget(out VacancyItemViewModel? viewModel))
                {
                    listeners.Add(viewModel);
                }
            }
        }

        foreach (VacancyItemViewModel viewModel in listeners)
        {
            viewModel.NotifySelectionChanged();
        }
    }

    private static void PruneDeadListeners()
    {
        for (int i = s_listeners.Count - 1; i >= 0; i--)
        {
            if (!s_listeners[i].TryGetTarget(out _))
            {
                s_listeners.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Картка передає ШІ-консультанту групу вибраних вакансій (2–3, FR-010).
/// </summary>
/// <param name="Vacancies">Вибрані вакансії; сервер приймає 1–3.</param>
public sealed record AnalyzeVacanciesRequestedMessage(IReadOnlyList<VacancyDto> Vacancies);
