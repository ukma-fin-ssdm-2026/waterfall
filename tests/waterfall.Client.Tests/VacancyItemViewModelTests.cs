// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести всіх гілок SalaryText/SourceText, CompanyLine, StatusText (Lab 06); перевірено: dotnet test, звіт покриття.

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class VacancyItemViewModelTests
{
    private readonly FakeGateway _gateway = new();
    private readonly List<string> _errors = [];
    private readonly List<VacancySaveChangedMessage> _sent = [];
    private readonly StrongReferenceMessenger _messenger = new();

    public VacancyItemViewModelTests()
    {
        _messenger.Register<VacancySaveChangedMessage>(this, (_, m) => _sent.Add(m));
    }

    private VacancyItemViewModel Item(
        bool isSaved = false, decimal? min = null, decimal? max = null,
        JobSource source = JobSource.Unknown, bool isActive = true,
        string company = "Genesis", string location = "", string title = "Senior .NET") =>
        new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            IsSaved = isSaved,
            SalaryMin = min,
            SalaryMax = max,
            Currency = "USD",
            Source = source,
            IsActive = isActive,
            Company = company,
            Location = location,
            Title = title,
            Description = "Опис",
            Skills = ["C#", "Kafka"]
        }, _gateway, _messenger, _errors.Add);

    [Theory]
    [InlineData(4000, 5500, "4\u00A0000–5\u00A0500 USD")]
    [InlineData(4000, 4000, "4\u00A0000 USD")]
    [InlineData(4000, null, "від 4\u00A0000 USD")]
    [InlineData(null, 5500, "до 5\u00A0500 USD")]
    [InlineData(null, null, null)]
    public void SalaryText_CoversAllRangeShapes(int? min, int? max, string? expected)
    {
        VacancyItemViewModel item = Item(min: (decimal?)min, max: (decimal?)max);

        Assert.Equal(expected, item.SalaryText);
        Assert.Equal(expected is not null, item.HasSalary);
    }

    [Theory]
    [InlineData(JobSource.WorkUa, "Work.ua")]
    [InlineData(JobSource.RobotaUa, "Robota.ua")]
    [InlineData(JobSource.Djinni, "Djinni")]
    [InlineData(JobSource.Dou, "DOU")]
    [InlineData(JobSource.Unknown, null)]
    public void SourceText_MapsEverySource(JobSource source, string? expected)
    {
        Assert.Equal(expected, Item(source: source).SourceText);
    }

    [Theory]
    [InlineData("", "Genesis")]
    [InlineData("   ", "Genesis")]
    [InlineData("Київ", "Genesis · Київ")]
    public void CompanyLine_HandlesMissingLocation(string location, string expected)
    {
        Assert.Equal(expected, Item(location: location).CompanyLine);
    }

    [Theory]
    [InlineData(true, "Активна")]
    [InlineData(false, "Архівна")]
    public void StatusText_ReflectsActivity(bool isActive, string expected)
    {
        VacancyItemViewModel item = Item(isActive: isActive);

        Assert.Equal(expected, item.StatusText);
        Assert.Equal(isActive, item.IsActive);
    }

    [Theory]
    [InlineData(false, "save", true)]
    [InlineData(true, "unsave", false)]
    public async Task Toggle_CallsServerAndFlipsState(bool initiallySaved, string expectedAction, bool expectedSaved)
    {
        VacancyItemViewModel item = Item(isSaved: initiallySaved);

        await item.ToggleSaveCommand.ExecuteAsync(null);

        Assert.Equal((expectedAction, item.Id), Assert.Single(_gateway.ToggleCalls));
        Assert.Equal(expectedSaved, item.IsSaved);
        Assert.Equal(expectedSaved ? "★ Збережено" : "☆ Зберегти", item.SaveButtonText);
        Assert.Equal(new VacancySaveChangedMessage(item.Id, expectedSaved), Assert.Single(_sent));
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Conflict, true)]
    [InlineData(true, HttpStatusCode.NotFound, false)]
    public async Task Toggle_ServerAlreadyInTargetState_SyncsWithoutError(bool initiallySaved, HttpStatusCode status, bool expectedSaved)
    {
        VacancyItemViewModel item = Item(isSaved: initiallySaved);
        _gateway.ToggleError = new ApiException(status, null, "already");

        await item.ToggleSaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedSaved, item.IsSaved);
        Assert.Empty(_errors);
        Assert.Equal(new VacancySaveChangedMessage(item.Id, expectedSaved), Assert.Single(_sent));
    }

    [Fact]
    public async Task Toggle_RealFailure_KeepsStateAndReportsMessage()
    {
        VacancyItemViewModel item = Item(isSaved: false);
        _gateway.ToggleError = new ApiException(HttpStatusCode.NotFound, null, "Вакансію не знайдено.");

        await item.ToggleSaveCommand.ExecuteAsync(null);

        Assert.False(item.IsSaved);
        Assert.Equal(["Вакансію не знайдено."], _errors);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task OpenSource_WithValidUrl_CallsLauncher()
    {
        FakeUrlLauncher launcher = new();
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET",
            Description = "Опис",
            Company = "Tech",
            SourceUrl = "https://djinni.co/jobs/720001-senior-net-developer/"
        }, _gateway, _messenger, _errors.Add, launcher);

        await item.OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://djinni.co/jobs/720001-senior-net-developer/"), launcher.LaunchedUri);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task OpenSource_WhenLauncherFails_ReportsError()
    {
        FakeUrlLauncher launcher = new() { ReturnResult = false };
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET",
            Description = "Опис",
            Company = "Tech",
            SourceUrl = "https://djinni.co/jobs/720001-senior-net-developer/"
        }, _gateway, _messenger, _errors.Add, launcher);

        await item.OpenSourceCommand.ExecuteAsync(null);

        Assert.Equal(["Не вдалося відкрити посилання у браузері."], _errors);
    }

    [Fact]
    public async Task OpenSource_WhenNoUrl_DoesNotCallLauncher()
    {
        FakeUrlLauncher launcher = new();
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET",
            Description = "Опис",
            Company = "Tech",
            SourceUrl = ""
        }, _gateway, _messenger, _errors.Add, launcher);

        await item.OpenSourceCommand.ExecuteAsync(null);

        Assert.Null(launcher.LaunchedUri);
        Assert.Empty(_errors);
    }

    [Fact]
    public void AddressDisplay_PrefersFullAddressOverLocation()
    {
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Backend Dev",
            Description = "Desc",
            Location = "Київ",
            FullAddress = "вул. Володимирська, 10, Київ"
        }, _gateway, _messenger, _errors.Add);

        Assert.Equal("вул. Володимирська, 10, Київ", item.AddressDisplay);
        Assert.True(item.HasAddress);
    }

    [Fact]
    public void AddressDisplay_FallsBackToLocation_WhenFullAddressEmpty()
    {
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Backend Dev",
            Description = "Desc",
            Location = "Дніпро",
            FullAddress = null
        }, _gateway, _messenger, _errors.Add);

        Assert.Equal("Дніпро, не вказано точну адресу", item.AddressDisplay);
        Assert.True(item.HasAddress);
    }

    [Theory]
    [InlineData(1.5, "1.5 км від вас")]
    [InlineData(0.8, "800 м від вас")]
    [InlineData(null, null)]
    public async Task RefreshDistance_FormatsDistanceCorrectly(double? km, string? expectedText)
    {
        FakeDistanceService service = new() { DistanceToReturn = km };
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Backend Dev",
            Description = "Desc",
            Location = "Київ",
            FullAddress = "вул. Мечникова, 2"
        }, _gateway, _messenger, _errors.Add, distanceService: service);

        await item.RefreshDistanceAsync();

        Assert.Equal(expectedText, item.DistanceText);
        Assert.Equal(expectedText is not null, item.HasDistanceText);
    }

    [Fact]
    public async Task Messenger_UserAddressChangedMessage_RefreshesDistance()
    {
        FakeDistanceService service = new() { DistanceToReturn = 2.0 };
        VacancyItemViewModel item = new(new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = "Backend Dev",
            Description = "Desc",
            FullAddress = "вул. Ярославів Вал, 1"
        }, _gateway, _messenger, _errors.Add, distanceService: service);

        await item.RefreshDistanceAsync();
        Assert.Equal("2.0 км від вас", item.DistanceText);

        service.DistanceToReturn = 4.2;
        _messenger.Send(new UserAddressChangedMessage("вул. Саксаганського, 40"));

        // Messenger triggers RefreshDistanceAsync asynchronously; give brief time
        await Task.Delay(50);
        Assert.Equal("4.2 км від вас", item.DistanceText);

        // Коли користувач виходить з системи — адреса очищується, відстань зникає з картки
        service.UserAddress = null;
        _messenger.Send(new UserAddressChangedMessage(null));
        await Task.Delay(50);
        Assert.Null(item.DistanceText);
        Assert.False(item.HasDistanceText);
    }

    private sealed class FakeDistanceService : IDistanceCalculatorService
    {
        public string? UserAddress { get; set; } = "вул. Хрещатик, 22, Київ";
        public double? DistanceToReturn { get; set; } = 1.5;

        public Task<double?> CalculateDistanceKmAsync(string destinationAddress, CancellationToken ct = default)
        {
            return Task.FromResult(DistanceToReturn);
        }
    }

    private sealed class FakeUrlLauncher : Waterfall.Client.Interfaces.IUrlLauncher
    {
        public Uri? LaunchedUri { get; private set; }
        public bool ReturnResult { get; set; } = true;
        public Exception? ExceptionToThrow { get; set; }

        public Task<bool> OpenAsync(Uri uri)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            LaunchedUri = uri;
            return Task.FromResult(ReturnResult);
        }
    }
}

