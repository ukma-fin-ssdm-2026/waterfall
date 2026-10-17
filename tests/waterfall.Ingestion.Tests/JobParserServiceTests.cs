// AI: тести фонового парсера дампів (issue #62, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Waterfall.Api.Workers;
using Waterfall.Services.Interfaces;

namespace Waterfall.Ingestion.Tests;

public sealed class JobParserServiceTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private const string ValidDump = "{ \"source\": \"djinni\", \"jobs\": [ { \"id\": \"1\" } ] }";

    // Фікстура: окремий тимчасовий каталог з дампами на кожен тест
    private readonly string _directory = Directory.CreateTempSubdirectory("waterfall-dumps-").FullName;
    private readonly RecordingIngestionService _ingestion = new();
    private readonly RecordingLogger _logger = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ParseDump_ReadsJobs()
    {
        List<RawVacancy> jobs = JobParserService.ParseDump(File.ReadAllText(VacancyIngestionServiceTests.SampleDumpPath()));

        Assert.Equal(12, jobs.Count);
        Assert.Equal("720001", jobs[0].Id);
        Assert.Equal("$3500-4500", jobs[0].Salary);
    }

    [Fact]
    public void ParseDump_WithoutJobs_ReturnsEmptyList()
    {
        Assert.Empty(JobParserService.ParseDump("{ \"source\": \"Djinni\" }"));
    }

    [Theory]
    [InlineData("{ не json")]
    [InlineData("null")]
    [InlineData("{ \"source\": \"workua\", \"jobs\": [] }")]
    public void ParseDump_RejectsBrokenEmptyOrForeignFiles(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JobParserService.ParseDump(json));
    }

    [Fact]
    public async Task BrokenFile_DoesNotStopOtherFiles()
    {
        // Файли обробляються за алфавітом, тож зламаний іде першим
        File.WriteAllText(Path.Combine(_directory, "a-broken.json"), "{ не json");
        File.WriteAllText(Path.Combine(_directory, "b-ok.json"), ValidDump);
        JobParserService parser = CreateParser(_directory);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        Assert.Equal("1", Assert.Single(_ingestion.Received).Id);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("a-broken.json"));
    }

    [Fact]
    public async Task Parser_RereadsDirectoryOnSchedule()
    {
        File.WriteAllText(Path.Combine(_directory, "dump.json"), ValidDump);
        JobParserService parser = CreateParser(_directory, interval: TimeSpan.FromMilliseconds(50));

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(2);
        await parser.StopAsync(CancellationToken.None);

        // Зупинка штатна: завдання завершилося без винятку
        Assert.True(parser.ExecuteTask?.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisabledParser_NeverReadsFiles()
    {
        File.WriteAllText(Path.Combine(_directory, "dump.json"), ValidDump);
        JobParserService parser = CreateParser(_directory, enabled: false);

        await parser.StartAsync(CancellationToken.None);
        await parser.ExecuteTask!.WaitAsync(WaitLimit);
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Contains(_logger.Entries, e => e.Message.Contains("вимкнено"));
    }

    [Fact]
    public async Task MissingDirectory_IsLoggedAndSkipped()
    {
        JobParserService parser = CreateParser(Path.Combine(_directory, "немає-такої-папки"));

        await parser.StartAsync(CancellationToken.None);
        await _logger.WaitForWarningAsync();
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("не знайдено"));
    }

    private JobParserService CreateParser(string directory, bool enabled = true, TimeSpan? interval = null)
    {
        ServiceProvider services = new ServiceCollection()
            .AddScoped<IVacancyIngestionService>(_ => _ingestion)
            .BuildServiceProvider();

        IngestionOptions options = new() { Enabled = enabled, RawDataDirectory = directory, Interval = interval ?? TimeSpan.FromHours(1) };

        return new JobParserService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            new HostingEnvironment { ContentRootPath = directory },
            _logger);
    }

    /// <summary>Заглушка сервісу нормалізації: запам'ятовує, які вакансії й скільки разів йому передали.</summary>
    private sealed class RecordingIngestionService : IVacancyIngestionService
    {
        private readonly object _lock = new();
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public List<RawVacancy> Received { get; } = [];

        public Task<IngestionResult> ProcessRawVacanciesAsync(IReadOnlyList<RawVacancy> rawItems, CancellationToken ct = default)
        {
            lock (_lock)
            {
                Received.AddRange(rawItems);
                _calls++;
                _changed.TrySetResult();
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return Task.FromResult(new IngestionResult(rawItems.Count, 0, 0));
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (_lock)
                {
                    if (_calls >= count)
                    {
                        return;
                    }

                    changed = _changed.Task;
                }

                await changed.WaitAsync(WaitLimit);
            }
        }
    }

    /// <summary>Логер-шпигун: збирає записи, щоб тест перевірив, що саме парсер повідомив.</summary>
    private sealed class RecordingLogger : ILogger<JobParserService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }

            if (logLevel >= LogLevel.Warning)
            {
                _logged.TrySetResult();
            }
        }

        public Task WaitForWarningAsync() => _logged.Task.WaitAsync(WaitLimit);
    }
}
