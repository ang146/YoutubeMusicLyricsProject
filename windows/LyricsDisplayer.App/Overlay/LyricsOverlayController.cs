using System.IO;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer;

public interface ILyricsOverlayView
{
    bool IsVisible { get; }
    double Left { get; }
    double Top { get; }
    double OverlayWidth { get; }
    double OverlayHeight { get; }
    event Action? CloseRequested;
    event Action<OverlayPosition>? DragCompleted;
    void SetPosition(OverlayPosition position);
    void SetLyrics(LyricsOverlayPresentationState state);
    void ShowWithoutActivation();
    void Hide();
    void CloseForApplicationShutdown();
}

public sealed class LyricsOverlayController
{
    private readonly Func<ILyricsOverlayView> _viewFactory;
    private readonly IOverlayPositionStore _positionStore;
    private readonly Func<IReadOnlyList<OverlayWorkArea>> _workAreas;
    private readonly Action<string, string, string>? _log;
    private ILyricsOverlayView? _view;
    private LyricsOverlayPresentationState _presentation = LyricsOverlayPresentationState.Empty;
    private bool _reportedVisible;
    private bool _shuttingDown;

    public LyricsOverlayController(
        Func<ILyricsOverlayView> viewFactory,
        IOverlayPositionStore positionStore,
        Func<IReadOnlyList<OverlayWorkArea>> workAreas,
        Action<string, string, string>? log = null)
    {
        _viewFactory = viewFactory;
        _positionStore = positionStore;
        _workAreas = workAreas;
        _log = log;
    }

    public event Action<bool>? VisibilityChanged;

    public bool IsVisible => _reportedVisible;
    public bool HasCreatedWindow => _view is not null;
    public LyricsOverlayPresentationState Presentation => _presentation;

    public void Update(LyricsSnapshotPayload? lyrics, LyricsTimelinePosition timeline)
    {
        var next = LyricsOverlayPresentationState.FromLyrics(lyrics, timeline);
        if (next == _presentation) return;
        _presentation = next;
        _view?.SetLyrics(next);
    }

    public void Show()
    {
        if (_shuttingDown) return;
        EnsureView();
        if (_reportedVisible) return;
        _view!.ShowWithoutActivation();
        _reportedVisible = true;
        _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay shown.");
        VisibilityChanged?.Invoke(true);
    }

    public void Hide()
    {
        if (_view is null || !_reportedVisible) return;
        _view.Hide();
        ReportHidden();
    }

    public void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        if (_view is null) return;
        _view.CloseRequested -= OnCloseRequested;
        _view.DragCompleted -= OnDragCompleted;
        _view.CloseForApplicationShutdown();
        _view = null;
        _reportedVisible = false;
    }

    private void EnsureView()
    {
        if (_view is not null) return;
        _view = _viewFactory();
        _view.CloseRequested += OnCloseRequested;
        _view.DragCompleted += OnDragCompleted;
        _view.SetLyrics(_presentation);

        var saved = _positionStore.LoadOverlayPosition();
        var placement = OverlayPositionResolver.Resolve(saved, _view.OverlayWidth, _view.OverlayHeight, _workAreas());
        _view.SetPosition(placement.Position);
        if (saved is not null && placement.UsedFallback)
            _log?.Invoke("Warning", "Overlay", "Saved overlay position was not visible; using fallback position.");
        else if (saved is not null)
            _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay position restored.");
    }

    private void OnCloseRequested()
    {
        if (_shuttingDown) return;
        if (_view?.IsVisible == true) _view.Hide();
        ReportHidden();
    }

    private void ReportHidden()
    {
        if (!_reportedVisible) return;
        _reportedVisible = false;
        _log?.Invoke("Information", "Overlay", "Desktop lyrics overlay hidden.");
        VisibilityChanged?.Invoke(false);
    }

    private void OnDragCompleted(OverlayPosition position)
    {
        try
        {
            _positionStore.SaveOverlayPosition(position);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log?.Invoke("Warning", "Overlay", $"Desktop lyrics overlay position could not be saved ({exception.GetType().Name}).");
        }
    }
}
