// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: налаштування фонового збору через сервер і лише для ролі Admin; перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Net;
using System.Text;
using System.Text.Json;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.SystemSettings;
using Waterfall.Client.ViewModels;
using Xunit;

namespace Waterfall.Client.Tests;

public class SystemSettingsViewModelTests
{
    private readonly FakeGateway _gateway = new();
    private readonly InMemoryTokenStore _tokens = new();

    private static string Jwt(UserRole role)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = "user@test.com",
            ["role"] = role.ToString(),
            ["exp"] = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds()
        });
        return $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payload)}.sig";
    }

    private SystemSettingsViewModel CreateVm() => new(_gateway, _tokens, localVacancyStore: null, vacancySyncService: null);

    [Fact]
    public void DefaultState_HasBackgroundParsingDisabled_AndThirtyMinutes()
    {
        SystemSettingsViewModel vm = new();

        // Вимога: по дефолту фолз
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("Вимкнено", vm.StatusLabelText);
        Assert.Contains("Вимкнено", vm.ParsingStatusBadgeText);
        Assert.Equal("00:30:00", vm.SelectedInterval);
        Assert.Equal("30 хвилин (00:30:00)", vm.SelectedIntervalDisplay);
        Assert.Contains("30 хвилин (00:30:00)", vm.IntervalOptions);
        Assert.False(vm.IsAdmin);
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(UserRole.JobSeeker)]
    [InlineData(UserRole.Analyst)]
    public async Task NonAdmin_DoesNotSeeServerSettings_AndCannotSave(UserRole role)
    {
        await _tokens.SetTokenAsync(Jwt(role));
        _gateway.SystemSettings = _gateway.SystemSettings with { BackgroundParsingEnabled = true };
        SystemSettingsViewModel vm = CreateVm();

        await vm.LoadServerSettingsAsync();

        Assert.False(vm.IsAdmin);
        Assert.True(vm.IsNotAdmin);
        // Налаштування сервера навіть не запитуються
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.False(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task NotLoggedIn_IsTreatedAsNonAdmin()
    {
        SystemSettingsViewModel vm = CreateVm();

        await vm.LoadServerSettingsAsync();

        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public async Task Admin_LoadsSettingsFromServer()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        _gateway.SystemSettings = new SystemSettingsDto
        {
            BackgroundParsingEnabled = true,
            Interval = TimeSpan.FromHours(2),
            DjinniPages = 1,
            WorkUaPages = 2,
            RobotaUaPages = 0,
            DouPages = 1,
            RequestDelay = TimeSpan.FromSeconds(3)
        };
        SystemSettingsViewModel vm = CreateVm();

        await vm.LoadServerSettingsAsync();

        Assert.True(vm.IsAdmin);
        Assert.True(vm.IsBackgroundParsingEnabled);
        Assert.Equal("02:00:00", vm.SelectedInterval);
        Assert.Equal("2 години (02:00:00)", vm.SelectedIntervalDisplay);
        Assert.Equal(2, vm.WorkUaPages);
        Assert.Equal(0, vm.RobotaUaPages);
        Assert.Equal("00:00:03", vm.RequestDelay);
        Assert.True(vm.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Admin_SavesToggleAndInterval_ToServer()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        SystemSettingsViewModel vm = CreateVm();
        await vm.LoadServerSettingsAsync();

        vm.ToggleBackgroundParsingCommand.Execute(null);
        vm.SelectedIntervalDisplay = "1 година (01:00:00)";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        UpdateSystemSettingsRequest sent = Assert.Single(_gateway.SystemSettingsUpdates);
        Assert.True(sent.BackgroundParsingEnabled);
        Assert.Equal(TimeSpan.FromHours(1), sent.Interval);
        Assert.True(vm.IsSuccessStatus);
        Assert.Contains("збережено на сервері", vm.StatusMessage);
    }

    [Fact]
    public async Task DayInterval_IsSentAsOneDay_NotTwentyFourDays()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        SystemSettingsViewModel vm = CreateVm();
        await vm.LoadServerSettingsAsync();

        vm.SelectedIntervalDisplay = "24 години (1.00:00:00)";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(TimeSpan.FromDays(1), Assert.Single(_gateway.SystemSettingsUpdates).Interval);
        Assert.Equal("24 години (1.00:00:00)", vm.SelectedIntervalDisplay);
    }

    [Fact]
    public async Task ServerRejectsAsForbidden_HidesSettingsAndExplains()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        SystemSettingsViewModel vm = CreateVm();
        await vm.LoadServerSettingsAsync();
        // Роль на сервері змінили, а токен ще старий
        _gateway.SystemSettingsError = new ApiException(HttpStatusCode.Forbidden, null, "Forbidden");

        vm.ToggleBackgroundParsingCommand.Execute(null);
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(vm.IsAdmin);
        Assert.False(vm.IsSuccessStatus);
        Assert.Contains("лише адміністратор", vm.StatusMessage);
    }

    [Fact]
    public async Task UnknownIntervalFromServer_IsShownAsIs()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        _gateway.SystemSettings = _gateway.SystemSettings with { Interval = TimeSpan.FromMinutes(45) };
        SystemSettingsViewModel vm = CreateVm();

        await vm.LoadServerSettingsAsync();

        Assert.Equal("00:45:00", vm.SelectedInterval);
        Assert.Contains("00:45:00 (інтервал)", vm.IntervalOptions);
    }

    [Fact]
    public async Task Admin_SavingUnchangedValues_DoesNotCallServer()
    {
        // Сторінка зберігає автоматично, зокрема коли перемикач змінило саме завантаження з сервера
        await _tokens.SetTokenAsync(Jwt(UserRole.Admin));
        SystemSettingsViewModel vm = CreateVm();
        await vm.LoadServerSettingsAsync();

        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.SystemSettingsUpdates);
    }

    [Fact]
    public async Task NonAdmin_SaveCalledDirectly_DoesNothing()
    {
        await _tokens.SetTokenAsync(Jwt(UserRole.JobSeeker));
        SystemSettingsViewModel vm = CreateVm();
        await vm.LoadServerSettingsAsync();
        vm.IsBackgroundParsingEnabled = true;

        await vm.SaveSettingsAsync();

        Assert.Empty(_gateway.SystemSettingsUpdates);
    }

    [Fact]
    public void ToggleBackgroundParsingCommand_TogglesFlag()
    {
        SystemSettingsViewModel vm = new();

        vm.ToggleBackgroundParsingCommand.Execute(null);
        Assert.True(vm.IsBackgroundParsingEnabled);
        Assert.Contains("Активно", vm.ParsingStatusBadgeText);

        vm.ToggleBackgroundParsingCommand.Execute(null);
        Assert.False(vm.IsBackgroundParsingEnabled);
    }

    [Fact]
    public void ResetToDefaults_ResetsOnScreen_WithoutCallingServer()
    {
        SystemSettingsViewModel vm = CreateVm();
        vm.IsBackgroundParsingEnabled = true;
        vm.SelectedIntervalDisplay = "4 години (04:00:00)";

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("00:30:00", vm.SelectedInterval);
        Assert.Empty(_gateway.SystemSettingsUpdates);
    }

    [Fact]
    public void DismissStatus_HidesBanner_WithoutResettingSettings()
    {
        SystemSettingsViewModel vm = new() { IsBackgroundParsingEnabled = true, StatusMessage = "щось" };

        vm.DismissStatusCommand.Execute(null);

        Assert.False(vm.HasStatusMessage);
        Assert.True(vm.IsBackgroundParsingEnabled);
    }
}
