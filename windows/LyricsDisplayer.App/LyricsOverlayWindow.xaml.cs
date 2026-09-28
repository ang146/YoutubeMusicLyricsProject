using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricsDisplayer.Core.Settings;
using WpfPoint = System.Windows.Point;
using MenuItem = System.Windows.Controls.MenuItem;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace LyricsDisplayer;

public enum OverlayHitTestResult
{
    Client,
    Transparent,
    ResizeLeft,
    ResizeRight,
    ResizeTop,
    ResizeBottom,
    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight
}

public static class OverlayHitTestPolicy
{
    public static OverlayHitTestResult Decide(
        bool clickThrough,
        bool overLyrics,
        OverlayHitTestResult resizeEdge)
    {
        if (overLyrics) return OverlayHitTestResult.Client;
        if (!clickThrough && resizeEdge != OverlayHitTestResult.Client) return resizeEdge;
        return clickThrough ? OverlayHitTestResult.Transparent : OverlayHitTestResult.Client;
    }
}

public partial class LyricsOverlayWindow : Window, ILyricsOverlayView
{
    private const int MaNoActivate = 3;
    private const double ResizeBorder = 7;
    private bool _allowClose;
    private OverlayInteractionState _interaction =
        OverlayInteractionState.FromPreferences(OverlayPreferences.Default);
    private LyricsOverlayPresentationState _presentation = LyricsOverlayPresentationState.Empty;
    private HwndSource? _source;

