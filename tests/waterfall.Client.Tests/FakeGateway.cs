// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: відкладені виклики «Обраного» і входу через TaskCompletionSource (Lab 06); перевірено: тести, що на них спираються, dotnet test.

using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Tests;

/// <summary>
/// Шлюз-заглушка: кожен виклик каталогу записується з власним TaskCompletionSource,
/// тож тест сам вирішує, коли й чим завершити запит (зокрема в «неправильному» порядку).
/// </summary>
internal sealed class FakeGateway : IHttpGatewayService
{
    public sealed record CatalogCall(string? Search, int Page, CancellationToken Ct)
    {
        public TaskCompletionSource<PagedResult<VacancyDto>> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record SavedCall(CancellationToken Ct)
    {
        public TaskCompletionSource<IReadOnlyList<SavedVacancyDto>> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly SemaphoreSlim _callSignal = new(0);

    private readonly SemaphoreSlim _savedSignal = new(0);

    public List<CatalogCall> CatalogCalls { get; } = [];

    /// <summary>
    /// Відповідь на SaveVacancyAsync / UnsaveVacancyAsync; null — успіх.
    /// </summary>
    public Exception? ToggleError { get; set; }

    public List<(string Action, Guid Id)> ToggleCalls { get; } = [];

    public Task<PagedResult<VacancyDto>> GetVacanciesAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        CatalogCall call = new(search, page, ct);
        ct.Register(() => call.Response.TrySetCanceled(ct));
        lock (CatalogCalls)
        {
            CatalogCalls.Add(call);
        }

        _callSignal.Release();
        return call.Response.Task;
    }

    /// <summary>
    /// Чекає, доки кількість викликів каталогу досягне <paramref name="count"/>.
    /// </summary>
    public async Task<CatalogCall> WaitForCatalogCallAsync(int count)
    {
        while (CatalogCalls.Count < count)
        {
            if (!await _callSignal.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException($"Очікувалось {count} викликів каталогу, отримано {CatalogCalls.Count}.");
            }
        }

        return CatalogCalls[count - 1];
    }

    public static PagedResult<VacancyDto> Page(int page, int totalCount, bool hasNext, params string[] titles) => new()
    {
        Items = titles.Select(t => new VacancyDto { Id = Guid.NewGuid(), Title = t }).ToList(),
        Page = page,
        PageSize = 20,
        TotalCount = totalCount,
        HasNextPage = hasNext
    };

    public Task<SavedVacancyDto> SaveVacancyAsync(Guid vacancyId, string? notes = null, CancellationToken ct = default)
    {
        ToggleCalls.Add(("save", vacancyId));
        return ToggleError is null
            ? Task.FromResult(new SavedVacancyDto { VacancyId = vacancyId })
            : Task.FromException<SavedVacancyDto>(ToggleError);
    }

    public Task UnsaveVacancyAsync(Guid vacancyId, CancellationToken ct = default)
    {
        ToggleCalls.Add(("unsave", vacancyId));
        return ToggleError is null ? Task.CompletedTask : Task.FromException(ToggleError);
    }

    public Task<VacancyDto> GetVacancyAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    /// <summary>
    /// Відповідь на GetSavedVacanciesAsync: список або виняток.
    /// </summary>
    public Func<IReadOnlyList<SavedVacancyDto>> Saved { get; set; } = () => [];

    public int SavedCalls { get; private set; }

    /// <summary>
    /// Якщо true, GetSavedVacanciesAsync повертає керований TaskCompletionSource (як у каталогу):
    /// тест сам вирішує, коли завершити запит, а скасування токена скасовує очікування.
    /// </summary>
    public bool DeferSavedCalls { get; set; }

    public List<SavedCall> SavedPendingCalls { get; } = [];

    public Task<IReadOnlyList<SavedVacancyDto>> GetSavedVacanciesAsync(CancellationToken ct = default)
    {
        SavedCalls++;
        if (DeferSavedCalls)
        {
            SavedCall call = new(ct);
            ct.Register(() => call.Response.TrySetCanceled(ct));
            lock (SavedPendingCalls)
            {
                SavedPendingCalls.Add(call);
            }

            _savedSignal.Release();
            return call.Response.Task;
        }

        try
        {
            return Task.FromResult(Saved());
        }
        catch (Exception ex)
        {
            return Task.FromException<IReadOnlyList<SavedVacancyDto>>(ex);
        }
    }

    /// <summary>
    /// Чекає, доки кількість відкладених викликів «Обраного» досягне <paramref name="count"/>.
    /// </summary>
    public async Task<SavedCall> WaitForSavedCallAsync(int count)
    {
        while (true)
        {
            lock (SavedPendingCalls)
            {
                if (SavedPendingCalls.Count >= count)
                {
                    return SavedPendingCalls[count - 1];
                }
            }

            if (!await _savedSignal.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException($"Очікувалось {count} викликів «Обраного», отримано {SavedPendingCalls.Count}.");
            }
        }
    }
    /// <summary>
    /// Відповідь на LoginAsync / RegisterAsync: результат або виняток.
    /// </summary>
    public Func<AuthResponse> Auth { get; set; } = () => throw new NotSupportedException();

    public List<object> AuthRequests { get; } = [];

    /// <summary>
    /// Якщо задано, LoginAsync/RegisterAsync чекають на нього — тест сам завершує вхід вручну,
    /// тож видно проміжний стан (IsBusy, CanExecute) під час очікування відповіді.
    /// </summary>
    public TaskCompletionSource<AuthResponse>? DeferredAuth { get; set; }

    public int LogoutCalls { get; private set; }

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) => Authenticate(request);

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default) => Authenticate(request);

    public Task LogoutAsync(CancellationToken ct = default)
    {
        LogoutCalls++;
        return Task.CompletedTask;
    }

    private Task<AuthResponse> Authenticate(object request)
    {
        AuthRequests.Add(request);
        if (DeferredAuth is not null)
        {
            return DeferredAuth.Task;
        }

        try
        {
            return Task.FromResult(Auth());
        }
        catch (Exception ex)
        {
            return Task.FromException<AuthResponse>(ex);
        }
    }
}
