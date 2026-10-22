// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Елемент повідомлення у ШІ-чаті (US-005, FR-002, FR-009).
/// </summary>
public partial class AIChatMessageViewModel : ObservableObject
{
    public string Text { get; init; } = string.Empty;
    public bool IsUser { get; init; }
    public bool IsBot => !IsUser;
    public string SenderName => IsUser ? "Ви" : "ШІ-консультант";
    public DateTime Time { get; init; } = DateTime.Now;
    public string TimeText => Time.ToString("HH:mm");

    /// <summary>
    /// Список знайдених вакансій, вкладених у відповідь ШІ (US-005 AC-1, AC-3).
    /// </summary>
    public ObservableCollection<VacancyItemViewModel> MatchedVacancies { get; } = [];

    public bool HasVacancies => MatchedVacancies.Count > 0;

    /// <summary>
    /// Позначка релевантності (наприклад «Відповідає запиту не на 100%», US-005 AC-3).
    /// </summary>
    public string? MatchDisclaimer { get; init; }
    public bool HasDisclaimer => !string.IsNullOrEmpty(MatchDisclaimer);
}

/// <summary>
/// Вкладка «ШІ-консультант»: текстовий чат, збереження контексту поточної сесії, аналіз критеріїв.
/// </summary>
public partial class AIConsultantViewModel : ObservableObject
{
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;

    public AIConsultantViewModel(IHttpGatewayService gateway, IMessenger messenger)
    {
        _gateway = gateway;
        _messenger = messenger;

        // Початкове привітальне повідомлення
        Messages.Add(new AIChatMessageViewModel
        {
            IsUser = false,
            Text = "Привіт! Я ваш ШІ-консультант із працевлаштування. Напишіть мені звичайною мовою, яку роботу ви шукаєте (наприклад: «Хочу віддалену роботу на Python для джуніора»), і я підберу найкращі вакансії."
        });
    }

    public ObservableCollection<AIChatMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> QuickPrompts { get; } =
    [
        "Віддалена робота на Python для джуніора",
        "Тільки з зарплатою від $1000",
        ".NET Developer з досвідом 1-3 роки",
        "QA Automation remote"
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusHint))]
    public partial bool IsThinking { get; set; }

    public string StatusHint => IsThinking ? "ШІ-консультант аналізує базу вакансій..." : string.Empty;

    // # Mock
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        string prompt = InputText.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        InputText = string.Empty;

        // 1. Додаємо повідомлення користувача
        Messages.Add(new AIChatMessageViewModel
        {
            IsUser = true,
            Text = prompt,
            Time = DateTime.Now
        });

        IsThinking = true;

        try
        {
            // Симуляція запиту до AI Consultant Service (≤ 1 с індикатор очікування за PERF-002)
            await Task.Delay(800);

            // Отримуємо зразкові вакансії для виведення в чаті
            var page = await _gateway.GetVacanciesAsync(prompt.Contains("Python", StringComparison.OrdinalIgnoreCase) ? "Python" : null, 1, 3);

            var botResponse = new AIChatMessageViewModel
            {
                IsUser = false,
                Text = $"Ось що мені вдалося знайти за вашим запитом «{prompt}». Я врахував вимоги щодо стеку та формату.",
                Time = DateTime.Now,
                MatchDisclaimer = page.Items.Count == 0 ? "Точних збігів не знайдено, показую найближчі пропозиції (відповідність ~85%)" : null
            };

            foreach (var item in page.Items)
            {
                botResponse.MatchedVacancies.Add(new VacancyItemViewModel(item, _gateway, _messenger, _ => { }));
            }

            Messages.Add(botResponse);
        }
        catch (Exception ex)
        {
            Messages.Add(new AIChatMessageViewModel
            {
                IsUser = false,
                Text = $"Вибачте, виникла помилка зв'язку з LLM API: {ex.Message}. Ви можете скористатися стандартним пошуком у каталозі (REL-003).",
                Time = DateTime.Now
            });
        }
        finally
        {
            IsThinking = false;
        }
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(InputText) && !IsThinking;

    [RelayCommand]
    private Task SendQuickPromptAsync(string prompt)
    {
        InputText = prompt;
        return SendMessageAsync();
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        Messages.Add(new AIChatMessageViewModel
        {
            IsUser = false,
            Text = "Контекст діалогу очищено. Чим я можу вам допомогти?"
        });
    }
}
