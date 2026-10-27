// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

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
        // Arrange
        SystemSettingsViewModel vm = new();

        // Act
        vm.SaveSettingsCommand.Execute(null);

        // Assert
        Assert.True(vm.HasStatusMessage);
        Assert.True(vm.IsSuccessStatus);
    }
}

