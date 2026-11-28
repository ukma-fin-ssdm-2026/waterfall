// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Спільний стан та команди пагінації для списків вакансій («Каталог» і «База вакансій», FR-011 / US-001 AC-2).
/// Керує номером поточної сторінки, розміром сторінки (20 / 50 / 100) та навігацією без мережевих запитів.
/// </summary>
public sealed partial class VacancyPager : ObservableObject
{
    public const int DefaultPageSize = 20;

    public static readonly IReadOnlyList<int> AllowedPageSizes = [20, 50, 100];

    private readonly Action _onPageChanged;

    public VacancyPager(Action onPageChanged)
    {
        ArgumentNullException.ThrowIfNull(onPageChanged);
        _onPageChanged = onPageChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
    public partial int CurrentPage { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible))]
    [NotifyPropertyChangedFor(nameof(IsPageSize20))]
    [NotifyPropertyChangedFor(nameof(IsPageSize50))]
    [NotifyPropertyChangedFor(nameof(IsPageSize100))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
    public partial int PageSize { get; set; } = DefaultPageSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousPage))]
    [NotifyPropertyChangedFor(nameof(HasNextPage))]
    [NotifyPropertyChangedFor(nameof(IsPagerVisible))]
    [NotifyCanExecuteChangedFor(nameof(FirstPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(LastPageCommand))]
    public partial bool IsEnabled { get; set; }

    public int TotalPages => TotalCount <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;

    public bool HasPreviousPage => IsEnabled && CurrentPage > 1;

    public bool HasNextPage => IsEnabled && CurrentPage < TotalPages;

    /// <summary>
    /// Показувати пейджер, коли елементів більше базової сторінки (20): навіть якщо обрано
    /// 50 чи 100 на сторінці, користувач може перемкнутися назад на 20.
    /// </summary>
    public bool IsPagerVisible => IsEnabled && TotalCount > DefaultPageSize;

    public string PageText => $"Сторінка {CurrentPage} з {Math.Max(1, TotalPages)}";

    public bool IsPageSize20 => PageSize == 20;
    public bool IsPageSize50 => PageSize == 50;
    public bool IsPageSize100 => PageSize == 100;

    private bool CanGoPrevPage() => HasPreviousPage;
    private bool CanGoNextPage() => HasNextPage;

    [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
    private void FirstPage()
    {
        if (CurrentPage == 1)
        {
            return;
        }

        CurrentPage = 1;
        _onPageChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevPage))]
    private void PrevPage()
    {
        if (!HasPreviousPage)
        {
            return;
        }

        CurrentPage--;
        _onPageChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private void NextPage()
    {
        if (!HasNextPage)
        {
            return;
        }

        CurrentPage++;
        _onPageChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private void LastPage()
    {
        int total = TotalPages;
        if (total <= 0 || CurrentPage >= total)
        {
            return;
        }

        CurrentPage = total;
        _onPageChanged();
    }

    [RelayCommand]
    private void SetPageSize(int size)
    {
        if (!AllowedPageSizes.Contains(size) || size == PageSize)
        {
            return;
        }

        PageSize = size;
        CurrentPage = 1;
        _onPageChanged();
    }

    public void Reset(int totalCount, bool isEnabled = true)
    {
        IsEnabled = isEnabled;
        TotalCount = Math.Max(0, totalCount);
        CurrentPage = 1;
    }

    public void UpdateTotalCount(int totalCount)
    {
        TotalCount = Math.Max(0, totalCount);
        ClampCurrentPage();
    }

    public (int Skip, int Take) GetSlice(int availableCount)
    {
        int totalPages = TotalPages;
        if (totalPages == 0 || availableCount <= 0)
        {
            CurrentPage = 1;
            return (0, 0);
        }

        ClampCurrentPage();
        int skip = (CurrentPage - 1) * PageSize;
        int take = Math.Clamp(availableCount - skip, 0, PageSize);
        return (skip, take);
    }

    private void ClampCurrentPage()
    {
        int totalPages = TotalPages;
        if (totalPages == 0 || CurrentPage < 1)
        {
            CurrentPage = 1;
        }
        else if (CurrentPage > totalPages)
        {
            CurrentPage = totalPages;
        }
    }
}
