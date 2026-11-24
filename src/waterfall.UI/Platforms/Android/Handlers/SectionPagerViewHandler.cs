using System.Diagnostics.CodeAnalysis;
using Android.Animation;
using Android.Content;
using Android.Views;
using Android.Views.Animations;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using waterfall.UI.Controls;

namespace waterfall.UI.Platforms.Android.Handlers;

/// <summary>
/// Maps <see cref="SectionPagerView"/> to a <see cref="LayoutHandler"/> whose
/// <see cref="LayoutViewGroup"/> intercepts deliberate horizontal swipes across nested
/// vertical scroll containers while leaving native vertical scrolling and MAUI layout passes untouched.
/// </summary>
public sealed class SectionPagerViewHandler : LayoutHandler
{
    public static readonly IPropertyMapper<SectionPagerView, SectionPagerViewHandler> PagerMapper =
        new PropertyMapper<SectionPagerView, SectionPagerViewHandler>(Mapper)
        {
            [nameof(SectionPagerView.IsSwipeEnabled)] = MapIsSwipeEnabled
        };

    public static readonly CommandMapper<SectionPagerView, SectionPagerViewHandler> PagerCommandMapper =
        new(CommandMapper)
        {
            [nameof(SectionPagerView.SetCurrentIndex)] = MapSetCurrentIndex
        };

    public SectionPagerViewHandler() : base(PagerMapper, PagerCommandMapper)
    {
    }

    protected override LayoutViewGroup CreatePlatformView() => new SwipePagerViewGroup(Context);

    protected override void ConnectHandler(LayoutViewGroup platformView)
    {
        base.ConnectHandler(platformView);

        if (platformView is SwipePagerViewGroup pagerGroup && VirtualView is SectionPagerView pager)
        {
            pagerGroup.Attach(pager);
        }
    }

    protected override void DisconnectHandler(LayoutViewGroup platformView)
    {
        if (platformView is SwipePagerViewGroup pagerGroup)
        {
            pagerGroup.Detach();
        }

        base.DisconnectHandler(platformView);
    }

    private static void MapIsSwipeEnabled(SectionPagerViewHandler handler, SectionPagerView view)
    {
        if (handler.PlatformView is SwipePagerViewGroup pagerGroup)
        {
            pagerGroup.Attach(view);
            pagerGroup.SetSwipeEnabled(view.IsSwipeEnabled);
        }
    }

    private static void MapSetCurrentIndex(SectionPagerViewHandler handler, SectionPagerView view, object? args)
    {
        if (handler.PlatformView is not SwipePagerViewGroup pagerGroup || args is not SectionPagerMoveRequest request)
        {
            return;
        }

        pagerGroup.Attach(view);

        if (request.Animate && request.FromIndex != request.ToIndex)
        {
            pagerGroup.AnimateToIndex(request.FromIndex, request.ToIndex);
        }
        else
        {
            pagerGroup.CancelAnimationsAndResetDrag();
        }
    }

    private sealed class SwipePagerViewGroup : LayoutViewGroup
    {
        private const float HorizontalDominanceRatio = 1.5f;
        private const float EdgeRubberBandFactor = 0.22f;
        private const float CommitDistanceFraction = 0.30f;

        private readonly int _touchSlop;
        private readonly int _minFlingVelocityPx;
        private readonly int _maxFlingVelocityPx;

        private WeakReference<SectionPagerView>? _pagerRef;
        private bool _isSwipeEnabled = true;

        private int _activePointerId = MotionEvent.InvalidPointerId;
        private float _downX;
        private float _downY;
        private float _lastX;
        private float _dragOffsetPx;
        private bool _isDragging;
        private bool _gestureLockedVertical;
        private int _baseIndex;
        private int _approachedNeighbor = -1;

        private VelocityTracker? _velocityTracker;
        private ValueAnimator? _animator;
        private EventHandler<ValueAnimator.AnimatorUpdateEventArgs>? _updateHandler;
        private EventHandler? _endHandler;
        private int _animGeneration;

        public SwipePagerViewGroup(Context context) : base(context)
        {
            ViewConfiguration config = ViewConfiguration.Get(context)!;
            _touchSlop = config.ScaledPagingTouchSlop > 0 ? config.ScaledPagingTouchSlop : config.ScaledTouchSlop;
            _minFlingVelocityPx = config.ScaledMinimumFlingVelocity * 2;
            _maxFlingVelocityPx = config.ScaledMaximumFlingVelocity;
        }

        public void Attach(SectionPagerView pager)
        {
            _pagerRef = new WeakReference<SectionPagerView>(pager);
            _isSwipeEnabled = pager.IsSwipeEnabled;
        }

        public void Detach()
        {
            CancelAnimator();
            RecycleVelocityTracker();
            ResetTouchState();
            _pagerRef = null;
        }

