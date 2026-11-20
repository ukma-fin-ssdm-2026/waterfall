// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.Json.Serialization;
using Waterfall.Client.Models.Ai;
using Waterfall.Client.Models.Analytics;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Geo;
using Waterfall.Client.Models.Profile;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.SiteSearch;
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
[JsonSerializable(typeof(RecommendationsResultDto))]
[JsonSerializable(typeof(MarketAnalyticsDto))]
[JsonSerializable(typeof(GradeSalaryDto))]
[JsonSerializable(typeof(TechSalaryDto))]
[JsonSerializable(typeof(CategoryShareDto))]
[JsonSerializable(typeof(VacancyDto))]
[JsonSerializable(typeof(SiteSearchRequest))]
[JsonSerializable(typeof(SiteSearchStatusDto))]
[JsonSerializable(typeof(List<SavedVacancyDto>))]
[JsonSerializable(typeof(SavedVacancyDto))]
[JsonSerializable(typeof(SaveVacancyRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(UserAddressDto))]
[JsonSerializable(typeof(UserStatsDto))]
[JsonSerializable(typeof(RecordSearchRequest))]
[JsonSerializable(typeof(RecordVacancyViewRequest))]
[JsonSerializable(typeof(CoordinatesDto))]
[JsonSerializable(typeof(GeoSyncItemDto))]
[JsonSerializable(typeof(List<GeoSyncItemDto>))]
[JsonSerializable(typeof(GeoSyncRequestDto))]
[JsonSerializable(typeof(GeoSyncResponseDto))]
[JsonSerializable(typeof(AiMessageRequest))]
[JsonSerializable(typeof(AiAnalyzeRequest))]
[JsonSerializable(typeof(AiMetaEvent))]
[JsonSerializable(typeof(AiAppliedFilter))]
[JsonSerializable(typeof(AiVacanciesEvent))]
[JsonSerializable(typeof(AiDeltaEvent))]
[JsonSerializable(typeof(AiDoneEvent))]
[JsonSerializable(typeof(AiErrorEvent))]
[JsonSerializable(typeof(AiRelevanceEvent))]
[JsonSerializable(typeof(AiRelevanceItem))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
