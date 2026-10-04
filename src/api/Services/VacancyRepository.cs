// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Models.Vacancies;

namespace Waterfall.Api.Services;

public class VacancyRepository : IVacancyRepository
{
    private readonly ConcurrentDictionary<Guid, VacancyDto> _vacancies = new();

    public VacancyRepository()
    {
        SeedInitialData();
    }

    private void SeedInitialData()
    {
        VacancyDto sample1 = new VacancyDto
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Senior C# .NET Backend Developer",
            Company = "Genesis Tech",
            Description = "Розробка high-load мікросервісів на .NET 10, Kafka, Redis та PostgreSQL. Підтримка мільйонів активних користувачів.",
            Category = ".NET",
            Location = "Київ (Віддалено)",
            FullAddress = "Київ, вул. Мечникова, 2, БЦ Парус",
            Schedule = "Повна зайнятість · Гнучкий графік",
            Experience = "5+ років",
            SalaryMin = 4000,
            SalaryMax = 5500,
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = "https://djinni.co/jobs/example-senior-dotnet/",
            Skills = [".NET 10", "C#", "Kafka", "PostgreSQL", "Docker", "Kubernetes"],
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
            IsActive = true
        };

        VacancyDto sample2 = new VacancyDto
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title = "Middle .NET MAUI / Mobile Developer",
            Company = "SoftServe",
            Description = "Створення кросплатформного клієнтського застосунку з архітектурою MVVM на базі .NET MAUI для iOS та Android.",
            Category = "Mobile",
            Location = "Львів / Віддалено",
            FullAddress = "Львів, вул. Садова, 2а",
            Schedule = "Повна зайнятість · Стандартний графік: 5/2",
            Experience = "2+ роки",
            SalaryMin = 2200,
            SalaryMax = 3200,
            Currency = "USD",
            Source = JobSource.Dou,
            SourceUrl = "https://jobs.dou.ua/vacancies/example-middle-maui/",
            Skills = [".NET MAUI", "C#", "MVVM", "XAML", "REST API", "SQLite"],
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsActive = true
        };

        VacancyDto sample3 = new VacancyDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Title = "Full Stack C# / Angular Developer",
            Company = "EPAM Systems",
            Description = "Розробка корпоративної платформи управління даними. Стек: ASP.NET Core, EF Core, Angular 17, Azure Cloud.",
            Category = "Full Stack",
            Location = "Київ (Гібрид)",
            FullAddress = "Київ, проспект Академіка Глушкова, 1",
            Schedule = "Повна зайнятість · Гібридний формат",
            Experience = "3+ роки",
            SalaryMin = 3000,
            SalaryMax = 4500,
            Currency = "USD",
            Source = JobSource.WorkUa,
            SourceUrl = "https://www.work.ua/jobs/example-fullstack-csharp/",
            Skills = ["ASP.NET Core", "C#", "Angular", "TypeScript", "EF Core", "Azure"],
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-12),
            IsActive = true
        };

        VacancyDto sample4 = new VacancyDto
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Title = "QA Automation (.NET / Playwright)",
            Company = "Grammarly",
            Description = "Автоматизація тестування Web та API сервісів. Створення тест-сьютів на C# з використанням Playwright та Allure.",
            Category = "QA",
            Location = "Віддалено",
            FullAddress = "Київ, Спортивна площа, 1а, БЦ Гулівер",
            Schedule = "Повна зайнятість · Віддалена робота",
            Experience = "2+ роки",
            SalaryMin = 2800,
            SalaryMax = 3800,
            Currency = "USD",
            Source = JobSource.RobotaUa,
            SourceUrl = "https://robota.ua/company123/vacancy-qa-dotnet",
            Skills = ["C#", "Playwright", "NUnit", "CI/CD", "REST API"],
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-3),
            IsActive = true
        };

        VacancyDto sample5 = new VacancyDto
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Title = "Lead AI / ML Solutions Engineer",
            Company = "Ajax Systems",
            Description = "Інтеграція сучасних мовних моделей (LLM) та комп'ютерного зору в системи безпеки розумного будинку.",
            Category = "AI / ML",
            Location = "Київ (Офіс)",
            FullAddress = "Київ, вул. Семена Скляренка, 5",
            Schedule = "Повна зайнятість · Офіс: 5/2",
            Experience = "5+ років",
            SalaryMin = 5000,
            SalaryMax = 7000,
            Currency = "USD",
            Source = JobSource.Dou,
            SourceUrl = "https://jobs.dou.ua/vacancies/example-lead-ai-ml/",
            Skills = ["Python", "C#", "PyTorch", "LLM", "Docker", "ONNX"],
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-4),
            IsActive = true
        };

        _vacancies[sample1.Id] = sample1;
        _vacancies[sample2.Id] = sample2;
        _vacancies[sample3.Id] = sample3;
        _vacancies[sample4.Id] = sample4;
        _vacancies[sample5.Id] = sample5;
    }

    public Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default)
    {
        IQueryable<VacancyDto> query = _vacancies.Values.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.Title.ToLowerInvariant().Contains(term) ||
                v.Company.ToLowerInvariant().Contains(term) ||
                v.Description.ToLowerInvariant().Contains(term) ||
                v.Skills.Any(s => s.ToLowerInvariant().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            string cat = filter.Category.Trim().ToLowerInvariant();
            query = query.Where(v => v.Category.ToLowerInvariant().Contains(cat));
        }

        if (!string.IsNullOrWhiteSpace(filter.Experience))
        {
            string exp = filter.Experience.Trim().ToLowerInvariant();
            query = query.Where(v => v.Experience != null && v.Experience.ToLowerInvariant().Contains(exp));
        }

        if (filter.MinSalary.HasValue)
        {
            query = query.Where(v => v.SalaryMin >= filter.MinSalary.Value || v.SalaryMax >= filter.MinSalary.Value);
        }

        if (filter.MaxSalary.HasValue)
        {
            query = query.Where(v => v.SalaryMin <= filter.MaxSalary.Value || (v.SalaryMax.HasValue && v.SalaryMax <= filter.MaxSalary.Value));
        }

        if (filter.Source.HasValue)
        {
            query = query.Where(v => v.Source == filter.Source.Value);
        }

        int totalCount = query.Count();
        List<VacancyDto> items = query
            .OrderByDescending(v => v.PublishedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<VacancyDto>(items, totalCount, filter.Page, filter.PageSize));
    }

    public Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _vacancies.TryGetValue(id, out VacancyDto? vacancy);
        return Task.FromResult(vacancy);
    }

    public Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
    {
        Guid id = Guid.NewGuid();
        VacancyDto dto = new VacancyDto
        {
            Id = id,
            Title = request.Title.Trim(),
            Company = request.Company.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            Location = request.Location.Trim(),
            FullAddress = request.FullAddress?.Trim(),
            Schedule = request.Schedule?.Trim(),
            Experience = request.Experience?.Trim(),
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Source = request.Source,
            SourceUrl = request.SourceUrl.Trim(),
            Skills = request.Skills ?? [],
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _vacancies[id] = dto;
        return Task.FromResult(dto);
    }

    public Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default)
    {
        if (!_vacancies.TryGetValue(id, out VacancyDto? existing))
        {
            return Task.FromResult<VacancyDto?>(null);
        }

        VacancyDto updated = new VacancyDto
        {
            Id = id,
            Title = request.Title.Trim(),
            Company = request.Company.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            Location = request.Location.Trim(),
            FullAddress = request.FullAddress?.Trim() ?? existing.FullAddress,
            Schedule = request.Schedule?.Trim() ?? existing.Schedule,
            Experience = request.Experience?.Trim() ?? existing.Experience,
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Source = existing.Source,
            SourceUrl = existing.SourceUrl,
            Skills = request.Skills ?? [],
            PublishedAt = existing.PublishedAt,
            IsActive = request.IsActive
        };

        _vacancies[id] = updated;
        return Task.FromResult<VacancyDto?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        bool removed = _vacancies.TryRemove(id, out _);
        return Task.FromResult(removed);
    }
}
