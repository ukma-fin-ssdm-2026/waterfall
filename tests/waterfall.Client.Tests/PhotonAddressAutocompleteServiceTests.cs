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

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public FakeHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}