        public void SetSwipeEnabled(bool enabled)
        {
            _isSwipeEnabled = enabled;
            if (!enabled && _isDragging && TryGetPager(out SectionPagerView? pager))
            {
                FinishDrag(pager, cancelled: true);
            }
        }

        public void CancelAnimationsAndResetDrag()
        {
            CancelAnimator();
            RecycleVelocityTracker();
            ResetTouchState();
        }

        public void AnimateToIndex(int fromIndex, int toIndex)
        {
            if (!TryGetPager(out SectionPagerView? pager))
            {
                return;
            }

            int count = pager.Pages.Count;
            if (fromIndex < 0 || fromIndex >= count || toIndex < 0 || toIndex >= count || fromIndex == toIndex)
            {
                CancelAnimationsAndResetDrag();
                pager.FinalizeVisualState(toIndex);
                return;
            }

            CancelAnimator();
            RecycleVelocityTracker();
            ResetTouchState();

            int generation = ++_animGeneration;
            pager.ApplyTransitionProgress(fromIndex, toIndex, 0.0);

            ValueAnimator animator = ValueAnimator.OfFloat(0f, 1f)!;
            animator.SetDuration(220);
            animator.SetInterpolator(new DecelerateInterpolator(1.6f));

            _updateHandler = (_, e) =>
            {
                if (generation != _animGeneration || !TryGetPager(out SectionPagerView? activePager))
                {
                    return;
                }

                float progress = (float)e.Animation!.AnimatedValue!;
                activePager.ApplyTransitionProgress(fromIndex, toIndex, progress);
            };

            _endHandler = (_, _) =>
            {
                if (generation != _animGeneration)
                {
                    return;
                }

                DisposeAnimator();
                if (TryGetPager(out SectionPagerView? activePager))
                {
                    activePager.FinalizeVisualState(toIndex);
                }
            };

            animator.Update += _updateHandler;
            animator.AnimationEnd += _endHandler;
            _animator = animator;
            animator.Start();
        }

        public override bool OnInterceptTouchEvent(MotionEvent? ev)
        {
            if (ev is null || !_isSwipeEnabled || !TryGetPager(out SectionPagerView? pager) || pager.Pages.Count <= 1)
            {
                if (ev?.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
                {
                    ResetTouchState();
                }

                return false;
            }

            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    _activePointerId = ev.GetPointerId(0);
                    _downX = ev.GetX();
                    _downY = ev.GetY();
                    _lastX = _downX;
                    _isDragging = false;
                    _gestureLockedVertical = false;
                    _approachedNeighbor = -1;
                    return false;

                case MotionEventActions.Move:
                    if (_isDragging)
                    {
                        return true;
                    }

                    if (_gestureLockedVertical || _activePointerId == MotionEvent.InvalidPointerId)
                    {
                        return false;
                    }

                    int pointerIndex = ev.FindPointerIndex(_activePointerId);
                    if (pointerIndex < 0)
                    {
                        return false;
                    }

                    float x = ev.GetX(pointerIndex);
                    float y = ev.GetY(pointerIndex);
                    float absDx = Math.Abs(x - _downX);
                    float absDy = Math.Abs(y - _downY);

                    if (absDy > _touchSlop && absDy >= absDx)
                    {
                        _gestureLockedVertical = true;
                        return false;
                    }

                    if (absDx > _touchSlop && absDx > absDy * HorizontalDominanceRatio)
                    {
                        BeginDrag(pager, ev, x);
                        return true;
                    }

                    return false;

                case MotionEventActions.PointerUp:
                    OnSecondaryPointerUp(ev);
                    return _isDragging;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    ResetTouchState();
                    return false;

                default:
                    return _isDragging;
            }
        }

