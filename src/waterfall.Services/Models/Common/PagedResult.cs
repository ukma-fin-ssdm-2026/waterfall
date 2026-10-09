// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Common;

/// <summary>
/// Стандартизована обгортка для пагінованих відповідей списків (вимога курсу SSDM-2026).
/// Містить сторінку елементів та метадані пагінації.
/// </summary>
/// <typeparam name="T">Тип елементів у списку.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Колекція елементів поточної сторінки.
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// Загальна кількість записів, що відповідають критеріям фільтрації у БД.
    /// </summary>
    /// <example>42</example>
    public int TotalCount { get; init; }

    /// <summary>
    /// Номер поточної сторінки (нумерація з 1).
    /// </summary>
    /// <example>1</example>
    public int Page { get; init; }

    /// <summary>
    /// Розмір сторінки (кількість елементів).
    /// </summary>
    /// <example>10</example>
    public int PageSize { get; init; }

    /// <summary>
    /// Загальна кількість сторінок.
    /// </summary>
    /// <example>5</example>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>
    /// Чи існує попередня сторінка.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// Чи існує наступна сторінка.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;
}
