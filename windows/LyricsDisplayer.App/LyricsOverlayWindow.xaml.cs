using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LyricsDisplayer.Core.Settings;

namespace LyricsDisplayer;

public partial class LyricsOverlayWindow : Window, ILyricsOverlayView
{
    private bool _allowClose;

    public LyricsOverlayWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public double OverlayWidth => Width;
    public double OverlayHeight => Height;
    public event Action? CloseRequested;
    public event Action<OverlayPosition>? DragCompleted;

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

    public void ShowWithoutActivation() => Show();

    public void CloseForApplicationShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        CloseRequested?.Invoke();
    }
}