    public LyricsOverlayWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnWindowSizeChanged;
    }

    public double OverlayWidth => Width;
    public double OverlayHeight => Height;
    internal OverlayInteractionState Interaction => _interaction;
    internal TextBlock PrimaryTextForTesting => PrimaryText;
    internal TextBlock SecondaryTextForTesting => SecondaryText;
    internal ItemsControl AllLyricsItemsForTesting => AllLyricsItems;
    internal System.Windows.Controls.ContextMenu ContextMenuForTesting => OverlayContextMenu;
    internal IReadOnlyList<MenuItem> CurrentLineTimingMenuItemsForTesting =>
        [CurrentLineMinus500MenuItem, CurrentLineMinus100MenuItem,
            CurrentLinePlus100MenuItem, CurrentLinePlus500MenuItem];
    internal IReadOnlyList<MenuItem> GlobalTimingMenuItemsForTesting =>
        [GlobalMinus500MenuItem, GlobalMinus100MenuItem, GlobalResetMenuItem,
            GlobalPlus100MenuItem, GlobalPlus500MenuItem];
    internal Brush SurfaceBackgroundForTesting => OverlaySurface.Background;
    public event Action? CloseRequested;
    public event Action<OverlayPosition>? DragCompleted;
    public event Action<OverlayCommand>? CommandRequested;
    public event Action<double, double>? OverlaySizeChanged;
    public event Action<OverlayPosition, double, double>? GeometryChangeCompleted;

    public void SetPosition(OverlayPosition position)
    {
        Left = position.Left;
        Top = position.Top;
    }

    public void SetLyrics(LyricsOverlayPresentationState state)
    {
        _presentation = state;
        PrimaryText.Text = state.PrimaryText;
        SecondaryText.Text = state.SecondaryText;
        var allLyrics = state.ContentMode == LyricsContentMode.AllLyrics && state.AllLines.Count > 0;
        AllLyricsItems.Visibility = allLyrics ? Visibility.Visible : Visibility.Collapsed;
        StackedLyrics.Visibility = allLyrics ? Visibility.Collapsed : Visibility.Visible;
        AllLyricsItems.ItemsSource = allLyrics ? state.AllLines : null;
        PrimaryText.MaxWidth = Math.Max(100, Width - 56);
        SecondaryText.MaxWidth = Math.Max(100, Width - 56);
    }

    public void ApplyInteractionState(OverlayInteractionState state)
    {
        _interaction = state;
        OverlaySurface.Background = state.ClickThrough
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        if (Math.Abs(Width - state.Width) > 0.1) Width = state.Width;
        if (Math.Abs(Height - state.Height) > 0.1) Height = state.Height;
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
        PrimaryText.MaxWidth = Math.Max(100, state.Width - 56);
        SecondaryText.MaxWidth = Math.Max(100, state.Width - 56);
    }

    public void ApplyTimingState(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs)
    {
        foreach (var item in CurrentLineTimingMenuItemsForTesting) item.IsEnabled = currentLineEnabled;
        foreach (var item in GlobalTimingMenuItemsForTesting) item.IsEnabled = globalTimingEnabled;
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

        if (message == 0x0231) // WM_ENTERSIZEMOVE
        {
            return nint.Zero;
        }
        if (message == 0x0232) // WM_EXITSIZEMOVE
        {
            GeometryChangeCompleted?.Invoke(new(Left, Top), Width, Height);
            return nint.Zero;
        }

        if (message != NativeOverlayClickThrough.WmNcHitTest) return nint.Zero;
        var packed = lParam.ToInt64();
        var screenPoint = new WpfPoint(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
        var clientPoint = PointFromScreen(screenPoint);
        var overLyrics = IsOverLyrics(clientPoint);
        var edge = GetResizeEdge(clientPoint);
        var result = OverlayHitTestPolicy.Decide(_interaction.ClickThrough, overLyrics, edge);

        if (result == OverlayHitTestResult.Client)
        {
            handled = _interaction.ClickThrough;
            return handled ? new nint(NativeOverlayClickThrough.HtClient) : nint.Zero;
        }
        if (result == OverlayHitTestResult.Transparent)
        {
            handled = true;
            return new nint(NativeOverlayClickThrough.HtTransparent);
        }

        // Edge hit-testing only applies with click-through off. Lyrics retain priority over resize zones.
        handled = true;
        return new nint(ToNativeHitTest(edge));
    }

    private bool IsOverLyrics(WpfPoint point)
    {
        var hit = VisualTreeHelper.HitTest(this, point)?.VisualHit;
        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBlock text && text.IsVisible && !string.IsNullOrEmpty(text.Text)) return true;
        }
        return false;
    }

    private OverlayHitTestResult GetResizeEdge(WpfPoint point)
    {
        var left = point.X <= ResizeBorder;
        var right = point.X >= ActualWidth - ResizeBorder;
        var top = point.Y <= ResizeBorder;
        var bottom = point.Y >= ActualHeight - ResizeBorder;
        if (top && left) return OverlayHitTestResult.ResizeTopLeft;
        if (top && right) return OverlayHitTestResult.ResizeTopRight;
        if (bottom && left) return OverlayHitTestResult.ResizeBottomLeft;
        if (bottom && right) return OverlayHitTestResult.ResizeBottomRight;
        if (left) return OverlayHitTestResult.ResizeLeft;
        if (right) return OverlayHitTestResult.ResizeRight;
        if (top) return OverlayHitTestResult.ResizeTop;
        if (bottom) return OverlayHitTestResult.ResizeBottom;
        return OverlayHitTestResult.Client;
    }

    private static int ToNativeHitTest(OverlayHitTestResult result) => result switch
    {
        OverlayHitTestResult.ResizeLeft => NativeOverlayClickThrough.HtLeft,
        OverlayHitTestResult.ResizeRight => NativeOverlayClickThrough.HtRight,
        OverlayHitTestResult.ResizeTop => NativeOverlayClickThrough.HtTop,
        OverlayHitTestResult.ResizeTopLeft => NativeOverlayClickThrough.HtTopLeft,
        OverlayHitTestResult.ResizeTopRight => NativeOverlayClickThrough.HtTopRight,
        OverlayHitTestResult.ResizeBottom => NativeOverlayClickThrough.HtBottom,
        OverlayHitTestResult.ResizeBottomLeft => NativeOverlayClickThrough.HtBottomLeft,
        OverlayHitTestResult.ResizeBottomRight => NativeOverlayClickThrough.HtBottomRight,
        _ => NativeOverlayClickThrough.HtClient
    };

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) =>
        OverlaySizeChanged?.Invoke(Width, Height);

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

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<OverlayCommand>(tag, out var command))
            CommandRequested?.Invoke(command);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        CloseRequested?.Invoke();
    }
}
