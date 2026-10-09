using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace LyricsDisplayer.Views.Main;

public partial class MainLyricsView : UserControl
{
    private readonly MainLyricsAutoFollowPolicy _autoFollowPolicy = new();
    private IMainLyricsViewModel? _viewModel;
    private Window? _ownerWindow;
    private DispatcherTimer? _manualScrollResumeTimer;
    private ScrollViewer? _lyricsScrollViewer;
    private DispatcherOperation? _pendingAutoCenterOperation;
    private bool _isProgrammaticScroll;

    public MainLyricsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public void ResetAutoFollowAndCenterCurrentLine()
    {
        ResetAutoFollowSuspension();
        QueueCurrentLineAutoCenter();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;

        AttachViewModel(e.NewValue as IMainLyricsViewModel);
        ResetAutoFollowAndCenterCurrentLine();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as IMainLyricsViewModel);
        if (_ownerWindow is null)
        {
            _ownerWindow = Window.GetWindow(this);
            if (_ownerWindow is not null) _ownerWindow.SizeChanged += OnOwnerWindowSizeChanged;
        }
        _lyricsScrollViewer ??= FindVisualChild<ScrollViewer>(LyricsListBox);

        if (_manualScrollResumeTimer is null)
        {
            _manualScrollResumeTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = MainLyricsAutoFollowPolicy.ManualScrollResumeDelay
            };
            _manualScrollResumeTimer.Tick += OnManualScrollResumeTimerTick;
        }

        QueueCurrentLineAutoCenter();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_ownerWindow is not null) _ownerWindow.SizeChanged -= OnOwnerWindowSizeChanged;
        _ownerWindow = null;
        DetachViewModel();
        StopManualScrollResumeTimer();
        ResetAutoFollowSuspension();
        _lyricsScrollViewer = null;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (IsLoaded) ResetAutoFollowAndCenterCurrentLine();
            return;
        }

        ResetAutoFollowSuspension();
    }

    private void AttachViewModel(IMainLyricsViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;

        CancelPendingAutoCenter();
        if (_viewModel is not null)
        {
            _viewModel.CurrentLineChanged -= OnCurrentLineChanged;
            _viewModel.PlaybackTrackChanged -= OnPlaybackTrackChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.CurrentLineChanged += OnCurrentLineChanged;
            _viewModel.PlaybackTrackChanged += OnPlaybackTrackChanged;
        }
    }

    private void DetachViewModel() => AttachViewModel(null);

    private void OnCurrentLineChanged(MainLyricsLineViewModel? line)
    {
        if (_autoFollowPolicy.ShouldCenterCurrentLine(line is not null) && line is not null)
            QueueAutoCenter(line);
    }

    private void OnPlaybackTrackChanged() => ResetAutoFollowAndCenterCurrentLine();

    private void OnOwnerWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_autoFollowPolicy.ShouldCenterCurrentLine(_viewModel?.CurrentLine is not null))
            QueueCurrentLineAutoCenter();
    }

    private void OnLyricsPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        SuspendAutoFollowForManualScroll();

    private void OnLyricsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space)
            SuspendAutoFollowForManualScroll();
    }

    private void SuspendAutoFollowForManualScroll()
    {
        if (_manualScrollResumeTimer is null || _viewModel is null || !IsLoaded || !IsVisible ||
            !_autoFollowPolicy.NotifyManualScroll(DateTimeOffset.UtcNow, _isProgrammaticScroll)) return;

        CancelPendingAutoCenter();
        _manualScrollResumeTimer.Stop();
        _manualScrollResumeTimer.Interval = MainLyricsAutoFollowPolicy.ManualScrollResumeDelay;
        _manualScrollResumeTimer.Start();
    }

    private void OnManualScrollResumeTimerTick(object? sender, EventArgs e)
    {
        if (_manualScrollResumeTimer is not { } timer) return;
        timer.Stop();
        var now = DateTimeOffset.UtcNow;
        if (!_autoFollowPolicy.TryResume(now))
        {
            if (_autoFollowPolicy.GetRemainingResumeDelay(now) is { } remaining && remaining > TimeSpan.Zero)
            {
                timer.Interval = remaining;
                timer.Start();
            }
            return;
        }

        // Read CurrentLine now, not the line that happened to be current when scrolling began.
        QueueCurrentLineAutoCenter();
    }

    private void QueueCurrentLineAutoCenter()
    {
        if (_viewModel?.CurrentLine is { } currentLine) QueueAutoCenter(currentLine);
    }

    private void QueueAutoCenter(MainLyricsLineViewModel line)
    {
        if (!IsLoaded || !IsVisible || _viewModel is null ||
            !_autoFollowPolicy.ShouldCenterCurrentLine(hasCurrentLine: true)) return;

        CancelPendingAutoCenter();
        if (LyricsListBox.ItemContainerGenerator.ContainerFromItem(line) is null)
        {
            // ScrollIntoView realizes a virtualized row; the final position is centered after layout.
            _isProgrammaticScroll = true;
            try { LyricsListBox.ScrollIntoView(line); }
            finally { _isProgrammaticScroll = false; }
        }

        _pendingAutoCenterOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _pendingAutoCenterOperation = null;
            CenterCurrentLine(line);
        }));
    }

    private void CenterCurrentLine(MainLyricsLineViewModel line)
    {
        if (!IsLoaded || !IsVisible || _viewModel is null ||
            !_autoFollowPolicy.ShouldCenterCurrentLine(hasCurrentLine: true) ||
            !ReferenceEquals(_viewModel.CurrentLine, line)) return;

        var container = LyricsListBox.ItemContainerGenerator.ContainerFromItem(line) as FrameworkElement;
        var scrollViewer = _lyricsScrollViewer ??= FindVisualChild<ScrollViewer>(LyricsListBox);
        if (container is null || scrollViewer is null || container.ActualHeight <= 0 ||
            scrollViewer.ViewportHeight <= 0) return;

        var centerY = container.TransformToAncestor(scrollViewer)
            .Transform(new Point(0, container.ActualHeight / 2)).Y;
        var desiredOffset = scrollViewer.VerticalOffset + centerY - scrollViewer.ViewportHeight / 2;
        desiredOffset = Math.Clamp(desiredOffset, 0, scrollViewer.ScrollableHeight);

        _isProgrammaticScroll = true;
        try { scrollViewer.ScrollToVerticalOffset(desiredOffset); }
        finally { _isProgrammaticScroll = false; }
    }

    private void ResetAutoFollowSuspension()
    {
        _manualScrollResumeTimer?.Stop();
        _autoFollowPolicy.Reset();
        CancelPendingAutoCenter();
    }

    private void StopManualScrollResumeTimer()
    {
        if (_manualScrollResumeTimer is not { } timer) return;
        timer.Stop();
        timer.Tick -= OnManualScrollResumeTimerTick;
        _manualScrollResumeTimer = null;
    }

    private void CancelPendingAutoCenter()
    {
        _pendingAutoCenterOperation?.Abort();
        _pendingAutoCenterOperation = null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }

        return null;
    }
}
