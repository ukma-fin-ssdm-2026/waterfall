// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;

namespace Waterfall.Client.Entities;

/// <summary>
/// Сутність вакансії у локальній SQLite базі даних клієнта.
/// Успадковує всі поля серверної сутності VacancyDB і додає ознаку синхронізації з сервером.
/// </summary>
[Table("Vacancies")]
[Index(nameof(Category))]
[Index(nameof(CategoryGroup))]
[Index(nameof(PublishedAt))]
[Index(nameof(IsActive))]
[Index(nameof(Source))]
[Index(nameof(IsSynced))]
public class ClientVacancyDB : VacancyDB
{
    /// <summary>
    /// true — запис синхронізовано з сервером; false — зібрано або відредаговано офлайн і чекає відправки на сервер.
    /// </summary>
    public bool IsSynced { get; set; } = true;

    /// <summary>
    /// Час останньої локальної зміни запису (UTC) для вирішення конфліктів при синхронізації.
    /// </summary>
    public DateTimeOffset LocalUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
