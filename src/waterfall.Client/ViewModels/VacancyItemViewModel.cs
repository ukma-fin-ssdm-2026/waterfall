// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Модель картки вакансії: готові до показу тексти та перемикач «Обране».
/// Спільна для вкладок «Каталог» та «Обране».
/// </summary>
public partial class VacancyItemViewModel : ObservableObject
{
    // Розділювач тисяч задано явно (нерозривний пробіл): дані культури uk-UA різняться між версіями ICU
    // та платформами (Windows / Android), а «4 000» не повинно переноситися на два рядки.
    private static readonly NumberFormatInfo MoneyFormat = new() { NumberGroupSeparator = "\u00A0", NumberGroupSizes = [3] };

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly Action<string> _reportError;

    /// <param name="vacancy">Дані вакансії з API.</param>
    /// <param name="gateway">Шлюз для збереження / видалення з «Обраного».</param>
    /// <param name="messenger">Куди повідомити про зміну стану (<see cref="VacancySaveChangedMessage"/>).</param>
    /// <param name="reportError">Куди показати помилку перемикача (банер сторінки).</param>
    public VacancyItemViewModel(VacancyDto vacancy, IHttpGatewayService gateway, IMessenger messenger, Action<string> reportError)
    {
        Vacancy = vacancy;
        _gateway = gateway;
        _messenger = messenger;
        _reportError = reportError;
        IsSaved = vacancy.IsSaved;
    }

    public VacancyDto Vacancy { get; }

    public Guid Id => Vacancy.Id;
    public string Title => Vacancy.Title;
    public string Description => Vacancy.Description;
    public IReadOnlyList<string> Skills => Vacancy.Skills;
    public bool IsActive => Vacancy.IsActive;

    /// <summary>
    /// «Компанія · Локація» (локація може бути порожньою).
    /// </summary>
    public string CompanyLine => string.IsNullOrWhiteSpace(Vacancy.Location)
        ? Vacancy.Company
        : $"{Vacancy.Company} · {Vacancy.Location}";

    public string StatusText => Vacancy.IsActive ? "Активна" : "Архівна";

    /// <summary>
    /// «4 000–5 500 USD», «від 4 000 USD», «до 5 500 USD»; null — зарплату не вказано.
    /// </summary>
    public string? SalaryText => (Vacancy.SalaryMin, Vacancy.SalaryMax) switch
    {
        ({ } min, { } max) when min == max => $"{Money(min)} {Vacancy.Currency}",
        ({ } min, { } max) => $"{Money(min)}–{Money(max)} {Vacancy.Currency}",
        ({ } min, null) => $"від {Money(min)} {Vacancy.Currency}",
        (null, { } max) => $"до {Money(max)} {Vacancy.Currency}",
        _ => null
    };

    public bool HasSalary => SalaryText is not null;

    public string? SourceText => Vacancy.Source switch
    {
        JobSource.WorkUa => "Work.ua",
        JobSource.RobotaUa => "Robota.ua",
        JobSource.Djinni => "Djinni",
        JobSource.Dou => "DOU",
        _ => null
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    public partial bool IsSaved { get; set; }

    public string SaveButtonText => IsSaved ? "★ Збережено" : "☆ Зберегти";

    /// <summary>
    /// Додає / прибирає вакансію з «Обраного». Поки запит триває, команда недоступна (кнопка вимкнена).
    /// Розбіжність стану з сервером (409 при збереженні, 404 при видаленні) означає,
    /// що бажаний стан уже досягнуто — просто синхронізуємо картку.
    /// Після зміни стану інші вкладки отримують <see cref="VacancySaveChangedMessage"/>.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSaveAsync()
    {
        bool target = !IsSaved;
        try
        {
            if (target)
            {
                await _gateway.SaveVacancyAsync(Id);
            }
            else
            {
                await _gateway.UnsaveVacancyAsync(Id);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == (target ? HttpStatusCode.Conflict : HttpStatusCode.NotFound))
        {
            // Сервер уже в бажаному стані.
        }
        catch (ApiException ex)
        {
            _reportError(ex.Message);
            return;
        }

        IsSaved = target;
        _messenger.Send(new VacancySaveChangedMessage(Id, target));
    }

    private static string Money(decimal value) => value.ToString("#,0", MoneyFormat);
}
