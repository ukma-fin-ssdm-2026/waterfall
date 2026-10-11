using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;
using Waterfall.Repository.Data;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// 1. Реєстрація контролерів у DI-контейнері та налаштування стандартизованої обробки помилок валідації
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Перевизначаємо стандартну фабрику відповідей на помилки валідації від Microsoft
        // Цей код викликається автоматично ТІЛЬКИ тоді, коли провалюються атрибути [Required], [StringLength] тощо
        options.InvalidModelStateResponseFactory = context =>
        {
            // Список, куди ми зберемо деталі по кожному невалідному полю
            List<ValidationErrorDetail> details = new List<ValidationErrorDetail>();

            // Проходимося по словнику ModelState, де збережено всі перевірені поля:
            // key - назва властивості C# (наприклад, "Title"), value - інформація про стан перевірки
            foreach ((string? key, ModelStateEntry? value) in context.ModelState)
            {
                // Перевіряємо, чи є помилки саме для цього поля
                if (value.Errors.Count > 0)
                {
                    // Одне поле може порушити кілька правил одночасно (наприклад, і [Required], і [MinLength])
                    foreach (ModelError error in value.Errors)
                    {
                        // З'ясовуємо, що саме ввів користувач:
                        // Якщо запит був формою - шукаємо в Request.Form, якщо через URL/Query - у Request.Query
                        string providedValue = context.HttpContext.Request.HasFormContentType
                            ? context.HttpContext.Request.Form[key].ToString()
                            : context.HttpContext.Request.Query[key].ToString();

                        // Додаємо інформацію про помилку у наш список деталей:
                        details.Add(new ValidationErrorDetail
                        {
                            // char.ToLowerInvariant(key[0]) + key[1..] перетворює першу літеру на малу: "Title" -> "title" (вимога camelCase для JSON)
                            Field = char.ToLowerInvariant(key[0]) + key[1..],

                            // Текст помилки, який ми вказали в анотації моделі (ErrorMessage)
                            Message = error.ErrorMessage,

                            // Записуємо введене значення користувача (якщо порожньо - null)
                            ProvidedValue = string.IsNullOrEmpty(providedValue) ? null : providedValue
                        });
                    }
                }
            }

            // Отримуємо унікальний ідентифікатор запиту (Correlation ID):
            // 1. Спочатку пробуємо дістати заголовок X-Correlation-Id від клієнта (.NET MAUI)
            // 2. Якщо клієнт його не надіслав - беремо TraceIdentifier, який автоматично згенерував .NET
            string requestId = context.HttpContext.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues corrId)
                ? corrId.ToString()
                : context.HttpContext.TraceIdentifier;

            // Збираємо фінальний об'єкт помилки строго за каркасом стандарту курсу SSDM-2026:
            ApiErrorResponse errorResponse = ApiErrorResponse.Create(
                code: "VALIDATION_ERROR", // Код помилки для програмної обробки клієнтом
                message: "Запит містить невалідні поля", // Опис для користувача
                requestId: requestId, // ID запиту для швидкого пошуку в логах
                documentation: "http://localhost:3000/api-docs/index.html", // Посилання на стандарт помилок
                details: details); // Масив з детальним описом кожного некоректного поля

            // Повертаємо клієнту статус HTTP 400 Bad Request разом із нашим стандартизованим JSON
            return new BadRequestObjectResult(errorResponse);
        };
    });

// 2. Реєстрація інфраструктури (EF Core SQLite/MSSQL) та сервісів кешування (In-Memory Cache-Aside)
builder.Services.AddWaterfallServices(builder.Configuration);

// 3. Налаштування CORS для браузерних клієнтів та Swagger UI
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});


// 4. Налаштування Swagger / OpenAPI документації
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Waterfall API",
        Version = "v1",
        Description = "RESTful API для платформи агрегації та підбору вакансій Waterfall "
    });

    c.EnableAnnotations();

    // Підключення XML-документації для автогенерації описів (Waterfall.Api, Waterfall.Repository, Waterfall.Services)
    string apiXmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(apiXmlPath))
    {
        c.IncludeXmlComments(apiXmlPath);
    }

    string repoXmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(WaterfallDbContext).Assembly.GetName().Name}.xml");
    if (File.Exists(repoXmlPath))
    {
        c.IncludeXmlComments(repoXmlPath);
    }

    string servicesXmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(VacancyService).Assembly.GetName().Name}.xml");
    if (File.Exists(servicesXmlPath))
    {
        c.IncludeXmlComments(servicesXmlPath);
    }
});

WebApplication app = builder.Build();

// Ініціалізація структури бази даних (SQLite / MSSQL) та сідування початкових мок-даних
using (IServiceScope scope = app.Services.CreateScope())
{
    WaterfallDbContext db = scope.ServiceProvider.GetRequiredService<WaterfallDbContext>();
    await WaterfallDbSeeder.SeedAsync(db);
}

// 5. Middleware для підтримки кореляційного Request ID (Structured Logging & Error Contract)
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues correlationId) || string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        context.Request.Headers["X-Correlation-Id"] = correlationId;
    }

    context.Response.Headers["X-Correlation-Id"] = correlationId;
    context.Response.Headers["X-Request-Id"] = correlationId;

    await next();
});


// 6. Налаштування Swagger UI на /api-docs
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Waterfall API v1");
    c.RoutePrefix = "api-docs";
    c.DocumentTitle = "Waterfall API Documentation";
});

app.MapGet("/", () => Results.Redirect("/api-docs"));

app.UseCors("AllowAll");


app.UseAuthorization();
app.MapControllers();

// 7. Автоматична генерація OpenAPI YAML специфікації у docs/api/openapi-generated.yaml
OpenApiGenerator.ExportOpenApiYaml(app.Services, app.Environment, app.Logger);

if (args.Contains("--generate-openapi-and-exit")) return;

app.Run();
