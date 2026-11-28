namespace Waterfall.Client.ViewModels;

/// <summary>
/// Запит до навігаційного хоста відкрити маршрут (наприклад, ШІ-чат після групового аналізу).
/// </summary>
public sealed record NavigateToRouteMessage(string Route);
