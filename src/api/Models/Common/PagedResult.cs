// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.Common;
/// <summary>
/// Універсальна обгортка пагінації зі зсувом (offset pagination).
/// </summary>
/// <typeparam name="T">Тип елементів у списку.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Список елементів поточної сторінки.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Загальна кількість елементів, що задовольняють фільтр.
    /// </summary>
    /// <example>42</example>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    /// <summary>
    /// Поточний номер сторінки (починаючи з 1).
    /// </summary>
    /// <example>1</example>
    [JsonPropertyName("page")]
    public int Page { get; init; }

    /// <summary>
    /// Розмір сторінки (кількість елементів на одній сторінці).
    /// </summary>
    /// <example>20</example>
    [JsonPropertyName("page_size")]
    public int PageSize { get; init; }

    /// <summary>
    /// Загальна кількість доступних сторінок.
    /// </summary>
    /// <example>3</example>
    [JsonPropertyName("total_pages")]
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Чи існує наступна сторінка.
    /// </summary>
    /// <example>true</example>
    [JsonPropertyName("has_next_page")]
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Чи існує попередня сторінка.
    /// </summary>
    /// <example>false</example>
    [JsonPropertyName("has_previous_page")]
    public bool HasPreviousPage => Page > 1;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}