        public override bool OnTouchEvent(MotionEvent? ev)
        {
            if (ev is null)
            {
                return false;
            }

            if (!TryGetPager(out SectionPagerView? pager) || pager.Pages.Count <= 1)
            {
                if (_isDragging)
                {
                    ResetTouchState();
                }

                return base.OnTouchEvent(ev);
            }

            if (!_isSwipeEnabled)
            {
                if (_isDragging)
                {
                    FinishDrag(pager, cancelled: true);
                }

                return base.OnTouchEvent(ev);
            }

            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    _activePointerId = ev.GetPointerId(0);
                    _downX = ev.GetX();
                    _downY = ev.GetY();
                    _lastX = _downX;
                    _gestureLockedVertical = false;
                    _approachedNeighbor = -1;
                    return true;

                case MotionEventActions.Move:
                    if (_activePointerId == MotionEvent.InvalidPointerId)
                    {
                        return false;
                    }

                    int pointerIndex = ev.FindPointerIndex(_activePointerId);
                    if (pointerIndex < 0)
                    {
                        return false;
                    }

                    float x = ev.GetX(pointerIndex);
                    float y = ev.GetY(pointerIndex);

                    if (!_isDragging)
                    {
                        if (_gestureLockedVertical)
                        {
                            return false;
                        }

                        float absDx = Math.Abs(x - _downX);
                        float absDy = Math.Abs(y - _downY);
                        if (absDy > _touchSlop && absDy >= absDx)
                        {
                            _gestureLockedVertical = true;
                            return false;
                        }

                        if (absDx > _touchSlop && absDx > absDy * HorizontalDominanceRatio)
                        {
                            BeginDrag(pager, ev, x);
                        }
                        else
                        {
                            return true;
                        }
                    }

                    _velocityTracker?.AddMovement(ev);
                    float deltaX = x - _lastX;
                    _lastX = x;
                    _dragOffsetPx += deltaX;

                    (float appliedFraction, int neighbor) = ComputeAppliedState(_dragOffsetPx, _baseIndex, pager.Pages.Count);
                    if (neighbor >= 0 && neighbor != _approachedNeighbor)
                    {
                        _approachedNeighbor = neighbor;
                        pager.NotifyPageApproaching(neighbor);
                    }

                    pager.ApplyDragOffsets(_baseIndex, neighbor, appliedFraction);
                    return true;

                case MotionEventActions.PointerDown:
                    int downIndex = ev.ActionIndex;
                    _activePointerId = ev.GetPointerId(downIndex);
                    _lastX = ev.GetX(downIndex);
                    return true;

                case MotionEventActions.PointerUp:
                    OnSecondaryPointerUp(ev);
                    return true;

                case MotionEventActions.Up:
                    if (_isDragging)
                    {
                        if (_activePointerId != MotionEvent.InvalidPointerId)
                        {
                            int upIndex = ev.FindPointerIndex(_activePointerId);
                            if (upIndex >= 0)
                            {
                                float upX = ev.GetX(upIndex);
                                _dragOffsetPx += upX - _lastX;
                                _lastX = upX;
                                _velocityTracker?.AddMovement(ev);
                            }
                        }

                        FinishDrag(pager, cancelled: false);
                    }
                    else
                    {
                        ResetTouchState();
                    }

                    return true;

                case MotionEventActions.Cancel:
                    if (_isDragging)
                    {
                        FinishDrag(pager, cancelled: true);
                    }
                    else
                    {
                        ResetTouchState();
                    }

                    return true;

                default:
                    return base.OnTouchEvent(ev);
            }
        }

        private void BeginDrag(SectionPagerView pager, MotionEvent ev, float currentX)
        {
            if (_animator is not null)
            {
                CancelAnimator();
                pager.FinalizeVisualState(pager.CurrentIndex);
            }

            _isDragging = true;
            _baseIndex = pager.CurrentIndex;
            _lastX = currentX;
            _dragOffsetPx = 0f;
            _approachedNeighbor = -1;

            RecycleVelocityTracker();
            _velocityTracker = VelocityTracker.Obtain();
            _velocityTracker?.AddMovement(ev);

            Parent?.RequestDisallowInterceptTouchEvent(true);
        }

        private void FinishDrag(SectionPagerView pager, bool cancelled)
        {
            _isDragging = false;
            Parent?.RequestDisallowInterceptTouchEvent(false);

            float velocityX = 0f;
            if (!cancelled && _velocityTracker is not null && _activePointerId != MotionEvent.InvalidPointerId)
            {
                _velocityTracker.ComputeCurrentVelocity(1000, _maxFlingVelocityPx);
                velocityX = _velocityTracker.GetXVelocity(_activePointerId);
            }

            RecycleVelocityTracker();

            int count = pager.Pages.Count;
            (float startFraction, int dragNeighbor) = ComputeAppliedState(_dragOffsetPx, _baseIndex, count);

            int targetIndex = _baseIndex;
            float targetFraction = 0f;

            bool isAtBlockedEdge = (_baseIndex == 0 && _dragOffsetPx > 0f) ||
                                   (_baseIndex == count - 1 && _dragOffsetPx < 0f);

            if (!cancelled && !isAtBlockedEdge)
            {
                bool shouldAdvanceRight = _baseIndex < count - 1 &&
                    (startFraction <= -CommitDistanceFraction ||
                     (velocityX < -_minFlingVelocityPx && _dragOffsetPx < -_touchSlop));

                bool shouldAdvanceLeft = _baseIndex > 0 &&
                    (startFraction >= CommitDistanceFraction ||
                     (velocityX > _minFlingVelocityPx && _dragOffsetPx > _touchSlop));

                if (shouldAdvanceRight)
                {
                    targetIndex = _baseIndex + 1;
                    targetFraction = -1f;
                }
                else if (shouldAdvanceLeft)
                {
                    targetIndex = _baseIndex - 1;
                    targetFraction = 1f;
                }
            }

            int settleNeighbor = targetIndex != _baseIndex ? targetIndex : dragNeighbor;
            int baseIndex = _baseIndex;
            if (targetIndex != _baseIndex && targetIndex != _approachedNeighbor)
            {
                pager.NotifyPageApproaching(targetIndex);
            }

            ResetTouchState();

            AnimateDragSettle(pager, baseIndex, settleNeighbor, targetIndex, startFraction, targetFraction);
        }

