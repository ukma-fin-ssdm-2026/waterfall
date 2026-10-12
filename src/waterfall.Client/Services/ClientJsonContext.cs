// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.Json.Serialization;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Services;

/// <summary>
/// Згенерований на етапі компіляції JSON-серіалізатор для всіх типів шлюзу.
/// Рефлексійний System.Text.Json вимкнено в trimmed/AOT-збірках (iOS, Android Release),
/// тож кожен новий тип запиту/відповіді треба додати сюди атрибутом.
/// Сервер віддає перелічення назвою ("Djinni", не 3), тож читаємо/пишемо їх рядками.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(JsonStringEnumConverter)])]
[JsonSerializable(typeof(PagedResult<VacancyDto>))]
[JsonSerializable(typeof(VacancyDto))]
[JsonSerializable(typeof(List<SavedVacancyDto>))]
[JsonSerializable(typeof(SavedVacancyDto))]
[JsonSerializable(typeof(SaveVacancyRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ApiErrorResponse))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
