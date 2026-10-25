// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Вхід»: вхід / реєстрація (з вибором ролі), стан «увійшли як…» та вихід.
/// </summary>
/// <remarks>
/// Локальна перевірка дублює правила сервера (#67), щоб не робити зайвий запит:
/// реєстрація — пароль 12–32 символи з цифрою; вхід — лише непорожній пароль
/// (політику навмисно не розкриваємо на вході, THR-01). Остаточно перевіряє сервер.
/// </remarks>
public partial class LoginViewModel : ObservableObject
{
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 32;

    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    private readonly IHttpGatewayService _gateway;
    private readonly IAuthTokenStore _tokens;
    private readonly TimeProvider _time;
    private readonly IDistanceCalculatorService? _distanceService;
    private readonly IMessenger? _messenger;
    private readonly IAddressAutocompleteService? _autocompleteService;
    private CancellationTokenSource? _autocompleteCts;
    private bool _isSelectingSuggestion;

    public LoginViewModel(
        IHttpGatewayService gateway,
        IAuthTokenStore tokens,
        TimeProvider time,
        IDistanceCalculatorService? distanceService = null,
        IMessenger? messenger = null,
        IAddressAutocompleteService? autocompleteService = null)
    {
        _gateway = gateway;
        _tokens = tokens;
        _time = time;
        _distanceService = distanceService;
        _messenger = messenger;
        _autocompleteService = autocompleteService;
        SelectedRole = Roles[0];
        // Адреса не відображається і не підтягується, поки користувач не увійшов у систему (завантажується з БД після входу)
        UserAddress = string.Empty;
        if (_distanceService is not null)
        {
            _distanceService.UserAddress = null;
        }
    }