        private void AnimateDragSettle(
            SectionPagerView pager,
            int baseIndex,
            int settleNeighbor,
            int targetIndex,
            float startFraction,
            float targetFraction)
        {
            if (Math.Abs(startFraction - targetFraction) < 0.005f)
            {
                pager.NotifyPageSelected(targetIndex);
                return;
            }

            CancelAnimator();
            int generation = ++_animGeneration;

            long durationMs = (long)Math.Clamp(140f + Math.Abs(targetFraction - startFraction) * 110f, 140f, 240f);
            ValueAnimator animator = ValueAnimator.OfFloat(startFraction, targetFraction)!;
            animator.SetDuration(durationMs);
            animator.SetInterpolator(new DecelerateInterpolator(1.6f));

            _updateHandler = (_, e) =>
            {
                if (generation != _animGeneration || !TryGetPager(out SectionPagerView? activePager))
                {
                    return;
                }

                float fraction = (float)e.Animation!.AnimatedValue!;
                activePager.ApplyDragOffsets(baseIndex, settleNeighbor, fraction);
            };

            _endHandler = (_, _) =>
            {
                if (generation != _animGeneration)
                {
                    return;
                }

                DisposeAnimator();
                if (TryGetPager(out SectionPagerView? activePager))
                {
                    activePager.NotifyPageSelected(targetIndex);
                }
            };

            animator.Update += _updateHandler;
            animator.AnimationEnd += _endHandler;
            _animator = animator;
            animator.Start();
        }

        private (float AppliedFraction, int NeighborIndex) ComputeAppliedState(float offsetPx, int baseIndex, int pageCount)
        {
            float width = Width > 0 ? Width : 1f;
            float rawFraction = Math.Clamp(offsetPx / width, -1f, 1f);

            if ((baseIndex == 0 && offsetPx > 0f) || (baseIndex == pageCount - 1 && offsetPx < 0f))
            {
                return (rawFraction * EdgeRubberBandFactor, -1);
            }

            int neighbor = rawFraction < 0f && baseIndex + 1 < pageCount
                ? baseIndex + 1
                : rawFraction > 0f && baseIndex - 1 >= 0
                    ? baseIndex - 1
                    : -1;

            return (rawFraction, neighbor);
        }

        private void OnSecondaryPointerUp(MotionEvent ev)
        {
            int pointerIndex = ev.ActionIndex;
            int pointerId = ev.GetPointerId(pointerIndex);
            if (pointerId != _activePointerId)
            {
                return;
            }

            int newPointerIndex = pointerIndex == 0 ? 1 : 0;
            if (newPointerIndex < ev.PointerCount)
            {
                _activePointerId = ev.GetPointerId(newPointerIndex);
                _lastX = ev.GetX(newPointerIndex);
                _velocityTracker?.Clear();
            }
            else
            {
                _activePointerId = MotionEvent.InvalidPointerId;
            }
        }

        private void CancelAnimator()
        {
            _animGeneration++;
            if (_animator is not null)
            {
                _animator.Cancel();
                DisposeAnimator();
            }
        }

        private void DisposeAnimator()
        {
            if (_animator is null)
            {
                return;
            }

            if (_updateHandler is not null)
            {
                _animator.Update -= _updateHandler;
                _updateHandler = null;
            }

            if (_endHandler is not null)
            {
                _animator.AnimationEnd -= _endHandler;
                _endHandler = null;
            }

            _animator.Dispose();
            _animator = null;
        }

        private void RecycleVelocityTracker()
        {
            if (_velocityTracker is not null)
            {
                _velocityTracker.Recycle();
                _velocityTracker.Dispose();
                _velocityTracker = null;
            }
        }

        private void ResetTouchState()
        {
            _activePointerId = MotionEvent.InvalidPointerId;
            _isDragging = false;
            _gestureLockedVertical = false;
            _dragOffsetPx = 0f;
            _approachedNeighbor = -1;
        }

        private bool TryGetPager([NotNullWhen(true)] out SectionPagerView? pager)
        {
            pager = null;
            return _pagerRef is not null && _pagerRef.TryGetTarget(out pager);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelAnimator();
                RecycleVelocityTracker();
            }

            base.Dispose(disposing);
        }
    }
}
