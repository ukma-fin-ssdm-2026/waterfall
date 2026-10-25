// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Client.Services;

namespace Waterfall.Client.Tests;

public class PhotonAddressAutocompleteServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public async Task GetSuggestionsAsync_ReturnsEmpty_ForShortOrEmptyQueries(string query)
    {
        PhotonAddressAutocompleteService service = new();
        IReadOnlyList<string> result = await service.GetSuggestionsAsync(query);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ParsesPhotonResponseCorrectly()
    {
        string mockJson = """
        {
            "type": "FeatureCollection",
            "features": [
                {
                    "type": "Feature",
                    "properties": {
                        "name": "БЦ Парус",
                        "street": "вулиця Мечникова",
                        "housenumber": "2",
                        "city": "Київ"
                    }
                },
                {
                    "type": "Feature",
                    "properties": {
                        "name": "Спортивна площа",
                        "housenumber": "1а",
                        "city": "Київ"
                    }
                }
            ]
        }
        """;

        FakeHttpMessageHandler handler = new(mockJson);
        HttpClient httpClient = new(handler);
        PhotonAddressAutocompleteService service = new(httpClient);

        IReadOnlyList<string> suggestions = await service.GetSuggestionsAsync("Київ Мечникова");

        Assert.NotEmpty(suggestions);
        Assert.Contains("Київ, вулиця Мечникова, 2", suggestions);
        Assert.Contains("Київ, Спортивна площа", suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ParsesTownAndVillageCorrectly()
    {
        string mockJson = """
        {
            "type": "FeatureCollection",
            "features": [
                {
                    "type": "Feature",
                    "properties": {
                        "name": "Центральна площа",
                        "street": "вулиця Шевченка",
                        "housenumber": "10",
                        "town": "Ірпінь"
                    }
                },
                {
                    "type": "Feature",
                    "properties": {
                        "name": "Парк Слави",
                        "village": "Коцюбинське"
                    }
                }
            ]
        }
        """;

        FakeHttpMessageHandler handler = new(mockJson);
        HttpClient httpClient = new(handler);
        PhotonAddressAutocompleteService service = new(httpClient);

        IReadOnlyList<string> suggestions = await service.GetSuggestionsAsync("Шевченка");

        Assert.Contains("Ірпінь, вулиця Шевченка, 10", suggestions);
        Assert.Contains("Коцюбинське, Парк Слави", suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_FallsBackToWikipediaOpenSearch_WhenPhotonFails()
    {
        // When Photon URL fails, it calls Wikipedia opensearch:
        string wikiOpenSearchJson = """
        [
            "Хрещатик",
            [
                "Вулиця Мечникова (Київ)",
                "Вулиця Садова (Львів)",
                "Вулиця Сумська (Харків)",
                "Вулиця Дерибасівська (Одеса)",
                "Проспект Дмитра Яворницького (Дніпро)"
            ]
        ]
        """;

        FakeHttpMessageHandler handler = new(wikiOpenSearchJson, throwOnPhoton: true);
        HttpClient httpClient = new(handler);
        PhotonAddressAutocompleteService service = new(httpClient);

        IReadOnlyList<string> suggestions = await service.GetSuggestionsAsync("Хрещатик");

        Assert.NotEmpty(suggestions);
        Assert.Contains("Київ, Вулиця Мечникова", suggestions);
        Assert.Contains("Львів, Вулиця Садова", suggestions);
        Assert.Contains("Харків, Вулиця Сумська", suggestions);
        Assert.Contains("Одеса, Вулиця Дерибасівська", suggestions);
        Assert.Contains("Дніпро, Проспект Дмитра Яворницького", suggestions);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly bool _throwOnPhoton;

        public FakeHttpMessageHandler(string responseContent, bool throwOnPhoton = false)
        {
            _responseContent = responseContent;
            _throwOnPhoton = throwOnPhoton;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwOnPhoton && request.RequestUri != null && request.RequestUri.Host.Contains("photon"))
            {
                throw new HttpRequestException("Photon unavailable");
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}
