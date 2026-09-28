using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LyricsDisplayer.Core.Settings;
using WpfPoint = System.Windows.Point;
using MenuItem = System.Windows.Controls.MenuItem;
using ListBox = System.Windows.Controls.ListBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LyricsDisplayer;

public enum OverlayHitTestResult
{
    Client,
    Transparent
}

public static class OverlayHitTestPolicy
{
    public static OverlayHitTestResult Decide(bool clickThrough, bool overLyricsOrResizeGrip) =>
        clickThrough && !overLyricsOrResizeGrip
            ? OverlayHitTestResult.Transparent
            : OverlayHitTestResult.Client;
}

public partial class LyricsOverlayWindow : Window, ILyricsOverlayView
{
    private const int MaNoActivate = 3;
    private bool _allowClose;
    private OverlayInteractionState _interaction =
        OverlayInteractionState.FromPreferences(OverlayPreferences.Default);
    private LyricsOverlayPresentationState _presentation = LyricsOverlayPresentationState.Empty;
    private HwndSource? _source;
    private int? _lastScrolledIndex;

    public LyricsOverlayWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
    }

    public double OverlayWidth => Width;
    public double OverlayHeight => Height;
    internal OverlayInteractionState Interaction => _interaction;
    internal TextBlock PrimaryTextForTesting => PrimaryText;
    internal TextBlock SecondaryTextForTesting => SecondaryText;
    internal ListBox AllLyricsListForTesting => AllLyricsList;
    internal bool CurrentLineMenuEnabledForTesting => CurrentLineMenuItem.IsEnabled;
    internal Brush SurfaceBackgroundForTesting => OverlaySurface.Background;
    public event Action? CloseRequested;
    public event Action<OverlayPosition>? DragCompleted;
    public event Action<OverlayCommand>? CommandRequested;
    public event Action<double>? WidthChangeCompleted;

    public void SetPosition(OverlayPosition position)
    {
        Left = position.Left;
        Top = position.Top;
    }

    public void SetLyrics(LyricsOverlayPresentationState state)
    {
        var currentChanged = _presentation.ContentMode != state.ContentMode ||
                             _presentation.CurrentIndex != state.CurrentIndex;
        _presentation = state;
        PrimaryText.Text = state.PrimaryText;
        SecondaryText.Text = state.SecondaryText;
        var allLyrics = state.ContentMode == LyricsContentMode.AllLyrics && state.AllLines.Count > 0;
        AllLyricsList.Visibility = allLyrics ? Visibility.Visible : Visibility.Collapsed;
        StackedLyrics.Visibility = allLyrics ? Visibility.Collapsed : Visibility.Visible;
        if (!allLyrics) return;

        AllLyricsList.ItemsSource = state.AllLines;
        var current = state.CurrentIndex is int index
            ? state.AllLines.FirstOrDefault(line => line.Index == index)
            : null;
        AllLyricsList.SelectedItem = current;
        if (currentChanged && current is not null) ScrollCurrentLineToCenter(current);
    }

    public void ApplyInteractionState(OverlayInteractionState state)
    {
        _interaction = state;
        OverlaySurface.Background = state.ClickThrough
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        Width = state.Width;
        Height = state.ContentMode switch
        {
            LyricsContentMode.OneLine => 150,
            LyricsContentMode.TwoLines => 220,
            LyricsContentMode.AllLyrics => 380,
            _ => 220
        };
        Topmost = state.Topmost;
        SecondaryText.Visibility = state.ContentMode == LyricsContentMode.TwoLines
            ? Visibility.Visible
            : Visibility.Collapsed;
        OneLineMenuItem.IsChecked = state.ContentMode == LyricsContentMode.OneLine;
        TwoLinesMenuItem.IsChecked = state.ContentMode == LyricsContentMode.TwoLines;
        AllLyricsMenuItem.IsChecked = state.ContentMode == LyricsContentMode.AllLyrics;
        LockedMenuItem.IsChecked = state.Locked;
        ClickThroughMenuItem.IsChecked = state.ClickThrough;
        TopmostMenuItem.IsChecked = state.Topmost;
        PrimaryText.MaxWidth = Math.Max(100, state.Width - 48);
        SecondaryText.MaxWidth = Math.Max(100, state.Width - 48);
    }

    public void ApplyTimingState(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs)
    {
        CurrentLineMenuItem.IsEnabled = currentLineEnabled;
        GlobalTimingMenuItem.IsEnabled = globalTimingEnabled;
    }

    public void ShowWithoutActivation() => Show();

    public void CloseForApplicationShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WindowProcedure);
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021)
        {
            handled = true;
            return new nint(MaNoActivate);
        }

        if (message != NativeOverlayClickThrough.WmNcHitTest || !_interaction.ClickThrough) return nint.Zero;
        var packed = lParam.ToInt64();
        var screenPoint = new WpfPoint(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
        var clientPoint = PointFromScreen(screenPoint);
        var overInteractiveContent = IsOverLyricsOrResizeGrip(clientPoint);
        if (OverlayHitTestPolicy.Decide(true, overInteractiveContent) == OverlayHitTestResult.Transparent)
        {
            handled = true;
            return new nint(NativeOverlayClickThrough.HtTransparent);
        }
        handled = true;
        return new nint(NativeOverlayClickThrough.HtClient);
    }

    private bool IsOverLyricsOrResizeGrip(WpfPoint point)
    {
        var hit = VisualTreeHelper.HitTest(this, point)?.VisualHit;
        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, HorizontalResizeGrip)) return true;
            if (current is TextBlock text && text.IsVisible && !string.IsNullOrEmpty(text.Text)) return true;
        }
        return false;
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_interaction.CanDragOnLyrics) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        DragCompleted?.Invoke(new(Left, Top));
    }

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange,
            OverlayPreferences.MinimumWidth, OverlayPreferences.MaximumWidth);
        PrimaryText.MaxWidth = Math.Max(100, Width - 48);
        SecondaryText.MaxWidth = Math.Max(100, Width - 48);
    }

    private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        WidthChangeCompleted?.Invoke(Width);
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<OverlayCommand>(tag, out var command))
            CommandRequested?.Invoke(command);
    }

    private void ScrollCurrentLineToCenter(PresentedLyricLine line)
    {
        if (_lastScrolledIndex == line.Index) return;
        _lastScrolledIndex = line.Index;
        AllLyricsList.ScrollIntoView(line);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var item = (ListBoxItem?)AllLyricsList.ItemContainerGenerator.ContainerFromItem(line);
            if (item is null) return;
            var scrollViewer = FindVisualChild<ScrollViewer>(AllLyricsList);
            if (scrollViewer is null) return;
            var itemTop = item.TransformToAncestor(scrollViewer).Transform(new WpfPoint(0, 0)).Y;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + itemTop -
                                                (scrollViewer.ViewportHeight - item.ActualHeight) / 2);
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        CloseRequested?.Invoke();
    }
}
