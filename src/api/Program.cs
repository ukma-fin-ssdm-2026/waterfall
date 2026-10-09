// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// Посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів через Swagger UI (docs/api/quality-attributes.md, розділ 6)

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text.Json;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// 1. Реєстрація контролерів у DI-контейнері та налаштування стандартизованої обробки помилок валідації
builder.Services.AddControllers(options =>
    {
        // Без snake_case ключ валідації розходився з ключем біндингу, і валідний запит ?page_size=1 отримував 400.
        options.ModelMetadataDetailsProviders.Add(
            new SystemTextJsonValidationMetadataProvider(JsonNamingPolicy.SnakeCaseLower));

        // Обов'язкові поля DTO позначені [Required] явно; без цього рядка до кожної помилки тіла
        // додавався зайвий запис "The request field is required."
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    // Не віддаємо клієнту тексти внутрішніх винятків десеріалізатора (типи .NET, номери рядків)
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false)
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
                            // Порожній ключ або "$" — помилка всього тіла (наприклад, порожнє тіло чи зламаний JSON);
                            // "$.salary_min" — помилка десеріалізації конкретного поля
                            Field = ToFieldName(key),

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

// 2. Реєстрація сервісів сховища
builder.Services.AddSingleton<IVacancyRepository, VacancyRepository>();
builder.Services.AddSingleton<ISavedVacancyRepository, SavedVacancyRepository>();

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

    // Підключення XML-документації для автогенерації описів
    string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

WebApplication app = builder.Build();

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

// Перетворює ключ ModelState на назву поля для відповіді. Без перевірки на порожній ключ
// запит з порожнім тілом падає з IndexOutOfRangeException і повертає 500 замість 400.
static string ToFieldName(string? key)
{
    if (string.IsNullOrEmpty(key) || key == "$")
    {
        return "body";
    }

    if (key.StartsWith("$.", StringComparison.Ordinal))
    {
        return key[2..];
    }

    return char.ToLowerInvariant(key[0]) + key[1..];
}
