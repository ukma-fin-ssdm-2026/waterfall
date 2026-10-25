namespace Waterfall.Client.ViewModels;

/// <summary>
/// Повідомлення про зміну або очищення домашньої/поточної адреси користувача.
/// </summary>
/// <param name="Address">Нова збережена адреса користувача (або null при виході з акаунту).</param>
public sealed record UserAddressChangedMessage(string? Address);
