// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class LoginViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeGateway _gateway = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly LoginViewModel _vm;

    public LoginViewModelTests()
    {
        _vm = new LoginViewModel(_gateway, _tokens, _time);
    }

    /// <summary>
    /// JWT у форматі сервера (#67): лише payload має значення, підпис клієнт не перевіряє.
    /// </summary>
    private static string Jwt(string payloadJson)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payloadJson)}.sig";
    }

    private static long Unix(DateTimeOffset t) => t.ToUnixTimeSeconds();

    [Theory]
    [InlineData("", "pw", false, "Введіть коректну електронну адресу.")]
    [InlineData("no-at-sign", "pw", false, "Введіть коректну електронну адресу.")]
    [InlineData("a@b.c", "", false, "Введіть пароль.")]
    [InlineData("a@b.c", "x", false, null)]
    [InlineData("a@b.c", "short1", true, "Пароль має містити від 12 до 32 символів.")]
    [InlineData("a@b.c", "abcdefghijk1abcdefghijk1abcdefghi", true, "Пароль має містити від 12 до 32 символів.")]
    [InlineData("a@b.c", "abcdefghijkl", true, "Пароль має містити хоча б одну цифру.")]
    [InlineData("a@b.c", "abcdefghijk1", true, null)]
    [InlineData("a@b.c", "abcdefghijk1abcdefghijk1abcdefgh", true, null)]
    public void Validate_RegisterEnforcesServerPolicy_LoginDoesNot(string email, string password, bool register, string? expected)
    {
        Assert.Equal(expected, LoginViewModel.Validate(email, password, register));
    }

    [Fact]
    public async Task InvalidInput_DoesNotCallServer()
    {
        _vm.IsRegisterMode = true;
        _vm.Email = "a@b.c";
        _vm.Password = "tooshort";

        await _vm.SubmitCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.AuthRequests);
        Assert.True(_vm.HasError);
        Assert.False(_vm.IsLoggedIn);
    }

    [Theory]
    [InlineData(false, "Вхід", "Увійти", "Немає облікового запису? Зареєструватися", null, "")]
    [InlineData(true, "Реєстрація", "Зареєструватися", "Вже маєте обліковий запис? Увійти", "Від 12 до 32 символів, хоча б одна цифра", "")]
    public void ModeSwitch_UpdatesAllTexts(bool register, string form, string submit, string switchBack,
        string? hint, string sessionText)
    {
        _vm.IsRegisterMode = register;

        Assert.Equal((form, submit), (_vm.FormTitle, _vm.SubmitText));
        Assert.Contains(switchBack, _vm.SwitchModeText);
        Assert.Equal(hint, _vm.PasswordHint);
        Assert.Equal(sessionText, _vm.SessionText);
        Assert.Equal(sessionText, _vm.UserEmail);
        Assert.Equal(sessionText, _vm.UserRoleText);
    }

    [Fact]
    public void RoleOption_ToStringShowsTitle()
    {
        Assert.Equal("Шукач роботи", _vm.Roles[0].ToString());
    }

    [Fact]
    public async Task SessionText_ShowsLocalExpiryTime()
    {
        _gateway.Auth = () => new AuthResponse { Token = "t", Email = "kosta@ukma.edu.ua", Role = UserRole.JobSeeker, ExpiresAt = Now.AddHours(1) };
        _vm.Email = "kosta@ukma.edu.ua";
        _vm.Password = "anything";
        await _vm.SubmitCommand.ExecuteAsync(null);

        string expected = _vm.Session!.ExpiresAt.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));

        Assert.Equal($"Сесія дійсна до {expected}", _vm.SessionText);
    }

    [Fact]
    public async Task SwitchMode_ClearsError() 
    {
        _vm.IsRegisterMode = true;
        _vm.Email = "a@b.c";
        _vm.Password = "tooshort";
        await _vm.SubmitCommand.ExecuteAsync(null);
        Assert.True(_vm.HasError);

        _vm.SwitchModeCommand.Execute(null);

        Assert.False(_vm.HasError);
        Assert.False(_vm.IsRegisterMode);
    }

    [Fact]
    public async Task Register_SendsTrimmedEmailAndChosenRole_ThenShowsSession()
    {
        _gateway.Auth = () => new AuthResponse { Token = "t", Email = "kosta@ukma.edu.ua", Role = UserRole.Analyst, ExpiresAt = Now.AddMinutes(15) };
        _vm.SwitchModeCommand.Execute(null);
        _vm.Email = "  kosta@ukma.edu.ua ";
        _vm.Password = "correcthorse1";
        _vm.SelectedRole = _vm.Roles.Single(r => r.Role == UserRole.Analyst);

        await _vm.SubmitCommand.ExecuteAsync(null);

        RegisterRequest sent = Assert.IsType<RegisterRequest>(Assert.Single(_gateway.AuthRequests));
        Assert.Equal(("kosta@ukma.edu.ua", UserRole.Analyst), (sent.Email, sent.Role));
        Assert.True(_vm.IsLoggedIn);
        Assert.Equal("kosta@ukma.edu.ua", _vm.UserEmail);
        Assert.Equal("Аналітик ринку", _vm.UserRoleText);
        Assert.Equal(string.Empty, _vm.Password);
        Assert.False(_vm.IsBusy);
    }

    [Fact]
    public async Task LoginMode_SendsLoginRequest()
    {
        _gateway.Auth = () => new AuthResponse { Token = "t", Email = "a@b.c", ExpiresAt = Now.AddMinutes(15) };
        _vm.Email = "a@b.c";
        _vm.Password = "anything";

        await _vm.SubmitCommand.ExecuteAsync(null);

        Assert.IsType<LoginRequest>(Assert.Single(_gateway.AuthRequests));
        Assert.Equal("Шукач роботи", _vm.UserRoleText);
    }

    [Fact]
    public async Task ServerRejection_ShowsMessage_KeepsPasswordForCorrection()
    {
        _gateway.Auth = () => throw new ApiException(HttpStatusCode.Unauthorized, null, "Невірний email або пароль.");
        _vm.Email = "a@b.c";
        _vm.Password = "wrong";

        await _vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Невірний email або пароль.", _vm.ErrorMessage);
        Assert.False(_vm.IsLoggedIn);
        Assert.Equal("wrong", _vm.Password);
    }

    [Fact]
    public async Task ValidationErrorWithDetails_ShowsFieldMessages()
    {
        ApiError error = new()
        {
            Code = "VALIDATION_ERROR",
            Message = "Невалідні дані запиту реєстрації.",
            Details = [new ApiValidationError { Field = "email", Message = "Поле email повинно бути валідною електронною адресою" }]
        };
        _gateway.Auth = () => throw new ApiException(HttpStatusCode.BadRequest, error, error.Message);
        _vm.IsRegisterMode = true;
        _vm.Email = "a@b";
        _vm.Password = "correcthorse1";

        await _vm.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Поле email повинно бути валідною електронною адресою", _vm.ErrorMessage);
    }

    [Fact]
    public async Task Logout_ClearsSessionThroughGateway()
    {
        _gateway.Auth = () => new AuthResponse { Token = "t", Email = "a@b.c", ExpiresAt = Now.AddMinutes(15) };
        _vm.Email = "a@b.c";
        _vm.Password = "pw";
        await _vm.SubmitCommand.ExecuteAsync(null);

        await _vm.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(1, _gateway.LogoutCalls);
        Assert.True(_vm.IsLoggedOut);
    }

    [Fact]
    public async Task Restore_ValidToken_ShowsUserFromClaims()
    {
        _tokens.Token = Jwt($$"""{"sub":"u1","email":"kosta@ukma.edu.ua","role":"Analyst","exp":{{Unix(Now.AddMinutes(10))}}}""");

        await _vm.RestoreSessionCommand.ExecuteAsync(null);

        Assert.True(_vm.IsLoggedIn);
        Assert.Equal("kosta@ukma.edu.ua", _vm.UserEmail);
        Assert.Equal(UserRole.Analyst, _vm.Session!.Role);
        Assert.NotNull(_tokens.Token);
    }

    [Fact]
    public async Task Restore_LongFormRoleClaim_IsRecognized()
    {
        _tokens.Token = Jwt($$"""{"email":"a@b.c","http://schemas.microsoft.com/ws/2008/06/identity/claims/role":"Analyst","exp":{{Unix(Now.AddMinutes(10))}}}""");

        await _vm.RestoreSessionCommand.ExecuteAsync(null);

        Assert.Equal(UserRole.Analyst, _vm.Session?.Role);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Restore_ExpiredToken_LogsOutAndClearsStorage(int minutesFromNow)
    {
        _tokens.Token = Jwt($$"""{"email":"a@b.c","role":"JobSeeker","exp":{{Unix(Now.AddMinutes(minutesFromNow))}}}""");

        await _vm.RestoreSessionCommand.ExecuteAsync(null);

        Assert.True(_vm.IsLoggedOut);
        Assert.Null(_tokens.Token);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.%%%.c")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1MSJ9.sig")]
    public async Task Restore_UnreadableToken_LogsOutAndClearsStorage(string token)
    {
        _tokens.Token = token;

        await _vm.RestoreSessionCommand.ExecuteAsync(null);

        Assert.True(_vm.IsLoggedOut);
        Assert.Null(_tokens.Token);
    }
}
