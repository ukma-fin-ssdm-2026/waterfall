// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Text.Json;
using Waterfall.Client.ViewModels;
using Xunit;

namespace Waterfall.Client.Tests;

public class SystemSettingsViewModelTests
{
    [Fact]
    public void DefaultState_HasBackgroundParsingDisabled_AndIntervalFromIngestionSettings()
    {
        // Act
        SystemSettingsViewModel vm = new();

        // Assert
        // Вимога: по дефолту фолз
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("Вимкнено", vm.StatusLabelText);
        Assert.Contains("Вимкнено", vm.ParsingStatusBadgeText);

        // Вимога: інтервал парсингу з готового файлу сеттінг інджестшн сервісу ("00:30:00")
        Assert.Equal("00:30:00", vm.SelectedInterval);
        Assert.Equal("30 хвилин (00:30:00)", vm.SelectedIntervalDisplay);
        Assert.NotEmpty(vm.IntervalOptions);
        Assert.Contains("30 хвилин (00:30:00)", vm.IntervalOptions);

        // Перевірка завантажених параметрів сервісу
        Assert.True(vm.DouPages > 0);
        Assert.True(vm.WorkUaPages > 0);
        Assert.True(vm.RobotaUaPages > 0);
        Assert.True(vm.DjinniPages > 0);
        Assert.NotEmpty(vm.RequestDelay);
    }

    [Fact]
    public void ToggleBackgroundParsingCommand_TogglesFlag()
    {
        // Arrange
        SystemSettingsViewModel vm = new();
        Assert.False(vm.IsBackgroundParsingEnabled);

        // Act
        vm.ToggleBackgroundParsingCommand.Execute(null);

        // Assert
        Assert.True(vm.IsBackgroundParsingEnabled);
        Assert.Equal("Увімкнено", vm.StatusLabelText);
        Assert.Contains("Активно", vm.ParsingStatusBadgeText);

        // Act 2
        vm.ToggleBackgroundParsingCommand.Execute(null);

        // Assert 2
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("Вимкнено", vm.StatusLabelText);
    }

    [Fact]
    public void ChangingIntervalDisplay_UpdatesSelectedInterval()
    {
        // Arrange
        SystemSettingsViewModel vm = new();

        // Act
        vm.SelectedIntervalDisplay = "1 година (01:00:00)";

        // Assert
        Assert.Equal("01:00:00", vm.SelectedInterval);
        Assert.Equal("1 година (01:00:00)", vm.SelectedIntervalDisplay);

        // Act 2
        vm.SelectedIntervalDisplay = "15 хвилин (00:15:00)";

        // Assert 2
        Assert.Equal("00:15:00", vm.SelectedInterval);
    }

    [Fact]
    public void ResetToDefaultsCommand_ResetsBackgroundParsingToFalse()
    {
        // Arrange
        SystemSettingsViewModel vm = new();
        vm.IsBackgroundParsingEnabled = true;
        vm.SelectedIntervalDisplay = "2 години (02:00:00)";

        // Act
        vm.ResetToDefaultsCommand.Execute(null);

        // Assert
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("00:30:00", vm.SelectedInterval);
        Assert.Equal("30 хвилин (00:30:00)", vm.SelectedIntervalDisplay);
        Assert.True(vm.HasStatusMessage);
        Assert.True(vm.IsSuccessStatus);
    }

    [Fact]
    public void SaveSettingsCommand_SetsSuccessStatus()
    {
        // Arrange: тимчасовий файл налаштувань, щоб тест не переписував справжній src/api/appsettings.json
        string path = Path.Combine(Path.GetTempPath(), $"waterfall-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Ingestion": { "Enabled": false, "Interval": "00:30:00", "RobotaUaPages": 1 } }""");
        try
        {
            SystemSettingsViewModel vm = new(path);
            vm.SelectedIntervalDisplay = "1 година (01:00:00)";

            // Act
            vm.SaveSettingsCommand.Execute(null);

            // Assert
            Assert.True(vm.HasStatusMessage);
            Assert.True(vm.IsSuccessStatus);
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement ingestion = saved.RootElement.GetProperty("Ingestion");
            Assert.Equal("01:00:00", ingestion.GetProperty("Interval").GetString());
            Assert.Equal(1, ingestion.GetProperty("RobotaUaPages").GetInt32()); // решта параметрів лишається
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadedSettings_ShowEnabledParsingFromFile_AndSavingKeepsItEnabled()
    {
        // Arrange: збір на сервері ввімкнений
        string path = Path.Combine(Path.GetTempPath(), $"waterfall-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Ingestion": { "Enabled": true, "Interval": "00:30:00" } }""");
        try
        {
            SystemSettingsViewModel vm = new(path);

            // Assert: екран показує справжній стан, а не завжди «Вимкнено»
            Assert.True(vm.IsBackgroundParsingEnabled);
            Assert.Equal("Увімкнено", vm.StatusLabelText);

            // Act: зберегли інший параметр — збір не має вимкнутися
            vm.SelectedIntervalDisplay = "1 година (01:00:00)";
            vm.SaveSettingsCommand.Execute(null);

            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(saved.RootElement.GetProperty("Ingestion").GetProperty("Enabled").GetBoolean());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DayInterval_IsWrittenAsOneDay_NotTwentyFourDays()
    {
        // Arrange: файл, збережений до виправлення, з «24:00:00»
        string path = Path.Combine(Path.GetTempPath(), $"waterfall-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Ingestion": { "Enabled": false, "Interval": "24:00:00" } }""");
        try
        {
            SystemSettingsViewModel vm = new(path);

            // Assert: на екрані — доба, а значення таке, яке сервер прочитає як добу
            Assert.Equal("24 години (1.00:00:00)", vm.SelectedIntervalDisplay);
            Assert.Equal(TimeSpan.FromDays(1), TimeSpan.Parse(vm.SelectedInterval, CultureInfo.InvariantCulture));

            // Act
            vm.SaveSettingsCommand.Execute(null);

            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("1.00:00:00", saved.RootElement.GetProperty("Ingestion").GetProperty("Interval").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingSettingsFile_ShowsParsingDisabled()
    {
        // Arrange: файлу налаштувань немає (застосунок не на тому комп'ютері, що API)
        string path = Path.Combine(Path.GetTempPath(), $"waterfall-settings-{Guid.NewGuid():N}.json");

        // Act
        SystemSettingsViewModel vm = new(path);

        // Assert: як і сервер без налаштувань, збір вимкнений
        Assert.False(vm.IsBackgroundParsingEnabled);
        Assert.Equal("Вимкнено", vm.StatusLabelText);
    }
}