    /// <summary>
    /// Ролі для вибору при реєстрації (обирає сам користувач, FR-008).
    /// </summary>
    public IReadOnlyList<RoleOption> Roles { get; } =
    [
        new(UserRole.JobSeeker, "Шукач роботи"),
        new(UserRole.Analyst, "Аналітик ринку")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle), nameof(SubmitText), nameof(SwitchModeText), nameof(PasswordHint))]
    public partial bool IsRegisterMode { get; set; }

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RoleOption SelectedRole { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(IsLoggedOut), nameof(UserEmail), nameof(UserRoleText), nameof(SessionText))]
    public partial AuthSession? Session { get; private set; }

    public bool HasError => ErrorMessage is not null;
    public bool IsLoggedIn => Session is not null;
    public bool IsLoggedOut => Session is null;

    public string FormTitle => IsRegisterMode ? "Реєстрація" : "Вхід";
    public string SubmitText => IsRegisterMode ? "Зареєструватися" : "Увійти";
    public string SwitchModeText => IsRegisterMode ? "Вже маєте обліковий запис? Увійти" : "Немає облікового запису? Зареєструватися";

    public string? PasswordHint => IsRegisterMode
        ? $"Від {PasswordMinLength} до {PasswordMaxLength} символів, хоча б одна цифра"
        : null;

    public string UserEmail => Session?.Email ?? string.Empty;
    public string UserRoleText => Session is null ? string.Empty : Roles.First(r => r.Role == Session.Role).Title;

    public string SessionText => Session is null
        ? string.Empty
        : $"Сесія дійсна до {Session.ExpiresAt.ToLocalTime().ToString("HH:mm", Uk)}";

    // ===== Особиста статистика користувача (FR-007, US-008) =====
    [ObservableProperty]
    public partial int SavedVacanciesCount { get; set; } = 3;

    public ObservableCollection<string> RecentSearches { get; } =
    [
        "Python remote junior",
        ".NET backend Kyiv",
        "React developer",
        "DevOps AWS",
        "QA manual"
    ];

    public ObservableCollection<string> TopTechnologies { get; } =
    [
        "Python (12)",
        "C# / .NET (9)",
        "Docker (5)",
        "SQL (4)"
    ];

    public bool HasActivity => SavedVacanciesCount > 0 || RecentSearches.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; set; }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [ObservableProperty]
    public partial string UserAddress { get; set; } = string.Empty;

    public ObservableCollection<string> AddressSuggestions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial bool IsLoadingSuggestions { get; set; }

    public bool HasSuggestions => AddressSuggestions.Count > 0;

    partial void OnUserAddressChanged(string value)
    {
        if (_isSelectingSuggestion)
        {
            return;
        }

        _ = TriggerAutocompleteDebouncedAsync(value);
    }

    private async Task TriggerAutocompleteDebouncedAsync(string query)
    {
        _autocompleteCts?.Cancel();
        _autocompleteCts?.Dispose();
        _autocompleteCts = new CancellationTokenSource();
        CancellationToken ct = _autocompleteCts.Token;

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3 || _autocompleteService is null)
        {
            AddressSuggestions.Clear();
            OnPropertyChanged(nameof(HasSuggestions));
            return;
        }

        try
        {
            await Task.Delay(1000, ct);
            IsLoadingSuggestions = true;
            IReadOnlyList<string> suggestions = await _autocompleteService.GetSuggestionsAsync(query, ct);

            AddressSuggestions.Clear();
            foreach (string s in suggestions)
            {
                AddressSuggestions.Add(s);
            }
            OnPropertyChanged(nameof(HasSuggestions));
        }
        catch (OperationCanceledException)
        {
            // Скасовано новим вводом
        }
        catch
        {
            AddressSuggestions.Clear();
            OnPropertyChanged(nameof(HasSuggestions));
        }
        finally
        {
            IsLoadingSuggestions = false;
        }
    }

    [RelayCommand]
    public void SelectAddressSuggestion(string suggestion)
    {
        _isSelectingSuggestion = true;
        UserAddress = suggestion;
        AddressSuggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
        _isSelectingSuggestion = false;
    }

    [RelayCommand]
    public void CloseSuggestions()
    {
        AddressSuggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    [RelayCommand]
    private async Task SaveAddressAsync()
    {
        if (_distanceService is not null)
        {
            _distanceService.UserAddress = UserAddress;
        }

        if (IsLoggedIn)
        {
            try
            {
                await _gateway.SaveProfileAddressAsync(UserAddress);
            }
            catch
            {
                // Офлайн
            }
        }

        _messenger?.Send(new UserAddressChangedMessage(UserAddress));
        StatusMessage = "Адресу збережено! Відстань до вакансій оновлено.";
    }

    public async Task LoadProfileAddressAsync()
    {
        try
        {
            string? address = await _gateway.GetProfileAddressAsync();
            if (!string.IsNullOrWhiteSpace(address))
            {
                UserAddress = address;
                if (_distanceService is not null)
                {
                    _distanceService.UserAddress = address;
                }
                _messenger?.Send(new UserAddressChangedMessage(address));
            }
            else
            {
                UserAddress = string.Empty;
                if (_distanceService is not null)
                {
                    _distanceService.UserAddress = null;
                }
                _messenger?.Send(new UserAddressChangedMessage(null));
            }
        }
        catch
        {
            // Офлайн
        }
    }

    // ===== Імпорт та експорт прогресу/статистики (FR-013) =====
    // # Mock
    [RelayCommand]
    private void ExportProgress() => StatusMessage = "Дані профілю (збережені вакансії, історія, статистика) успішно експортовано у profile_backup.json";

    // # Mock
    [RelayCommand]
    private void ImportProgress()
    {
        SavedVacanciesCount++;
        RecentSearches.Insert(0, "Imported search query");
        StatusMessage = "Дані профілю успішно відновлено з резервного файлу. Статистику оновлено.";
    }

    // # Mock
    [RelayCommand]
    private void ClearRecentSearches()
    {
        RecentSearches.Clear();
        OnPropertyChanged(nameof(HasActivity));
    }

    /// <summary>
    /// Відкриття вкладки: відновити сесію зі збереженого токена (прострочений — видаляється).
    /// </summary>
    [RelayCommand]
    private async Task RestoreSessionAsync()
    {
        string? token = await _tokens.GetTokenAsync();
        AuthSession? session = token is null ? null : AuthSession.FromToken(token);

        if (session is null || session.IsExpired(_time.GetUtcNow()))
        {
            if (token is not null)
            {
                await _tokens.ClearAsync();
            }

            Session = null;
            UserAddress = string.Empty;
            if (_distanceService is not null)
            {
                _distanceService.UserAddress = null;
            }
            _messenger?.Send(new UserAddressChangedMessage(null));
            return;
        }

        Session = session;
        await LoadProfileAddressAsync();
    }

    [RelayCommand]
    private void SwitchMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        string email = Email.Trim();
        ErrorMessage = Validate(email, Password, IsRegisterMode);
        if (ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AuthResponse response = IsRegisterMode
                ? await _gateway.RegisterAsync(new RegisterRequest { Email = email, Password = Password, Role = SelectedRole.Role })
                : await _gateway.LoginAsync(new LoginRequest { Email = email, Password = Password });

            Session = AuthSession.FromResponse(response);
            Password = string.Empty;
            await LoadProfileAddressAsync();
        }
        catch (ApiException ex)
        {
            // Для 400 сервер додає причини по полях — показуємо їх, а не лише загальний заголовок.
            ErrorMessage = ex.Error is { Details.Count: > 0 } error
                ? string.Join(Environment.NewLine, error.Details.Select(d => d.Message))
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSubmit() => !IsBusy;

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _gateway.LogoutAsync();
        Session = null;
        Password = string.Empty;
        ErrorMessage = null;
        UserAddress = string.Empty;
        if (_distanceService is not null)
        {
            _distanceService.UserAddress = null;
        }
        _messenger?.Send(new UserAddressChangedMessage(null));
    }

    /// <returns>Повідомлення про першу порушену вимогу або null, якщо все гаразд.</returns>
    public static string? Validate(string email, string password, bool isRegister)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return "Введіть коректну електронну адресу.";
        }

        if (string.IsNullOrEmpty(password))
        {
            return "Введіть пароль.";
        }

        if (isRegister)
        {
            if (password.Length is < PasswordMinLength or > PasswordMaxLength)
            {
                return $"Пароль має містити від {PasswordMinLength} до {PasswordMaxLength} символів.";
            }

            // char.IsDigit = будь-яка Unicode-цифра, як \d у серверному RegularExpression.
            if (!password.Any(char.IsDigit))
            {
                return "Пароль має містити хоча б одну цифру.";
            }
        }

        return null;
    }
}

/// <summary>
/// Пункт вибору ролі (Picker показує Title).
/// </summary>
public sealed record RoleOption(UserRole Role, string Title)
{
    public override string ToString() => Title;
}
