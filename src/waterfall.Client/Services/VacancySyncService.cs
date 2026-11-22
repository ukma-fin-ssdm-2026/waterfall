// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Entities;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Services;

/// <summary>
/// Реалізація двосторонньої синхронізації бази вакансій між локальною SQLite БД клієнта та сервером.
/// Підтримує пакетне вивантаження великої кількості офлайн-вакансій, дельта-синхронізацію за LastSyncedAt
/// та повну синхронізацію.
/// </summary>
public sealed class VacancySyncService : IVacancySyncService, IDisposable
{
    public const int UploadBatchSize = 500;

    private readonly IHttpGatewayService _gateway;
    private readonly ILocalVacancyStore _localStore;
    private readonly IMessenger? _messenger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private volatile bool _isSyncing;

    public VacancySyncService(
        IHttpGatewayService gateway,
        ILocalVacancyStore localStore,
        IMessenger? messenger = null)
    {
        _gateway = gateway;
        _localStore = localStore;
        _messenger = messenger;
    }

    public bool IsSyncing => _isSyncing;

    public async Task<VacancySyncResult> SyncWithServerAsync(bool fullSync = false, CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        _isSyncing = true;
        try
        {
            IReadOnlyList<ClientVacancyDB> unsynced = await _localStore.GetUnsyncedAsync(ct);
            DateTimeOffset? lastSyncedAt = fullSync ? null : await _localStore.GetLastSyncedAtAsync(ct);

            int totalUploaded = 0;
            int totalCreatedOnServer = 0;
            int totalUpdatedOnServer = 0;
            int totalDownloaded = 0;
            int totalAddedLocally = 0;
            int totalUpdatedLocally = 0;
            int totalServerVacancies = 0;
            DateTimeOffset serverTime = DateTimeOffset.UtcNow;

            List<VacancySyncItemDto> uploadItems = unsynced.Select(EfLocalVacancyStore.MapToSyncItem).ToList();

            // Навіть якщо локальних змін немає, виконуємо один запит синхронізації, щоб забрати нові серверні вакансії.
            int batchCount = Math.Max(1, (int)Math.Ceiling(uploadItems.Count / (double)UploadBatchSize));
            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                bool isLastBatch = batchIndex == batchCount - 1;
                List<VacancySyncItemDto> chunk = uploadItems
                    .Skip(batchIndex * UploadBatchSize)
                    .Take(UploadBatchSize)
                    .ToList();

                VacancySyncRequestDto request = new()
                {
                    ClientVacancies = chunk,
                    // Повну/дельта вибірку з сервера забираємо на фінальному батчі, щоб не дублювати трафік
                    LastSyncedAt = isLastBatch ? lastSyncedAt : DateTimeOffset.UtcNow.AddMinutes(1),
                    FullSync = isLastBatch && (fullSync || lastSyncedAt is null)
                };

                VacancySyncResponseDto response = await _gateway.SyncVacanciesAsync(request, ct);
                totalUploaded += response.ReceivedFromClient;
                totalCreatedOnServer += response.CreatedOnServer;
                totalUpdatedOnServer += response.UpdatedOnServer;
                totalServerVacancies = response.TotalServerVacancies;
                serverTime = response.ServerTime;

                if (chunk.Count > 0)
                {
                    await _localStore.MarkAsSyncedAsync(chunk.Select(c => c.Id), ct);
                }

                if (response.ServerVacancies is { Count: > 0 })
                {
                    totalDownloaded += response.ServerVacancies.Count;
                    (int added, int updated) = await _localStore.UpsertFromServerAsync(response.ServerVacancies, ct);
                    totalAddedLocally += added;
                    totalUpdatedLocally += updated;
                }
            }

            LocalVacancyStats stats = await _localStore.GetStatsAsync(ct);
            string summary = $"Надіслано: {totalUploaded}, отримано: {totalDownloaded} (нових локально: {totalAddedLocally})";
            await _localStore.SetLastSyncedAtAsync(serverTime, summary, ct);

            VacancySyncResult result = new()
            {
                Succeeded = true,
                UploadedToServer = totalUploaded,
                CreatedOnServer = totalCreatedOnServer,
                UpdatedOnServer = totalUpdatedOnServer,
                DownloadedFromServer = totalDownloaded,
                AddedLocally = totalAddedLocally,
                UpdatedLocally = totalUpdatedLocally,
                TotalLocalVacancies = stats.TotalCount,
                TotalServerVacancies = totalServerVacancies,
                SyncedAt = serverTime
            };

            _messenger?.Send(new VacanciesSyncedMessage(result));
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LocalVacancyStats stats = await SafeGetStatsAsync();
            return new VacancySyncResult
            {
                Succeeded = false,
                TotalLocalVacancies = stats.TotalCount,
                SyncedAt = stats.LastSyncedAt,
                ErrorMessage = ex is ApiException apiEx ? apiEx.Message : $"Не вдалося синхронізувати базу: {ex.Message}"
            };
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    private async Task<LocalVacancyStats> SafeGetStatsAsync()
    {
        try
        {
            return await _localStore.GetStatsAsync();
        }
        catch
        {
            return new LocalVacancyStats(0, 0, null);
        }
    }

    public void Dispose() => _syncLock.Dispose();
}
