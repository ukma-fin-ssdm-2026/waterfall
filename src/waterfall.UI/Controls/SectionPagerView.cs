namespace waterfall.UI.Controls;

public sealed record SectionPagerMoveRequest(int FromIndex, int ToIndex, bool Animate);

/// <summary>
/// Cross-platform paged section container backed on Android by <c>LayoutHandler</c>
/// with a touch-intercepting <c>SwipePagerViewGroup</c> for 1:1 finger-tracking sub-tab swipes
/// without throttling vertical scrolling or interfering with dynamic MAUI layout passes.
/// </summary>
public sealed class SectionPagerView : Grid
{
    public static readonly BindableProperty IsSwipeEnabledProperty =
        BindableProperty.Create(
            nameof(IsSwipeEnabled),
            typeof(bool),
            typeof(SectionPagerView),
            true);

    private readonly List<View> _pages = [];

    public SectionPagerView()
    {
        IsClippedToBounds = true;
    }

    public bool IsSwipeEnabled
    {
        get => (bool)GetValue(IsSwipeEnabledProperty);
        set => SetValue(IsSwipeEnabledProperty, value);
    }

    public IReadOnlyList<View> Pages => _pages;

    public int CurrentIndex { get; private set; }

    public event EventHandler<int>? PageSelected;

    public event EventHandler<int>? PageApproaching;

    public void SetPages(IReadOnlyList<View> pages)
    {
        Children.Clear();
        _pages.Clear();
        _pages.AddRange(pages);

        if (_pages.Count == 0)
        {
            CurrentIndex = 0;
            return;
        }

        if (CurrentIndex >= _pages.Count)
        {
            CurrentIndex = _pages.Count - 1;
        }

        for (int i = 0; i < _pages.Count; i++)
        {
            View page = _pages[i];
            page.TranslationX = 0;
            page.IsVisible = i == CurrentIndex;
            Children.Add(page);
        }
    }

    public void SetCurrentIndex(int index, bool animate)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        int previous = CurrentIndex;
        CurrentIndex = index;

        if (Handler is not null && animate && previous != index)
        {
            Handler.Invoke(nameof(SetCurrentIndex), new SectionPagerMoveRequest(previous, index, true));
            return;
        }

        Handler?.Invoke(nameof(SetCurrentIndex), new SectionPagerMoveRequest(previous, index, false));
        FinalizeVisualState(index);
    }

    internal void PrepareDragVisibility(int primaryIndex, int neighborIndex)
    {
        if (_pages.Count == 0)
        {
            return;
        }

        double width = Width > 0 ? Width : 1.0;

        for (int i = 0; i < _pages.Count; i++)
        {
            View page = _pages[i];
            if (i == primaryIndex)
            {
                if (!page.IsVisible)
                {
                    page.IsVisible = true;
                }
            }
            else if (i == neighborIndex)
            {
                if (!page.IsVisible)
                {
                    page.TranslationX = i > primaryIndex ? width : -width;
                    page.IsVisible = true;
                }
            }
            else if (page.IsVisible)
            {
                page.IsVisible = false;
                page.TranslationX = 0;
            }
        }
    }

    internal void ApplyDragOffsets(int baseIndex, int neighborIndex, double offsetFraction)
    {
        if (baseIndex < 0 || baseIndex >= _pages.Count)
        {
            return;
        }

        PrepareDragVisibility(baseIndex, neighborIndex);

        double width = Width > 0 ? Width : 1.0;
        _pages[baseIndex].TranslationX = offsetFraction * width;

        if (neighborIndex >= 0 && neighborIndex < _pages.Count && neighborIndex != baseIndex)
        {
            _pages[neighborIndex].TranslationX = neighborIndex > baseIndex
                ? (1.0 + offsetFraction) * width
                : (offsetFraction - 1.0) * width;
        }
    }

    internal void ApplyTransitionProgress(int fromIndex, int toIndex, double progress)
    {
        if (fromIndex < 0 || fromIndex >= _pages.Count ||
            toIndex < 0 || toIndex >= _pages.Count ||
            fromIndex == toIndex)
        {
            return;
        }

        PrepareDragVisibility(fromIndex, toIndex);

        double width = Width > 0 ? Width : 1.0;
        double dir = toIndex > fromIndex ? 1.0 : -1.0;
        _pages[fromIndex].TranslationX = -dir * progress * width;
        _pages[toIndex].TranslationX = dir * (1.0 - progress) * width;
    }

    internal void FinalizeVisualState(int activeIndex)
    {
        if (_pages.Count == 0 || activeIndex < 0 || activeIndex >= _pages.Count)
        {
            return;
        }

        CurrentIndex = activeIndex;

        for (int i = 0; i < _pages.Count; i++)
        {
            View page = _pages[i];
            if (i == activeIndex)
            {
                page.TranslationX = 0;
                if (!page.IsVisible)
                {
                    page.IsVisible = true;
                }
            }
            else
            {
                page.TranslationX = 0;
                if (page.IsVisible)
                {
                    page.IsVisible = false;
                }
            }
        }
    }

    internal void NotifyPageApproaching(int index)
    {
        if (index >= 0 && index < _pages.Count)
        {
            PageApproaching?.Invoke(this, index);
        }
    }

    internal void NotifyPageSelected(int index)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        FinalizeVisualState(index);
        PageSelected?.Invoke(this, index);
    }
}
