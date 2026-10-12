// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Data;

/// <summary>
/// Ініціалізатор бази даних та сідування мок-даних згідно з вимогами лабораторної роботи.
/// </summary>
public static class WaterfallDbSeeder
{
    public static async Task SeedAsync(WaterfallDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        if (!await context.Vacancies.AnyAsync())
        {
            List<VacancyDB> vacancies = new List<VacancyDB>
            {
                new VacancyDB
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
                    Source = "Djinni",
                    SourceUrl = "https://djinni.co/jobs/example-senior-dotnet/",
                    SkillsJson = JsonSerializer.Serialize(new[] { ".NET 10", "C#", "Kafka", "PostgreSQL", "Docker", "Kubernetes" }),
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-2),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IsActive = true
                },
                new VacancyDB
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
                    Source = "Dou",
                    SourceUrl = "https://jobs.dou.ua/vacancies/example-middle-maui/",
                    SkillsJson = JsonSerializer.Serialize(new[] { ".NET MAUI", "C#", "MVVM", "XAML", "REST API", "SQLite" }),
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IsActive = true
                },
                new VacancyDB
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
                    Source = "WorkUa",
                    SourceUrl = "https://www.work.ua/jobs/example-fullstack-csharp/",
                    SkillsJson = JsonSerializer.Serialize(new[] { "ASP.NET Core", "C#", "Angular", "TypeScript", "EF Core", "Azure" }),
                    PublishedAt = DateTimeOffset.UtcNow.AddHours(-12),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddHours(-12),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IsActive = true
                },
                new VacancyDB
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
                    Source = "RobotaUa",
                    SourceUrl = "https://robota.ua/company123/vacancy-qa-dotnet",
                    SkillsJson = JsonSerializer.Serialize(new[] { "C#", "Playwright", "NUnit", "CI/CD", "REST API" }),
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-3),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-3),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IsActive = true
                },
                new VacancyDB
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
                    Source = "Dou",
                    SourceUrl = "https://jobs.dou.ua/vacancies/example-lead-ai-ml/",
                    SkillsJson = JsonSerializer.Serialize(new[] { "Python", "C#", "PyTorch", "LLM", "Docker", "ONNX" }),
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-4),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-4),
                    LastSeenAt = DateTimeOffset.UtcNow,
                    IsActive = true
                }
            };

            await context.Vacancies.AddRangeAsync(vacancies);
            await context.SaveChangesAsync();
        }

        if (!await context.SavedVacancies.AnyAsync())
        {
            List<SavedVacancyDB> sampleBookmarks = new List<SavedVacancyDB>
            {
                new SavedVacancyDB
                {
                    Id = Guid.Parse("aaaaaaa1-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    VacancyId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    // Закладки належать seeker1@waterfall.com (див. devUsers нижче).
                    UserId = Guid.Parse("66666666-6666-6666-6666-66666666666a"),
                    Notes = "Пріоритет №1: підготувати сертифікати та стек мікросервісів.",
                    SavedAt = DateTimeOffset.UtcNow.AddHours(-5)
                },
                new SavedVacancyDB
                {
                    Id = Guid.Parse("aaaaaaa2-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    VacancyId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    UserId = Guid.Parse("66666666-6666-6666-6666-66666666666a"),
                    Notes = "Цікава вакансія по .NET MAUI: повторити життєвий цикл сторінок та XAML bindings.",
                    SavedAt = DateTimeOffset.UtcNow.AddHours(-2)
                }
            };

            await context.SavedVacancies.AddRangeAsync(sampleBookmarks);
            await context.SaveChangesAsync();
        }

        // Тестові облікові записи лише для локальної розробки.
        // Гілка закрита за умовою провайдер - sqlite, щоб дані не потрапили в продакшн.
        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            && !await context.Users.AnyAsync())
        {
            List<UserDB> devUsers = new List<UserDB>
            {
                new UserDB
                {
                    Id = Guid.Parse("66666666-6666-6666-6666-66666666666a"),
                    Email = "seeker1@waterfall.com",
                    // Пароль: DevSeeker1234
                    PasswordHash = "AQAAAAIAAYagAAAAELS0tART4w8CokOL0gSzcJAqi4a6nUzSZ1EUrjXd9NCvtkgXpXLMayG5mhBYhvqR3w==",
                    Role = "JobSeeker",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-32)
                },
                new UserDB
                {
                    Id = Guid.Parse("66666666-6666-6666-6666-66666666666b"),
                    Email = "seeker2@waterfall.com",
                    // Пароль: DevSeeker1234
                    PasswordHash = "AQAAAAIAAYagAAAAELS0tART4w8CokOL0gSzcJAqi4a6nUzSZ1EUrjXd9NCvtkgXpXLMayG5mhBYhvqR3w==",
                    Role = "JobSeeker",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-31)
                },
                new UserDB
                {
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    Email = "analyst@waterfall.com",
                    // Пароль: DevAnalyst1234
                    PasswordHash = "AQAAAAIAAYagAAAAENr1KdZ+sdpNXwmI+lat+FdNpqQHqwYlOmp2NoEA0Y7SCKmzka7ScgPXoZOWxJJ6bA==",
                    Role = "Analyst",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
                }
            };

            await context.Users.AddRangeAsync(devUsers);
            await context.SaveChangesAsync();
        }
    }
}
