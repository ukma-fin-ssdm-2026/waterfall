// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Interfaces;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Services;

/// <summary>
/// Підключає збереження фільтрів каталогу (REL-002) до Preferences MAUI.
/// Attach викликається з кількох сторінок (CatalogPage на Windows, PhoneSectionHostPage на телефоні),
/// тож повторний виклик для того самого ViewModel нічого не робить.
/// </summary>
internal static class CatalogFilterPreferences
{
    private static CatalogFilterPersistence? _persistence;

    public static void Attach(CatalogViewModel viewModel)
    {
        if (ReferenceEquals(_persistence?.ViewModel, viewModel))
        {
            return;
        }

        _persistence?.Dispose();
        _persistence = CatalogFilterPersistence.Attach(viewModel, new PreferencesFilterStateStorage());
    }

    private sealed class PreferencesFilterStateStorage : IFilterStateStorage
    {
        private const string Key = "waterfall.catalog.filters";

        public string? Load()
        {
            string value = Preferences.Default.Get(Key, string.Empty);
            return value.Length == 0 ? null : value;
        }

        public void Save(string value) => Preferences.Default.Set(Key, value);
    }
}
