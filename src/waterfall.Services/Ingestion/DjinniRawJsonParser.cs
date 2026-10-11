// AI: розбір JSON-дампу вакансій Djinni (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Text.Json;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Розбирає сирий JSON-дамп вакансій Djinni (формат описано в data/raw/README.md) у список <see cref="RawVacancy"/>.
/// Перевіряє лише структуру документа; зміст полів перевіряє <see cref="VacancyNormalizer"/>.
/// </summary>
public sealed class DjinniRawJsonParser : IRawVacancyParser
{
    public const string Source = "djinni";

    /// <summary>Більший документ — ознака помилки експорту або спроби перевантажити збір.</summary>
    public const int MaxItemsPerDocument = 5_000;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16
    };

    public string SourceName => Source;

    public RawParseResult Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return RawParseResult.Failure("документ порожній");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return RawParseResult.Failure($"некоректний JSON (рядок {(ex.LineNumber ?? 0) + 1})");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RawParseResult.Failure("очікувався JSON-об'єкт з полями source і jobs");
            }

            string? source = ReadString(root, "source");
            if (!string.Equals(source, Source, StringComparison.OrdinalIgnoreCase))
            {
                return RawParseResult.Failure($"очікувалось джерело «{Source}», а в документі «{source ?? "не вказано"}»");
            }

            if (!TryGetProperty(root, "jobs", out JsonElement jobs) || jobs.ValueKind != JsonValueKind.Array)
            {
                return RawParseResult.Failure("у документі немає масиву jobs");
            }

            int count = jobs.GetArrayLength();
            if (count > MaxItemsPerDocument)
            {
                return RawParseResult.Failure($"документ містить {count} записів, дозволено не більше {MaxItemsPerDocument}");
            }

            List<RawVacancy> items = new(count);
            List<string> itemErrors = [];
            int index = 0;

            foreach (JsonElement job in jobs.EnumerateArray())
            {
                index++;
                if (job.ValueKind != JsonValueKind.Object)
                {
                    itemErrors.Add($"запис №{index} не є JSON-об'єктом");
                    continue;
                }

                items.Add(new RawVacancy
                {
                    Source = Source,
                    ExternalId = ReadString(job, "id"),
                    Url = ReadString(job, "url"),
                    Title = ReadString(job, "title"),
                    Company = ReadString(job, "company"),
                    Salary = ReadString(job, "salary"),
                    Location = ReadString(job, "location"),
                    Posted = ReadString(job, "posted"),
                    Experience = ReadString(job, "experience"),
                    Employment = ReadString(job, "employment"),
                    Category = ReadString(job, "category"),
                    DescriptionHtml = ReadString(job, "descriptionHtml"),
                    Tags = ReadString(job, "tags")
                });
            }

            return RawParseResult.Success(items, itemErrors);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // Регістр назв полів у дампах різних експортерів відрізняється, тож порівнюємо без його врахування
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            // id може прийти числом: 720001
            JsonValueKind.Number => value.GetRawText(),
            // Навички можуть прийти масивом рядків
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())),
            _ => null
        };
    }
}
