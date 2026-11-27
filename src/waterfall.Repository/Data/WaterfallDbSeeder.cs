// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: сід-вакансії отримують числовий досвід, решта похідних полів рахується нормалізатором; перевірено: тести WaterfallDbSeederTests
// AI: демо-дані й тестові акаунти лише за includeDemoData (у Program.cs — лише в Development); перевірено: тести WaterfallDbSeederTests, самоперевірка автора (§8)

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Normalization;

namespace Waterfall.Repository.Data;

/// <summary>
/// Ініціалізатор бази даних та сідування мок-даних згідно з вимогами лабораторної роботи.
/// </summary>
public static class WaterfallDbSeeder
{
    private const string SeniorDotNetSkillsJson = "[\".NET 10\",\"C#\",\"Kafka\",\"PostgreSQL\",\"Docker\",\"Kubernetes\"]";
    private const string MiddleMauiSkillsJson = "[\".NET MAUI\",\"C#\",\"MVVM\",\"XAML\",\"REST API\",\"SQLite\"]";
    private const string FullStackSkillsJson = "[\"ASP.NET Core\",\"C#\",\"Angular\",\"TypeScript\",\"EF Core\",\"Azure\"]";
    private const string QaSkillsJson = "[\"C#\",\"Playwright\",\"NUnit\",\"CI/CD\",\"REST API\"]";
    private const string AiMlSkillsJson = "[\"Python\",\"C#\",\"PyTorch\",\"LLM\",\"Docker\",\"ONNX\"]";

    /// <summary>
    /// Готує базу: створює таблиці й дописує колонки старих схем, дозаповнює похідні поля наявних вакансій.
    /// Демо-дані (5 вакансій, закладки, адреса, геокеш) і тестові акаунти з відомими паролями додаються
    /// лише з <paramref name="includeDemoData"/> = true: API передає сюди <c>app.Environment.IsDevelopment()</c>,
    /// тож на будь-якому іншому сервері вони не з'являться.
    /// </summary>
    public static async Task SeedAsync(WaterfallDbContext context, bool includeDemoData = true)
    {
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

        if (!await context.Vacancies.AnyAsync())
        {
            if (includeDemoData)
            {
                await SeedDemoVacanciesAsync(context);
            }
        }
        else
        {
            await RepairExistingVacanciesAsync(context);
        }

        // Геокеш: прибираємо записи лише з назвою міста (вони перехоплювали конкретні вулиці) — це виправлення даних, не демо
        await RemoveCityOnlyGeoEntriesAsync(context);

        if (includeDemoData)
        {
            await SeedDemoUsersAndRelatedDataAsync(context);
        }
    }

