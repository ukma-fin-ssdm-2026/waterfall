# Summary
<details open><summary>Summary</summary>

|||
|:---|:---|
| Generated on: | 26.10.2026 - 0:28:01 |
| Coverage date: | 26.10.2026 - 0:10:40 - 26.10.2026 - 0:27:58 |
| Parser: | MultiReport (16x Cobertura) |
| Assemblies: | 4 |
| Classes: | 99 |
| Files: | 114 |
| **Line coverage:** | 75.8% (4407 of 5807) |
| Covered lines: | 4407 |
| Uncovered lines: | 1400 |
| Coverable lines: | 5807 |
| Total lines: | 12502 |
| **Branch coverage:** | 70.3% (1320 of 1876) |
| Covered branches: | 1320 |
| Total branches: | 1876 |
| **Method coverage:** | [Feature is only available for sponsors](https://reportgenerator.io/pro) |

</details>

## Coverage
<details><summary>Waterfall.Api - 83.9%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**Waterfall.Api**|**83.9%**|**73%**|
|System.Runtime.CompilerServices|0%||
|Waterfall.Api.Controllers.ApiControllerBase|100%|100%|
|Waterfall.Api.Controllers.AuthController|100%|100%|
|Waterfall.Api.Controllers.GeoController|100%|100%|
|Waterfall.Api.Controllers.ProfileController|88.2%|62.5%|
|Waterfall.Api.Controllers.SavedVacanciesController|91.4%|70%|
|Waterfall.Api.Controllers.VacanciesController|91.1%|70%|
|Waterfall.Api.Extensions.ServiceCollectionExtensions|0%|0%|
|Waterfall.Api.Models.Common.ApiErrorResponse|100%|100%|
|Waterfall.Api.Models.Common.ErrorPayload|100%||
|Waterfall.Api.Models.Common.ValidationErrorDetail|66.6%||
|Waterfall.Api.Options.JwtOptions|100%||
|Waterfall.Api.Services.AspNetPasswordHasher|91.6%|87.5%|
|Waterfall.Api.Services.AuthenticationService|96.6%|92.8%|
|Waterfall.Api.Services.JwtTokenService|100%||
|Waterfall.Api.Services.OpenApiGenerator|0%|0%|
|Waterfall.Api.Workers.IngestionOptions|100%||
|Waterfall.Api.Workers.JobParserService|97.6%|92.8%|

</details>
<details><summary>waterfall.Client - 71%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**waterfall.Client**|**71%**|**68%**|
|Waterfall.Client.ApiException|100%||
|Waterfall.Client.Data.ClientWaterfallDbContext|71.4%|100%|
|Waterfall.Client.Entities.ClientGeoLocationCacheDB|100%||
|Waterfall.Client.Extensions.ServiceCollectionExtensions|100%|100%|
|Waterfall.Client.Models.Auth.AuthResponse|100%||
|Waterfall.Client.Models.Auth.AuthSession|100%|100%|
|Waterfall.Client.Models.Auth.LoginRequest|100%||
|Waterfall.Client.Models.Auth.RegisterRequest|100%||
|Waterfall.Client.Models.Common.ApiError|100%||
|Waterfall.Client.Models.Common.ApiErrorResponse|100%||
|Waterfall.Client.Models.Common.ApiValidationError|100%||
|Waterfall.Client.Models.Common.PagedResult`1|100%||
|Waterfall.Client.Models.Geo.CoordinatesDto|100%||
|Waterfall.Client.Models.Geo.GeoSyncItemDto|100%||
|Waterfall.Client.Models.Geo.GeoSyncRequestDto|100%||
|Waterfall.Client.Models.Geo.GeoSyncResponseDto|100%||
|Waterfall.Client.Models.Geo.LocalGeoCacheItem|0%||
|Waterfall.Client.Models.Profile.UserAddressDto|0%||
|Waterfall.Client.Models.SavedVacancies.SavedVacancyDto|80%||
|Waterfall.Client.Models.SavedVacancies.SaveVacancyRequest|100%||
|Waterfall.Client.Models.Vacancies.VacancyDto|100%||
|Waterfall.Client.Services.AuthHeaderHandler|100%|100%|
|Waterfall.Client.Services.ClientJsonContext|68.7%|52.3%|
|Waterfall.Client.Services.DefaultUrlLauncher|0%||
|Waterfall.Client.Services.EfLocalGeoCacheService|85.6%|80.3%|
|Waterfall.Client.Services.GeoDistanceService|75.8%|62.9%|
|Waterfall.Client.Services.HttpGatewayService|72.9%|89.4%|
|Waterfall.Client.Services.PhotonAddressAutocompleteService|95.1%|90.9%|
|Waterfall.Client.ViewModels.AIChatMessageViewModel|0%|0%|
|Waterfall.Client.ViewModels.AIConsultantViewModel|0%|0%|
|Waterfall.Client.ViewModels.AnalyticsViewModel|0%|0%|
|Waterfall.Client.ViewModels.CatalogViewModel|85.8%|96%|
|Waterfall.Client.ViewModels.FavoritesViewModel|100%|85%|
|Waterfall.Client.ViewModels.LoginViewModel|80.6%|86.9%|
|Waterfall.Client.ViewModels.RecommendationsViewModel|100%|70%|
|Waterfall.Client.ViewModels.RoleOption|100%||
|Waterfall.Client.ViewModels.TechDemandItem|0%||
|Waterfall.Client.ViewModels.TimelinePoint|0%||
|Waterfall.Client.ViewModels.UserAddressChangedMessage|100%||
|Waterfall.Client.ViewModels.VacancyItemViewModel|84.3%|79.7%|
|Waterfall.Client.ViewModels.VacancySaveChangedMessage|100%||

</details>
<details><summary>waterfall.Repository - 83.4%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**waterfall.Repository**|**83.4%**|**64.1%**|
|Waterfall.Repository.Data.WaterfallDbContext|100%|100%|
|Waterfall.Repository.Data.WaterfallDbSeeder|83.4%|34.6%|
|Waterfall.Repository.Entities.GeoLocationCacheDB|100%||
|Waterfall.Repository.Entities.SavedVacancyDB|100%||
|Waterfall.Repository.Entities.UserDB|100%||
|Waterfall.Repository.Entities.UserPersonalDataDB|100%||
|Waterfall.Repository.Entities.VacancyDB|100%||
|Waterfall.Repository.Extensions.ServiceCollectionExtensions|0%|0%|
|Waterfall.Repository.Repositories.EfGeoLocationCacheRepository|85.4%|65.3%|
|Waterfall.Repository.Repositories.EfSavedVacancyRepository|98.3%|92.8%|
|Waterfall.Repository.Repositories.EfUserRepository|100%||
|Waterfall.Repository.Repositories.EfVacancyRepository|77.3%|89.5%|

</details>
<details><summary>waterfall.Services - 90.8%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**waterfall.Services**|**90.8%**|**78.7%**|
|waterfall.Services.Common.SalaryRange|100%||
|waterfall.Services.Common.SalaryRangeParser|100%|100%|
|Waterfall.Services.Exceptions.DuplicateEmailException|100%||
|Waterfall.Services.Exceptions.InvalidCredentialsException|100%||
|Waterfall.Services.Extensions.ServiceCollectionExtensions|0%||
|Waterfall.Services.GeoLocationService|93.4%|86.1%|
|Waterfall.Services.Interfaces.IngestionResult|100%||
|Waterfall.Services.Interfaces.RawVacancy|100%||
|Waterfall.Services.Models.Auth.AuthResponse|100%||
|Waterfall.Services.Models.Auth.LoginRequest|100%||
|Waterfall.Services.Models.Auth.RegisterRequest|100%||
|Waterfall.Services.Models.Common.PagedResult`1|57.1%|0%|
|Waterfall.Services.Models.Geo.CoordinatesDto|100%||
|Waterfall.Services.Models.Geo.GeoSyncItemDto|100%||
|Waterfall.Services.Models.Geo.GeoSyncRequestDto|100%||
|Waterfall.Services.Models.Geo.GeoSyncResponseDto|100%||
|Waterfall.Services.Models.Profile.UserAddressDto|100%||
|Waterfall.Services.Models.SavedVacancies.SavedVacancyDto|100%||
|Waterfall.Services.Models.SavedVacancies.SaveVacancyRequest|28.5%|0%|
|Waterfall.Services.Models.Vacancies.CreateVacancyRequest|51.8%|0%|
|Waterfall.Services.Models.Vacancies.UpdateVacancyRequest|53.5%|0%|
|Waterfall.Services.Models.Vacancies.VacancyDto|100%||
|Waterfall.Services.Models.Vacancies.VacancyFilterParameters|72%|25%|
|Waterfall.Services.SavedVacancyService|100%|80%|
|Waterfall.Services.UserProfileService|100%|100%|
|Waterfall.Services.VacancyCacheService|100%|87.5%|
|Waterfall.Services.VacancyIngestionService|93.9%|87.7%|
|Waterfall.Services.VacancyService|98.7%|75%|

</details>
