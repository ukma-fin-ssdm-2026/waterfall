// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using Waterfall.Api.Models.SavedVacancies;
using Waterfall.Api.Models.Vacancies;

namespace Waterfall.Api.Services;

public class SavedVacancyRepository : ISavedVacancyRepository
{
    private readonly ConcurrentDictionary<Guid, SavedVacancyDto> _saved = new();
    private readonly IVacancyRepository _vacancyRepository;

    public SavedVacancyRepository(IVacancyRepository vacancyRepository)
    {
        _vacancyRepository = vacancyRepository;
        SeedInitialBookmarks();
    }

    private void SeedInitialBookmarks()
    {
        Guid sampleVacancyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid bookmarkId = Guid.Parse("aaaaaaa1-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        _saved[bookmarkId] = new SavedVacancyDto
        {
            Id = bookmarkId,
            VacancyId = sampleVacancyId,
            Notes = "Пріоритет №1: підготувати сертифікати та стек мікросервісів.",
            SavedAt = DateTimeOffset.UtcNow.AddHours(-5)
        };
    }

    public async Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(CancellationToken ct = default)
    {
        List<SavedVacancyDto> list = new List<SavedVacancyDto>();
        foreach (SavedVacancyDto item in _saved.Values)
        {
            VacancyDto? vacancy = await _vacancyRepository.GetByIdAsync(item.VacancyId, ct);
            list.Add(new SavedVacancyDto
            {
                Id = item.Id,
                VacancyId = item.VacancyId,
                Vacancy = vacancy,
                Notes = item.Notes,
                SavedAt = item.SavedAt
            });
        }
        return list.OrderByDescending(x => x.SavedAt).ToList();
    }

    public async Task<SavedVacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (!_saved.TryGetValue(id, out SavedVacancyDto? item))
        {
            item = _saved.Values.FirstOrDefault(s => s.VacancyId == id);
        }

        if (item is null)
        {
            return null;
        }

        VacancyDto? vacancy = await _vacancyRepository.GetByIdAsync(item.VacancyId, ct);
        return new SavedVacancyDto
        {
            Id = item.Id,
            VacancyId = item.VacancyId,
            Vacancy = vacancy,
            Notes = item.Notes,
            SavedAt = item.SavedAt
        };
    }

    public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, CancellationToken ct = default)
    {
        Guid id = Guid.NewGuid();
        VacancyDto? vacancy = await _vacancyRepository.GetByIdAsync(request.VacancyId, ct);

        SavedVacancyDto dto = new SavedVacancyDto
        {
            Id = id,
            VacancyId = request.VacancyId,
            Vacancy = vacancy,
            Notes = request.Notes?.Trim(),
            SavedAt = DateTimeOffset.UtcNow
        };

        _saved[id] = dto;
        return dto;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (_saved.TryRemove(id, out _))
        {
            return Task.FromResult(true);
        }

      
        SavedVacancyDto? byVacancy = _saved.Values.FirstOrDefault(s => s.VacancyId == id);
        if (byVacancy != null && _saved.TryRemove(byVacancy.Id, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> ExistsForVacancyAsync(Guid vacancyId, CancellationToken ct = default)
    {
        bool exists = _saved.Values.Any(s => s.VacancyId == vacancyId);
        return Task.FromResult(exists);
    }
}
