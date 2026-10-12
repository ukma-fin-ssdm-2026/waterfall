// AI: юніт-тести парсера JSON-дампу Djinni (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Text.Json;
using Waterfall.Services.Ingestion;

namespace Waterfall.Ingestion.Tests;

public class DjinniRawJsonParserTests
{
    private readonly DjinniRawJsonParser _parser = new();

    [Fact]
    public void ValidDocument_ReturnsAllRecords()
    {
        RawParseResult result = _parser.Parse(RawData.DjinniDocument(RawData.Job("1"), RawData.Job("2")));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(["1", "2"], result.Items.Select(item => item.ExternalId));
        Assert.All(result.Items, item => Assert.Equal("djinni", item.Source));
        Assert.Empty(result.ItemErrors);
    }

    [Fact]
    public void NumericIdAndTagsArray_AreReadAsText()
    {
        string json = JsonSerializer.Serialize(new
        {
            source = "djinni",
            jobs = new object[] { new { id = 720033, title = "React Developer", tags = new[] { "React", "TypeScript" } } }
        });

        RawVacancy item = Assert.Single(_parser.Parse(json).Items);

        Assert.Equal("720033", item.ExternalId);
        Assert.Equal("React, TypeScript", item.Tags);
    }

    [Fact]
    public void PropertyNames_AreCaseInsensitive()
    {
        RawParseResult result = _parser.Parse("""{ "Source": "Djinni", "JOBS": [ { "Title": "QA Engineer", "ID": "7" } ] }""");

        RawVacancy item = Assert.Single(result.Items);
        Assert.Equal("QA Engineer", item.Title);
        Assert.Equal("7", item.ExternalId);
    }

    [Fact]
    public void NonObjectRecord_IsReportedWithoutLosingOthers()
    {
        RawParseResult result = _parser.Parse(RawData.DjinniDocument(RawData.Job("1"), "зламаний запис", RawData.Job("3")));

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains("запис №2", Assert.Single(result.ItemErrors));
    }

    [Theory]
    [InlineData("", "документ порожній")]
    [InlineData("{ \"source\": \"djinni\", \"jobs\": [", "некоректний JSON")]
    [InlineData("[ { \"title\": \"x\" } ]", "очікувався JSON-об'єкт")]
    [InlineData("{ \"source\": \"workua\", \"jobs\": [] }", "очікувалось джерело")]
    [InlineData("{ \"source\": \"djinni\" }", "немає масиву jobs")]
    [InlineData("{ \"source\": \"djinni\", \"jobs\": {} }", "немає масиву jobs")]
    public void InvalidDocument_IsRejectedWithReason(string content, string reason)
    {
        RawParseResult result = _parser.Parse(content);

        Assert.False(result.IsValid);
        Assert.Contains(reason, result.Error);
    }

    [Fact]
    public void OversizedDocument_IsRejected()
    {
        object[] jobs = Enumerable.Range(1, DjinniRawJsonParser.MaxItemsPerDocument + 1)
            .Select(i => (object)new { id = i.ToString() })
            .ToArray();

        RawParseResult result = _parser.Parse(RawData.DjinniDocument(jobs));

        Assert.False(result.IsValid);
        Assert.Contains("дозволено не більше", result.Error);
    }
}
