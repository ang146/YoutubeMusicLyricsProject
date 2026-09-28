using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public partial class LyricsOverlayWindow : Window, ILyricsOverlayView
{
    private bool _allowClose;
    private readonly IOverlayClickThroughAdapter _clickThrough;
    private OverlayInteractionState _interaction =
        OverlayInteractionState.FromPreferences(OverlayPreferences.Default);

    public LyricsOverlayWindow() : this(new NativeOverlayClickThrough())
    {
    }

    internal LyricsOverlayWindow(IOverlayClickThroughAdapter clickThrough)
    {
        _clickThrough = clickThrough;
        InitializeComponent();
        Closing += OnClosing;
        SourceInitialized += (_, _) => ApplyClickThrough();
    }

    public double OverlayWidth => Width;
    public double OverlayHeight => Height;
    public event Action? CloseRequested;
    public event Action<OverlayPosition>? DragCompleted;
    public event Action<OverlayCommand>? CommandRequested;

    public void SetPosition(OverlayPosition position)
    {
        Left = position.Left;
        Top = position.Top;
    }

    public void SetLyrics(LyricsOverlayPresentationState state)
    {
        if (PrimaryText.Text != state.PrimaryText) PrimaryText.Text = state.PrimaryText;
        if (SecondaryText.Text != state.SecondaryText) SecondaryText.Text = state.SecondaryText;
    }

    public void ApplyInteractionState(OverlayInteractionState state)
    {
        _interaction = state;
        Width = state.Width;
        Height = state.DisplayMode == OverlayDisplayMode.OneLine ? 150 : 220;
        Topmost = state.Topmost;
        SecondaryText.Visibility = state.DisplayMode == OverlayDisplayMode.OneLine
            ? Visibility.Collapsed
            : Visibility.Visible;
        LockedMenuItem.IsChecked = state.Locked;
        ClickThroughMenuItem.IsChecked = state.ClickThrough;
        TopmostMenuItem.IsChecked = state.Topmost;
        OneLineMenuItem.IsChecked = state.DisplayMode == OverlayDisplayMode.OneLine;
        TwoLinesMenuItem.IsChecked = state.DisplayMode == OverlayDisplayMode.TwoLines;
        ApplyClickThrough();
    }

    public void ShowWithoutActivation() => Show();

    public void CloseForApplicationShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_interaction.CanDrag) return;
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

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _clickThrough.SetClickThrough(handle, _interaction.ClickThrough);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        CloseRequested?.Invoke();
    }
}
