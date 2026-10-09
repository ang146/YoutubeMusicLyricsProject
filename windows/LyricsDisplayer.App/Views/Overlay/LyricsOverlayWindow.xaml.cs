using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Controls;
using WpfPoint = System.Windows.Point;
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

public static class OverlayWindowStatePolicy
{
    private const uint ScMinimize = 0xF020;
    private const uint ScMaximize = 0xF030;
    private const uint ScMask = 0xFFF0;

    public static bool ShouldBlockSystemCommand(nint commandParameter)
    {
        var command = unchecked((uint)commandParameter.ToInt64()) & ScMask;
        return command is ScMinimize or ScMaximize;
    }
}

public partial class LyricsOverlayWindow : Window, ILyricsOverlayView
{
    private const int MaNoActivate = 3;
    private const double ResizeBorder = 7;
    private bool _allowClose;
    private bool _managedDragActive;
    private OverlayPixelPoint _dragGrabOffsetDips;
    private bool _geometryRecoveryRequired;
    private OverlayMonitorDescriptor? _resizeMonitor;
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
        StateChanged += OnWindowStateChanged;
    }

    public bool GeometryRecoveryRequired => _geometryRecoveryRequired;
    public double OverlayWidth => Width;
    public double OverlayHeight => Height;
    internal OverlayInteractionState Interaction => _interaction;
    internal TextBlock PrimaryTextForTesting => PrimaryText;
    internal TextBlock SecondaryTextForTesting => SecondaryText;
    internal ItemsControl AllLyricsItemsForTesting => AllLyricsItems;
    internal System.Windows.Controls.ContextMenu ContextMenuForTesting => OverlayContextMenu;
    internal Brush SurfaceBackgroundForTesting => OverlaySurface.Background;
    public event Action? CloseRequested;
    public event Action<OverlayPosition, double, double>? DragCompleted;
    public event Action<double, double>? OverlaySizeChanged;
    public event Action<OverlayPosition, double, double>? GeometryChangeCompleted;

    public void SetLyricsSurfaceActions(IOverlayLyricsSurfaceActionsViewModel actions) =>
        OverlaySurface.DataContext = actions ?? throw new ArgumentNullException(nameof(actions));

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

    public void ApplyInteractionState(OverlayInteractionState state, bool effectiveTopmost)
    {
        _interaction = state;
        OverlaySurface.Background = state.ClickThrough
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        if (Math.Abs(Width - state.Width) > 0.1) Width = state.Width;
        if (Math.Abs(Height - state.Height) > 0.1) Height = state.Height;
        Topmost = effectiveTopmost;
        SecondaryText.Visibility = state.ContentMode == LyricsContentMode.TwoLines
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayContextMenu.UpdateInteractionState(state);
        PrimaryText.MaxWidth = Math.Max(100, state.Width - 56);
        SecondaryText.MaxWidth = Math.Max(100, state.Width - 56);
    }

    public void ShowWithoutActivation()
    {
        NormalizeWindowState();
        Show();
    }

    public void NormalizeWindowState()
    {
        if (base.WindowState != System.Windows.WindowState.Normal)
            base.WindowState = System.Windows.WindowState.Normal;
    }

    public void CompleteGeometryRecovery() => _geometryRecoveryRequired = false;

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
        if (message == 0x0112 && OverlayWindowStatePolicy.ShouldBlockSystemCommand(wParam)) // WM_SYSCOMMAND
        {
            handled = true;
            return nint.Zero;
        }

        if (message == 0x0021)
        {
            handled = true;
            return new nint(MaNoActivate);
        }

        if (message == 0x0231) // WM_ENTERSIZEMOVE
        {
            var overlayHandle = new WindowInteropHelper(this).Handle;
            _resizeMonitor = DesktopWorkAreaProvider.TryGetCursorPosition(out var point)
                ? DesktopWorkAreaProvider.GetMonitorAt(point)
                : null;
            _resizeMonitor ??= DesktopWorkAreaProvider.GetMonitorForWindow(overlayHandle);
            if (_resizeMonitor is not null) ApplyMonitorMinimums(_resizeMonitor);
            return nint.Zero;
        }
        if (message == 0x0214 && _resizeMonitor is not null) // WM_SIZING; keep the active resize monitor fixed.
        {
            ConstrainNativeResize(wParam, lParam, _resizeMonitor);
            handled = true;
            return new nint(1);
        }
        if (message == 0x0232) // WM_EXITSIZEMOVE
        {
            GeometryChangeCompleted?.Invoke(new(Left, Top), Width, Height);
            _resizeMonitor = null;
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

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (base.WindowState == System.Windows.WindowState.Normal && !_geometryRecoveryRequired)
            OverlaySizeChanged?.Invoke(Width, Height);
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_interaction.CanDragOnLyrics) return;
        if (!DesktopWorkAreaProvider.TryGetCursorPosition(out _) || !OverlaySurface.CaptureMouse()) return;

        _managedDragActive = true;
        var grab = e.GetPosition(this);
        _dragGrabOffsetDips = new(grab.X, grab.Y);
        e.Handled = true;
    }

    private void OnSurfaceMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_managedDragActive || e.LeftButton != MouseButtonState.Pressed) return;
        if (!DesktopWorkAreaProvider.TryGetCursorPosition(out var cursor)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = DesktopWorkAreaProvider.GetMonitorAt(cursor) ?? DesktopWorkAreaProvider.GetMonitorForWindow(hwnd);
        if (monitor is null) return;

        var constrained = OverlayMonitorGeometry.ConstrainMove(
            cursor, _dragGrabOffsetDips, Width, Height, monitor);
        ApplyMonitorMinimums(monitor);
        if (Math.Abs(Width - constrained.WidthDips) > 0.1) Width = constrained.WidthDips;
        if (Math.Abs(Height - constrained.HeightDips) > 0.1) Height = constrained.HeightDips;
        DesktopWorkAreaProvider.SetWindowScreenPosition(hwnd, constrained.Position);
        e.Handled = true;
    }

    private void OnSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_managedDragActive) return;
        CompleteManagedDrag(releaseCapture: true);
        e.Handled = true;
    }

    private void OnSurfaceLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_managedDragActive) CompleteManagedDrag(releaseCapture: false);
    }

    private void CompleteManagedDrag(bool releaseCapture)
    {
        if (!_managedDragActive) return;
        _managedDragActive = false;
        if (releaseCapture && Mouse.Captured == OverlaySurface) OverlaySurface.ReleaseMouseCapture();
        if (base.WindowState == System.Windows.WindowState.Normal && !_geometryRecoveryRequired)
            DragCompleted?.Invoke(new(Left, Top), Width, Height);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (base.WindowState == System.Windows.WindowState.Normal) return;
        _geometryRecoveryRequired = true;
        NormalizeWindowState();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        CloseRequested?.Invoke();
    }

    private void ApplyMonitorMinimums(OverlayMonitorDescriptor monitor)
    {
        MinWidth = Math.Min(OverlayPreferences.MinimumWidth,
            monitor.WorkArea.Width / monitor.ScaleX);
        MinHeight = Math.Min(OverlayPreferences.MinimumHeight,
            monitor.WorkArea.Height / monitor.ScaleY);
    }

    private void ConstrainNativeResize(nint sizingEdge, nint rectanglePointer, OverlayMonitorDescriptor monitor)
    {
        var edge = (int)sizingEdge;
        var resizeEdge = edge switch
        {
            1 => OverlayResizeEdge.Left,
            2 => OverlayResizeEdge.Right,
            3 => OverlayResizeEdge.Top,
            4 => OverlayResizeEdge.TopLeft,
            5 => OverlayResizeEdge.TopRight,
            6 => OverlayResizeEdge.Bottom,
            7 => OverlayResizeEdge.BottomLeft,
            8 => OverlayResizeEdge.BottomRight,
            _ => (OverlayResizeEdge?)null
        };
        if (resizeEdge is null || rectanglePointer == 0) return;

        var requested = Marshal.PtrToStructure<NativeRectangle>(rectanglePointer);
        var workArea = monitor.WorkArea;
        var constrained = OverlayMonitorGeometry.ConstrainResize(
            new(requested.Left, requested.Top, requested.Right, requested.Bottom),
            workArea,
            resizeEdge.Value,
            Math.Min(OverlayPreferences.MinimumWidth * monitor.ScaleX, workArea.Width),
            Math.Min(OverlayPreferences.MinimumHeight * monitor.ScaleY, workArea.Height));
        Marshal.StructureToPtr(new NativeRectangle
        {
            Left = (int)Math.Round(constrained.Left),
            Top = (int)Math.Round(constrained.Top),
            Right = (int)Math.Round(constrained.Right),
            Bottom = (int)Math.Round(constrained.Bottom)
        }, rectanglePointer, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
