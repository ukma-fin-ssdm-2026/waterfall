// AI: тести фонового парсера дампів (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Waterfall.Api.Workers;
using Waterfall.Services.Interfaces;

namespace Waterfall.Ingestion.Tests;

public class JobParserServiceTests
{
    [Fact]
    public void ParseDump_ReadsJobs()
    {
        List<RawVacancy> jobs = JobParserService.ParseDump(File.ReadAllText(VacancyIngestionServiceTests.SampleDumpPath()));

        Assert.Equal(12, jobs.Count);
        Assert.Equal("720001", jobs[0].Id);
        Assert.Equal("$3500-4500", jobs[0].Salary);
    }

    [Theory]
    [InlineData("{ не json")]
    [InlineData("{ \"source\": \"workua\", \"jobs\": [] }")]
    public void ParseDump_RejectsBrokenOrForeignFiles(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JobParserService.ParseDump(json));
    }

    [Fact]
    public async Task BrokenFile_DoesNotStopOtherFiles()
    {
        string directory = Directory.CreateTempSubdirectory("waterfall-dumps-").FullName;
        try
        {
            // Файли обробляються за алфавітом, тож зламаний іде першим
            File.WriteAllText(Path.Combine(directory, "a-broken.json"), "{ не json");
            File.WriteAllText(Path.Combine(directory, "b-ok.json"), "{ \"source\": \"djinni\", \"jobs\": [ { \"id\": \"1\" } ] }");

            RecordingIngestionService ingestion = new();
            JobParserService parser = CreateParser(ingestion, directory);

            await parser.StartAsync(CancellationToken.None);
            await ingestion.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await parser.StopAsync(CancellationToken.None);

            Assert.Equal("1", Assert.Single(ingestion.Received).Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JobParserService CreateParser(IVacancyIngestionService ingestion, string directory)
    {
        ServiceProvider services = new ServiceCollection()
            .AddScoped(_ => ingestion)
            .BuildServiceProvider();

        IngestionOptions options = new() { Enabled = true, RawDataDirectory = directory, Interval = TimeSpan.FromHours(1) };

        return new JobParserService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            new HostingEnvironment { ContentRootPath = directory },
            NullLogger<JobParserService>.Instance);
    }

    /// <summary>Замість справжнього сервісу запам'ятовує, які вакансії йому передали.</summary>
    private sealed class RecordingIngestionService : IVacancyIngestionService
    {
        public List<RawVacancy> Received { get; } = [];

        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IngestionResult> ProcessRawVacanciesAsync(IReadOnlyList<RawVacancy> rawItems, CancellationToken ct = default)
        {
            Received.AddRange(rawItems);
            Called.TrySetResult();
            return Task.FromResult(new IngestionResult(rawItems.Count, 0, 0));
        }
    }
}