    private static async Task SeedDemoVacanciesAsync(WaterfallDbContext context)
    {
        var vacancies = new List<VacancyDB>
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
                ExperienceMonths = 60,
                SalaryMin = 4000,
                SalaryMax = 5500,
                Currency = "USD",
                Source = "Djinni",
                SourceUrl = "https://djinni.co/jobs/example-senior-dotnet/",
                SkillsJson = SeniorDotNetSkillsJson,
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
                ExperienceMonths = 24,
                SalaryMin = 2200,
                SalaryMax = 3200,
                Currency = "USD",
                Source = "Dou",
                SourceUrl = "https://jobs.dou.ua/vacancies/example-middle-maui/",
                SkillsJson = MiddleMauiSkillsJson,
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
                ExperienceMonths = 36,
                SalaryMin = 3000,
                SalaryMax = 4500,
                Currency = "USD",
                Source = "WorkUa",
                SourceUrl = "https://www.work.ua/jobs/example-fullstack-csharp/",
                SkillsJson = FullStackSkillsJson,
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
                ExperienceMonths = 24,
                SalaryMin = 2800,
                SalaryMax = 3800,
                Currency = "USD",
                Source = "RobotaUa",
                SourceUrl = "https://robota.ua/company123/vacancy-qa-dotnet",
                SkillsJson = QaSkillsJson,
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
                ExperienceMonths = 60,
                SalaryMin = 5000,
                SalaryMax = 7000,
                Currency = "USD",
                Source = "Dou",
                SourceUrl = "https://jobs.dou.ua/vacancies/example-lead-ai-ml/",
                SkillsJson = AiMlSkillsJson,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-4),
                FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-4),
                LastSeenAt = DateTimeOffset.UtcNow,
                IsActive = true
            }
        };

        await context.Vacancies.AddRangeAsync(vacancies);
        await context.SaveChangesAsync();
    }

    private static async Task RepairExistingVacanciesAsync(WaterfallDbContext context)
    {
        // Якщо БД оновлено зі старішої схеми, дозаповнюємо нормалізовані поля (CategoryGroup, WorkFormat, SearchText, ExperienceMonths)
        List<VacancyDB> unnormalized = await context.Vacancies
            .Where(v => v.CategoryGroup == null || v.CategoryGroup == "" || v.SearchText == "")
            .ToListAsync();
        if (unnormalized.Count > 0)
        {
            foreach (VacancyDB v in unnormalized)
            {
                v.ExperienceMonths ??= VacancyExperienceParser.ParseMonths(v.Experience, v.Description);
                VacancyNormalizer.Apply(v);
            }

            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedDemoUsersAndRelatedDataAsync(WaterfallDbContext context)
    {
        // Тестові облікові записи лише для локальної розробки: метод викликається тільки з includeDemoData
        // (API — лише в Development), а не за типом бази, бо SQLite у нас стоїть і за замовчуванням.
        // Сідуємо Users перед SavedVacancies та UserPersonalData, щоб задовольнити зовнішні ключі.
        if (!await context.Users.AnyAsync())
        {
            var devUsers = new List<UserDB>
            {
                new UserDB
                {
                    Id = Guid.Parse("66666666-6666-6666-6666-66666666666a"),
                    Email = "seeker1@waterfall.com",
                    PasswordHash = "AQAAAAIAAYagAAAAELS0tART4w8CokOL0gSzcJAqi4a6nUzSZ1EUrjXd9NCvtkgXpXLMayG5mhBYhvqR3w==",
                    Role = "JobSeeker",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-32)
                },
                new UserDB
                {
                    Id = Guid.Parse("66666666-6666-6666-6666-66666666666b"),
                    Email = "123@123",
                    PasswordHash = "AQAAAAIAAYagAAAAED+/zVB86tdUugHC7i0xTTVLx9I2SbiMKpfOlYVxiv9d5RKskh8jhFCoE6HnwQHjEw==",
                    Role = "JobSeeker",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-31)
                },
                new UserDB
                {
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    Email = "analyst@waterfall.com",
                    PasswordHash = "AQAAAAIAAYagAAAAENr1KdZ+sdpNXwmI+lat+FdNpqQHqwYlOmp2NoEA0Y7SCKmzka7ScgPXoZOWxJJ6bA==",
                    Role = "Analyst",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
                }
            };

            await context.Users.AddRangeAsync(devUsers);
            await context.SaveChangesAsync();
        }

        // Демо-адміністратор (лише системні налаштування сервера). Окремо від блоку вище, щоб з'явився й у вже наповненій
        // базі розробника; пароль — у README, як і в інших тестових акаунтів
        const string DevAdminEmail = "admin@waterfall.com";
        if (!await context.Users.AnyAsync(u => u.Email == DevAdminEmail))
        {
            await context.Users.AddAsync(new UserDB
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                Email = DevAdminEmail,
                PasswordHash = "AQAAAAIAAYagAAAAECXs85vCTbWwJd+DMypA83afHeSItJvUBoBJRJ9SMjgOAFuyl754ta0AygdE41u2Hg==",
                Role = "Admin",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });
            await context.SaveChangesAsync();
        }

        Guid demoUserId = Guid.Parse("66666666-6666-6666-6666-66666666666a");
        if (!await context.SavedVacancies.AnyAsync() && await context.Users.AnyAsync(u => u.Id == demoUserId))
        {
            Guid demoVacancy1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
            Guid demoVacancy2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
            HashSet<Guid> existingDemoVacancies = (await context.Vacancies
                .Where(v => v.Id == demoVacancy1 || v.Id == demoVacancy2)
                .Select(v => v.Id)
                .ToListAsync())
                .ToHashSet();

            var sampleBookmarks = new List<SavedVacancyDB>();
            if (existingDemoVacancies.Contains(demoVacancy1))
            {
                sampleBookmarks.Add(new SavedVacancyDB
                {
                    VacancyId = demoVacancy1,
                    // Закладки належать seeker1@waterfall.com.
                    UserId = demoUserId,
                    Notes = "Пріоритет №1: підготувати сертифікати та стек мікросервісів.",
                    SavedAt = DateTimeOffset.UtcNow.AddHours(-5)
                });
            }

            if (existingDemoVacancies.Contains(demoVacancy2))
            {
                sampleBookmarks.Add(new SavedVacancyDB
                {
                    VacancyId = demoVacancy2,
                    UserId = demoUserId,
                    Notes = "Цікава вакансія по .NET MAUI: повторити життєвий цикл сторінок та XAML bindings.",
                    SavedAt = DateTimeOffset.UtcNow.AddHours(-2)
                });
            }

            if (sampleBookmarks.Count > 0)
            {
                await context.SavedVacancies.AddRangeAsync(sampleBookmarks);
                await context.SaveChangesAsync();
            }
        }

        if (!await context.UserPersonalData.AnyAsync() && await context.Users.AnyAsync(u => u.Id == demoUserId))
        {
            await context.UserPersonalData.AddAsync(new UserPersonalDataDB
            {
                Id = Guid.NewGuid(),
                UserId = Guid.Parse("66666666-6666-6666-6666-66666666666a"),
                Address = "вул. Хрещатик, 22, Київ",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        if (!await context.GeoLocationCache.AnyAsync())
        {
            await context.GeoLocationCache.AddRangeAsync(GetBaseLocations());
            await context.SaveChangesAsync();
        }
    }

    private static async Task RemoveCityOnlyGeoEntriesAsync(WaterfallDbContext context)
    {
        // Видаляємо суто назви міст без вулиць, щоб вони не перехоплювали конкретні вулиці
        List<GeoLocationCacheDB> cityOnlyEntries = await context.GeoLocationCache
            .Where(c => c.NormalizedAddress == "київ" || c.NormalizedAddress == "львів" ||
                        c.NormalizedAddress == "харків" || c.NormalizedAddress == "дніпро" ||
                        c.NormalizedAddress == "одеса")
            .ToListAsync();

        if (cityOnlyEntries.Count > 0)
        {
            context.GeoLocationCache.RemoveRange(cityOnlyEntries);
            await context.SaveChangesAsync();
        }
    }

    private static List<GeoLocationCacheDB> GetBaseLocations() =>
    [
        new() { Id = Guid.NewGuid(), NormalizedAddress = "соборност", Latitude = 50.4421, Longitude = 30.6250 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "спортивна", Latitude = 50.4378, Longitude = 30.5220 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "гулівер", Latitude = 50.4378, Longitude = 30.5220 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "мечников", Latitude = 50.4372, Longitude = 30.5276 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "парус", Latitude = 50.4372, Longitude = 30.5276 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "скляренк", Latitude = 50.4851, Longitude = 30.4721 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "глушков", Latitude = 50.3780, Longitude = 30.4682 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "васильківськ", Latitude = 50.4320, Longitude = 30.5165 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "хрещатик", Latitude = 50.4472, Longitude = 30.5226 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "саксаганськ", Latitude = 50.4360, Longitude = 30.5050 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "володимирськ", Latitude = 50.4460, Longitude = 30.5160 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "польов", Latitude = 50.4430, Longitude = 30.4480 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "позняк", Latitude = 50.4020, Longitude = 30.6350 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "садов", Latitude = 49.8225, Longitude = 23.9855 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "яворницьк", Latitude = 48.4715, Longitude = 35.0310 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "дерибасівськ", Latitude = 46.4850, Longitude = 30.7420 },
        new() { Id = Guid.NewGuid(), NormalizedAddress = "сумськ", Latitude = 50.0010, Longitude = 36.2340 }
    ];
}
