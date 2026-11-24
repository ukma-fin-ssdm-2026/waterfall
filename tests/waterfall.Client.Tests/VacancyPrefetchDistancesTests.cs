using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

/// <summary>
/// Поведінка фонового префетчу відстаней: він існує лише для того, щоб картки
/// з точною адресою офісу в місті користувача вже мали відстань під час скролу.
/// Для решти карток жодної роботи (і жодного звернення до сервісу) бути не повинно.
/// </summary>
public sealed class VacancyPrefetchDistancesTests
{
    private static readonly VacancyItemViewModel[] NoCards = [];

    [Fact]
    public async Task Prefetch_DoesNotAskDistanceService_WhenAddressHasNoStreet()
    {
        RecordingDistanceService distance = new("Київ, вул. Лесі Українки 1");
        VacancyItemViewModel card = MakeCard(
            "Київ, не вказано точну адресу",
            fullAddress: "Київ, не вказано точну адресу",
            distance);

        await VacancyFilterEngine.PrefetchDistancesAsync(
            [card], distance, CancellationToken.None, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Empty(distance.Requests);
        Assert.Null(card.DistanceText);
    }

    [Fact]
    public async Task Prefetch_DoesNotAskDistanceService_WhenVacancyIsInAnotherCity()
    {
        RecordingDistanceService distance = new("Київ, вул. Лесі Українки 1");
        VacancyItemViewModel card = MakeCard(
            "Львів, вул. Садова 5",
            fullAddress: "Львів, вул. Садова 5",
            distance);

        await VacancyFilterEngine.PrefetchDistancesAsync(
            [card], distance, CancellationToken.None, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Empty(distance.Requests);
        Assert.Null(card.DistanceText);
    }

    [Fact]
    public async Task Prefetch_AsksDistanceService_ForExactAddressInUserCity()
    {
        RecordingDistanceService distance = new("Київ, вул. Лесі Українки 1", 1.2);
        VacancyItemViewModel card = MakeCard(
            "Київ, вул. Хрещатик 22",
            fullAddress: "Київ, вул. Хрещатик 22",
            distance);

        await VacancyFilterEngine.PrefetchDistancesAsync(
            [card], distance, CancellationToken.None, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Equal(["Київ, вул. Хрещатик 22"], distance.Requests);
        Assert.Equal("1.2 км від вас", card.DistanceText);
    }

    [Fact]
    public async Task Prefetch_DoesNothing_WhenUserAddressIsEmpty()
    {
        RecordingDistanceService distance = new(userAddress: null, km: 1.2);
        VacancyItemViewModel card = MakeCard(
            "Київ, вул. Хрещатик 22",
            fullAddress: "Київ, вул. Хрещатик 22",
            distance);

        await VacancyFilterEngine.PrefetchDistancesAsync(
            [card], distance, CancellationToken.None, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Empty(distance.Requests);
    }

    [Fact]
    public async Task Prefetch_StopsQuietly_WhenCancelled()
    {
        RecordingDistanceService distance = new("Київ, вул. Лесі Українки 1");
        VacancyItemViewModel card = MakeCard(
            "Київ, вул. Хрещатик 22",
            fullAddress: "Київ, вул. Хрещатик 22",
            distance);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await VacancyFilterEngine.PrefetchDistancesAsync(
            [card], distance, cts.Token, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Empty(distance.Requests);
    }

    [Fact]
    public async Task Prefetch_WithNoCards_DoesNotThrow()
    {
        RecordingDistanceService distance = new("Київ, вул. Лесі Українки 1");

        await VacancyFilterEngine.PrefetchDistancesAsync(
            NoCards, distance, CancellationToken.None, initialDelayMs: 0, interCardDelayMs: 0);

        Assert.Empty(distance.Requests);
    }

    private static VacancyItemViewModel MakeCard(
        string location,
        string? fullAddress,
        IDistanceCalculatorService distance) =>
        new(
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "prefetch-1",
                Title = "Senior .NET",
                Company = "Tech",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/prefetch-1",
                Location = location,
                FullAddress = fullAddress
            },
            new FakeGateway(),
            new StrongReferenceMessenger(),
            _ => { },
            distanceService: distance);

    /// <summary>
    /// Сервіс відстаней — зовнішня межа (мережа геокодування), тому в тестах він
    /// замінений записуючим дублем: єдине, що спостерігаємо, — чи був запит і яка відстань.
    /// </summary>
    private sealed class RecordingDistanceService : IDistanceCalculatorService
    {
        private readonly double? _km;

        public RecordingDistanceService(string? userAddress, double? km = null)
        {
            UserAddress = userAddress;
            _km = km;
        }

        public string? UserAddress { get; set; }

        public List<string> Requests { get; } = [];

        public Task<double?> CalculateDistanceKmAsync(string destinationAddress, CancellationToken ct = default)
        {
            Requests.Add(destinationAddress);
            return Task.FromResult(_km);
        }
    }
}
