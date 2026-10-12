// AI: тести життєвого циклу фонового воркера (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Waterfall.Api.Workers;
using Waterfall.Services.Ingestion;

namespace Waterfall.Ingestion.Tests;

public class VacancyIngestionWorkerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task FailedCycle_DoesNotStopWorker()
    {
        // Перший цикл падає, другий має відбутися за розкладом
        ScriptedIngestionService ingestion = new(failingCycles: 1, signalAfterCycles: 2);
        VacancyIngestionWorker worker = CreateWorker(ingestion, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        bool reachedSecondCycle = await ingestion.Signal.Task.WaitAsync(WaitLimit).ContinueWith(t => t.IsCompletedSuccessfully);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(reachedSecondCycle, "Після помилки першого циклу воркер не виконав наступний");
        Assert.True(ingestion.Calls >= 2);
    }

    [Fact]
    public async Task DisabledWorker_NeverRunsIngestion()
    {
        ScriptedIngestionService ingestion = new(failingCycles: 0, signalAfterCycles: 1);
        VacancyIngestionWorker worker = CreateWorker(ingestion, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, ingestion.Calls);
    }

    [Fact]
    public async Task StoppingWorker_EndsGracefully()
    {
        ScriptedIngestionService ingestion = new(failingCycles: 0, signalAfterCycles: 1);
        VacancyIngestionWorker worker = CreateWorker(ingestion, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await ingestion.Signal.Task.WaitAsync(WaitLimit);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask?.IsCompletedSuccessfully);
    }

    private static VacancyIngestionWorker CreateWorker(IVacancyIngestionService ingestion, bool enabled)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => ingestion)
            .BuildServiceProvider();

        IngestionOptions options = new()
        {
            Enabled = enabled,
            Interval = TimeSpan.FromMilliseconds(50),
            InitialDelay = TimeSpan.Zero
        };

        return new VacancyIngestionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<VacancyIngestionWorker>.Instance);
    }

    /// <summary>Сервіс збору, що падає в перших циклах і сигналізує після заданої кількості викликів.</summary>
    private sealed class ScriptedIngestionService : IVacancyIngestionService
    {
        private readonly int _failingCycles;
        private readonly int _signalAfterCycles;
        private int _calls;

        public ScriptedIngestionService(int failingCycles, int signalAfterCycles)
        {
            _failingCycles = failingCycles;
            _signalAfterCycles = signalAfterCycles;
        }

        public TaskCompletionSource Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public Task<IngestionReport> RunOnceAsync(CancellationToken ct = default)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call >= _signalAfterCycles)
            {
                Signal.TrySetResult();
            }

            if (call <= _failingCycles)
            {
                throw new InvalidOperationException("Імітація збою циклу");
            }

            return Task.FromResult(new IngestionReport());
        }
    }
}
