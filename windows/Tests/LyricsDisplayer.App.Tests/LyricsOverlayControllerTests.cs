using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsOverlayControllerTests
{
    [Test]
    public void ShowCreatesOneNonActivatingViewAndRepeatedShowDoesNotDuplicateIt()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.CreatedViews, Is.EqualTo(1));
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(1));
            Assert.That(harness.Controller.IsVisible, Is.True);
        });
    }

    [Test]
    public void HideAndShowReuseTheSameWindow()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Hide();
        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.CreatedViews, Is.EqualTo(1));
            Assert.That(harness.View.HideCalls, Is.EqualTo(1));
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(2));
            Assert.That(harness.Controller.IsVisible, Is.True);
        });
    }

    [Test]
    public void OverlayCloseOnlyHidesWhileApplicationShutdownReallyCloses()
    {
        var harness = new Harness();
        var visibility = new List<bool>();
        harness.Controller.VisibilityChanged += visibility.Add;
        harness.Controller.Show();
        harness.View.RequestClose();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.IsVisible, Is.False);
            Assert.That(harness.View.CloseForShutdownCalls, Is.Zero);
            Assert.That(visibility, Is.EqualTo(new[] { true, false }));
        });

        harness.Controller.Show();
        harness.Controller.Shutdown();
        Assert.Multiple(() =>
        {
            Assert.That(harness.View.CloseForShutdownCalls, Is.EqualTo(1));
            Assert.That(harness.Controller.HasCreatedWindow, Is.False);
        });
    }

    [Test]
    public void TimelineChangesUpdateTextButIdenticalPauseRefreshDoesNotRenderAgain()
    {
        var harness = new Harness();
        harness.Controller.Show();
        var position = Timeline("A", "B");
        harness.Controller.Update(TimedLyrics(), position);
        harness.Controller.Update(TimedLyrics(), position);

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.LastState, Is.EqualTo(new LyricsOverlayPresentationState("A", "B")));
            Assert.That(harness.View.SetLyricsCalls, Is.EqualTo(2),
                "One initial render plus one changed presentation; a paused refresh is a no-op.");
        });
    }

    [Test]
    public void SeekJumpsDirectlyWithoutIntermediatePresentation()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Update(TimedLyrics(), Timeline("Line 3", "Line 4"));
        harness.Controller.Update(TimedLyrics(), Timeline("Line 15", "Line 16"));

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo("Line 15"));
            Assert.That(harness.View.RenderedStates.Select(state => state.PrimaryText),
                Is.EqualTo(new[] { "", "Line 3", "Line 15" }));
        });
    }

    [Test]
    public void TrackChangeClearsAImmediatelyThenDisplaysB()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Update(TimedLyrics(), Timeline("Track A lyric", "Track A next"));
        harness.Controller.Update(null, EmptyTimeline());
        Assert.That(harness.View.LastState, Is.EqualTo(LyricsOverlayPresentationState.Empty));

        harness.Controller.Update(TimedLyrics(), Timeline("Track B lyric", "Track B next"));
        Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo("Track B lyric"));
    }

    [TestCase(false, false, LyricsOverlayPresentationState.NoLyricsText)]
    [TestCase(true, false, LyricsOverlayPresentationState.UntimedLyricsText)]
    public void TrackChangeClearsTimedTextBeforeKnownTrackBStatus(
        bool available, bool timed, string expectedStatus)
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Update(TimedLyrics(), Timeline("Track A lyric", "Track A next"));
        harness.Controller.Update(null, EmptyTimeline());
        Assert.That(harness.View.LastState, Is.EqualTo(LyricsOverlayPresentationState.Empty));

        harness.Controller.Update(Lyrics(available, timed), EmptyTimeline());

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo(expectedStatus));
            Assert.That(harness.View.LastState.SecondaryText, Is.Empty);
            Assert.That(harness.View.RenderedStates.Select(state => state.PrimaryText),
                Is.EqualTo(new[] { "", "Track A lyric", "", expectedStatus }));
        });
    }

    [TestCase(false, false, LyricsOverlayPresentationState.NoLyricsText)]
    [TestCase(true, false, LyricsOverlayPresentationState.UntimedLyricsText)]
    public void KnownStatusIsFullyReplacedWhenTimedLyricsBecomeActive(
        bool available, bool timed, string expectedStatus)
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Update(Lyrics(available, timed), EmptyTimeline());
        Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo(expectedStatus));

        harness.Controller.Update(TimedLyrics(), Timeline("A", "B"));
        Assert.That(harness.View.LastState, Is.EqualTo(new LyricsOverlayPresentationState("A", "B")));
    }

    [Test]
    public void PositionIsPersistedOnlyOnDragCompletion()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.View.SimulateMovement(new(100, 200));
        harness.View.SimulateMovement(new(300, 400));
        Assert.That(harness.PositionStore.Saved, Is.Empty);

        harness.View.CompleteDrag(new(300, 400));
        Assert.That(harness.PositionStore.Saved, Is.EqualTo(new[] { new OverlayPosition(300, 400) }));
    }

    [Test]
    public void VisibleSavedPositionRestoresAndInvalidPositionFallsBack()
    {
        var visible = new Harness(new OverlayPosition(100, 200));
        visible.Controller.Show();
        Assert.That(visible.View.Position, Is.EqualTo(new OverlayPosition(100, 200)));

        var invalid = new Harness(new OverlayPosition(5000, 5000));
        invalid.Controller.Show();
        Assert.That(invalid.View.Position, Is.Not.EqualTo(new OverlayPosition(5000, 5000)));
    }

    [Test]
    public void PreferencesLoadBeforeWindowCreationAndApplyWhenWindowIsCreated()
    {
        var preferences = new OverlayPreferences(true, true, false, OverlayDisplayMode.OneLine, 1200);
        var harness = new Harness(preferences: preferences);

        Assert.That(harness.Controller.Interaction,
            Is.EqualTo(OverlayInteractionState.FromPreferences(preferences)));
        harness.Controller.Show();
        Assert.That(harness.View.Interaction, Is.EqualTo(harness.Controller.Interaction));
    }

    [Test]
    public void PreferenceChangesApplyImmediatelyPersistAndNotifyOnce()
    {
        var harness = new Harness();
        var changes = new List<OverlayInteractionState>();
        harness.Controller.InteractionStateChanged += changes.Add;
        harness.Controller.Show();

        harness.Controller.SetLocked(true);
        harness.Controller.SetClickThrough(true);
        harness.Controller.SetTopmost(false);
        harness.Controller.SetDisplayMode(OverlayDisplayMode.OneLine);
        harness.Controller.SetWidth(1200);

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.Interaction, Is.EqualTo(harness.Controller.Interaction));
            Assert.That(harness.PositionStore.SavedPreferences, Has.Count.EqualTo(5));
            Assert.That(changes, Has.Count.EqualTo(5));
            Assert.That(harness.Controller.Interaction,
                Is.EqualTo(new OverlayInteractionState(true, true, false, OverlayDisplayMode.OneLine, 1200)));
        });
    }

    [Test]
    public void LockedOrClickThroughStateRejectsDragCompletion()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.SetLocked(true);
        harness.View.CompleteDrag(new(300, 400));
        harness.Controller.SetLocked(false);
        harness.Controller.SetClickThrough(true);
        harness.View.CompleteDrag(new(500, 600));

        Assert.That(harness.PositionStore.Saved, Is.Empty);
    }

    [Test]
    public void ControlPanelTransitionCanRecoverFromClickThrough()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { ClickThrough = true });
        harness.Controller.SetClickThrough(false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.ClickThrough, Is.False);
            Assert.That(harness.PositionStore.Preferences.ClickThrough, Is.False);
        });
    }

    [Test]
    public void OneLineModeUsesCurrentLineOrUpcomingLineAndClearsSecondary()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.Update(TimedLyrics(), Timeline("A", "B"));
        harness.Controller.SetDisplayMode(OverlayDisplayMode.OneLine);
        Assert.That(harness.View.LastState, Is.EqualTo(new LyricsOverlayPresentationState("A", "")));

        harness.Controller.Update(TimedLyrics(), new(null, null, 0, Line("First")));
        Assert.That(harness.View.LastState, Is.EqualTo(new LyricsOverlayPresentationState("First", "")));
    }

    [Test]
    public void ViewCommandsUseTheSameControllerTransitions()
    {
        var harness = new Harness();
        var opened = 0;
        harness.Controller.OpenControlPanelRequested += () => opened++;
        harness.Controller.Show();

        harness.View.RequestCommand(OverlayCommand.ToggleLocked);
        harness.View.RequestCommand(OverlayCommand.UseOneLine);
        harness.View.RequestCommand(OverlayCommand.OpenControlPanel);
        harness.View.RequestCommand(OverlayCommand.Hide);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Locked, Is.True);
            Assert.That(harness.Controller.Interaction.DisplayMode, Is.EqualTo(OverlayDisplayMode.OneLine));
            Assert.That(opened, Is.EqualTo(1));
            Assert.That(harness.Controller.IsVisible, Is.False);
        });
    }

    private static LyricsTimelinePosition Timeline(string current, string next) =>
        new(0, Line(current), 1, Line(next));

    private static LyricsTimelinePosition EmptyTimeline() => new(null, null, null, null);
    private static LyricsSnapshotPayload TimedLyrics() => Lyrics(available: true, timed: true);
    private static LyricsSnapshotPayload Lyrics(bool available, bool timed) =>
        new("track", available, timed, available ? "youtubeMusic" : null, [], null);
    private static LyricsLine Line(string text) => new(0, 1, text);

    private sealed class Harness
    {
        public Harness(OverlayPosition? savedPosition = null, OverlayPreferences? preferences = null)
        {
            PositionStore.Position = savedPosition;
            PositionStore.Preferences = preferences ?? OverlayPreferences.Default;
            Controller = new LyricsOverlayController(CreateView, PositionStore,
                () => [new OverlayWorkArea(0, 0, 1920, 1040)]);
        }

        public FakePositionStore PositionStore { get; } = new();
        public FakeView View { get; private set; } = null!;
        public LyricsOverlayController Controller { get; }
        public int CreatedViews { get; private set; }

        private ILyricsOverlayView CreateView()
        {
            CreatedViews++;
            return View = new FakeView();
        }
    }

    private sealed class FakePositionStore : IOverlaySettingsStore
    {
        public OverlayPosition? Position { get; set; }
        public OverlayPreferences Preferences { get; set; } = OverlayPreferences.Default;
        public List<OverlayPosition> Saved { get; } = [];
        public List<OverlayPreferences> SavedPreferences { get; } = [];
        public OverlayPosition? LoadOverlayPosition() => Position;
        public void SaveOverlayPosition(OverlayPosition position) => Saved.Add(position);
        public OverlayPreferences LoadOverlayPreferences() => Preferences;
        public void SaveOverlayPreferences(OverlayPreferences preferences)
        {
            Preferences = preferences;
            SavedPreferences.Add(preferences);
        }
    }

    private sealed class FakeView : ILyricsOverlayView
    {
        public bool IsVisible { get; private set; }
        public double Left => Position.Left;
        public double Top => Position.Top;
        public double OverlayWidth => Interaction?.Width ?? 900;
        public double OverlayHeight => Interaction?.DisplayMode == OverlayDisplayMode.OneLine ? 150 : 220;
        public event Action? CloseRequested;
        public event Action<OverlayPosition>? DragCompleted;
        public event Action<OverlayCommand>? CommandRequested;
        public OverlayPosition Position { get; private set; }
        public int ShowWithoutActivationCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int CloseForShutdownCalls { get; private set; }
        public int SetLyricsCalls { get; private set; }
        public LyricsOverlayPresentationState? LastState { get; private set; }
        public OverlayInteractionState? Interaction { get; private set; }
        public List<LyricsOverlayPresentationState> RenderedStates { get; } = [];

        public void SetPosition(OverlayPosition position) => Position = position;
        public void ApplyInteractionState(OverlayInteractionState state) => Interaction = state;
        public void SetLyrics(LyricsOverlayPresentationState state)
        {
            SetLyricsCalls++;
            LastState = state;
            RenderedStates.Add(state);
        }
        public void ShowWithoutActivation() { ShowWithoutActivationCalls++; IsVisible = true; }
        public void Hide() { HideCalls++; IsVisible = false; }
        public void CloseForApplicationShutdown() { CloseForShutdownCalls++; IsVisible = false; }
        public void RequestClose() { IsVisible = false; CloseRequested?.Invoke(); }
        public void SimulateMovement(OverlayPosition position) => Position = position;
        public void CompleteDrag(OverlayPosition position) { Position = position; DragCompleted?.Invoke(position); }
        public void RequestCommand(OverlayCommand command) => CommandRequested?.Invoke(command);
    }
}
