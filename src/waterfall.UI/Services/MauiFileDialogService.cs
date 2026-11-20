// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Interfaces;

namespace waterfall.UI.Services;

/// <summary>
/// Реалізація діалогів вибору файлу та шляху збереження для .NET MAUI (нативний FileSavePicker на Windows + FilePicker на всіх платформах).
/// </summary>
public sealed class MauiFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType JsonFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".json"],
        [DevicePlatform.Android] = ["application/json", "text/plain"],
        [DevicePlatform.iOS] = ["public.json"],
        [DevicePlatform.MacCatalyst] = ["public.json"]
    });

    public async Task<string?> PickJsonFileToOpenAsync(CancellationToken ct = default)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Оберіть JSON-файл вакансій для імпорту",
                FileTypes = JsonFileTypes
            });

            return result?.FullPath;
        });
    }

    public async Task<string?> PickJsonFileToSaveAsync(string suggestedFileName, CancellationToken ct = default)
    {
        string safeName = string.IsNullOrWhiteSpace(suggestedFileName) ? "vacancies_export.json" : suggestedFileName.Trim();

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
#if WINDOWS
            Windows.Storage.Pickers.FileSavePicker picker = new()
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(safeName)
            };
            picker.FileTypeChoices.Add("JSON файл", [".json"]);

            Window? mauiWindow = Application.Current?.Windows.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
            return file?.Path;
#else
            string defaultPath = Path.Combine(FileSystem.AppDataDirectory, safeName);
            Page? page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
            {
                return defaultPath;
            }

            string? chosen = await page.DisplayPromptAsync(
                "Експорт у JSON",
                "Вкажіть повний шлях для збереження файлу:",
                accept: "Зберегти",
                cancel: "Скасувати",
                initialValue: defaultPath);

            return string.IsNullOrWhiteSpace(chosen) ? null : chosen.Trim();
#endif
        });
    }
}
