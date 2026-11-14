// AI: варіант фільтра для мультивибору (чіпи Work.ua) (issue #62, на прохання Дениса); перевірено: тести CatalogSiteSearchTests, самоперевірка автора (§8)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Один варіант фільтра, який можна обрати разом з іншими (сфера, мова, вид зайнятості на Work.ua).
/// <see cref="Key"/> і <see cref="Value"/> — параметр адреси сайту: обрані варіанти з однаковим ключем
/// йдуть на сервер одним фільтром через кому (category=1,9), а сервер пише їх так, як чекає сайт.
/// </summary>
public sealed partial class SelectableOption : ObservableObject
{
    public SelectableOption(string label, string key, string value)
    {
        Label = label;
        Key = key;
        Value = value;
    }

    public string Label { get; }

    public string Key { get; }

    public string Value { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}
