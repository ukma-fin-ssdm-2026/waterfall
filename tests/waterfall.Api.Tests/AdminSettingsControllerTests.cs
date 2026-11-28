// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести ролі Admin і системних налаштувань сервера; перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Admin;
using Waterfall.Api.Services;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Tests;

/// <summary>
/// Роль Admin: бачить і змінює лише системні налаштування сервера (фоновий збір та інтервал).
/// Сховище перевіряється на справжньому файлі й справжній конфігурації з перечитуванням.
/// </summary>
public sealed class AdminSettingsControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "waterfall-admin-" + Guid.NewGuid().ToString("N"));
    private readonly string _adminFile;
    private readonly IConfigurationRoot _configuration;
    private readonly FileSystemSettingsStore _store;

    public AdminSettingsControllerTests()
    {
        Directory.CreateDirectory(_directory);
        _adminFile = Path.Combine(_directory, FileSystemSettingsStore.FileName);
        // Як у Program.cs: базові значення, а файл адміністратора — останнім, щоб переважав
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ingestion:Enabled"] = "false",
                ["Ingestion:Interval"] = "00:30:00",
                ["Ingestion:DjinniPages"] = "1",
                ["Ingestion:RequestDelay"] = "00:00:02"
            })
            .AddJsonFile(_adminFile, optional: true, reloadOnChange: false)
            .Build();
        _store = new FileSystemSettingsStore(_configuration, _adminFile);
    }

    [Fact]
    public void Get_Admin_ReturnsCurrentServerSettings()
    {
        AdminSettingsController controller = Controller(UserRole.Admin);

        ActionResult<SystemSettingsDto> result = controller.Get();

        SystemSettingsDto settings = Assert.IsType<SystemSettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(settings.BackgroundParsingEnabled);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.Interval);
        Assert.Equal(1, settings.DjinniPages);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.RequestDelay);
    }

    [Theory]
    [InlineData(UserRole.JobSeeker)]
    [InlineData(UserRole.Analyst)]
    public async Task NonAdmin_GetsForbidden_AndNothingIsWritten(UserRole role)
    {
        AdminSettingsController controller = Controller(role);

        ObjectResult get = Assert.IsType<ObjectResult>(controller.Get().Result);
        ObjectResult put = Assert.IsType<ObjectResult>((await controller.Update(new UpdateSystemSettingsRequest
        {
            BackgroundParsingEnabled = true,
            Interval = TimeSpan.FromHours(1)
        })).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, get.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, put.StatusCode);
        Assert.False(File.Exists(_adminFile));
    }

    [Fact]
    public async Task Update_Admin_WritesAdminFile_AndServerSeesNewValuesAtOnce()
    {
        AdminSettingsController controller = Controller(UserRole.Admin);

        ActionResult<SystemSettingsDto> result = await controller.Update(new UpdateSystemSettingsRequest
        {
            BackgroundParsingEnabled = true,
            Interval = TimeSpan.FromDays(1)
        });

        SystemSettingsDto updated = Assert.IsType<SystemSettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(updated.BackgroundParsingEnabled);
        Assert.Equal(TimeSpan.FromDays(1), updated.Interval);
        // Фоновий збір читає саме цей ключ конфігурації
        Assert.True(_configuration.GetValue<bool>("Ingestion:Enabled"));
        string json = await File.ReadAllTextAsync(_adminFile);
        // Доба записується як 1.00:00:00: «24:00:00» .NET прочитав би як 24 дні
        Assert.Contains("\"1.00:00:00\"", json);
        Assert.False(File.Exists(_adminFile + ".tmp"));
    }

    [Fact]
    public async Task Update_KeepsOtherSettingsInAdminFile()
    {
        await File.WriteAllTextAsync(_adminFile, "{ \"VacancySync\": { \"AcceptClientVacancies\": false }, \"Ingestion\": { \"DjinniPages\": 2 } }");
        _configuration.Reload();

        await Controller(UserRole.Admin).Update(new UpdateSystemSettingsRequest { BackgroundParsingEnabled = false, Interval = TimeSpan.FromHours(2) });

        Assert.Equal(2, _configuration.GetValue<int>("Ingestion:DjinniPages"));
        Assert.False(_configuration.GetValue<bool>("VacancySync:AcceptClientVacancies", defaultValue: true));
        Assert.Equal(TimeSpan.FromHours(2), _store.Get().Interval);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60 * 24 * 2)]
    public async Task Update_IntervalOutOfRange_Returns400(int minutes)
    {
        ActionResult<SystemSettingsDto> result = await Controller(UserRole.Admin).Update(new UpdateSystemSettingsRequest
        {
            BackgroundParsingEnabled = true,
            Interval = TimeSpan.FromMinutes(minutes)
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(File.Exists(_adminFile));
    }

    private AdminSettingsController Controller(UserRole role)
    {
        AdminSettingsController controller = new(_store, new FakeLogger<AdminSettingsController>());
        Guid userId = Guid.NewGuid();
        ControllerTestContext.Setup(controller, currentUserId: userId);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
            "TestAuth"));
        return controller;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Тимчасова тека: якщо файл ще зайнятий, її прибере система
        }
    }
}
