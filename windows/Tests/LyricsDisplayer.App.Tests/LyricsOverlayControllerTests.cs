using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Core.Timeline;
using System.Windows;

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
        harness.Controller.Hide();
        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.CreatedViews, Is.EqualTo(1));
            Assert.That(harness.View.HideCalls, Is.EqualTo(2));
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(3));
            Assert.That(harness.Controller.IsVisible, Is.True);
        });
    }

    [Test]
    public void MissingFilePresentationPersistsWhileHiddenAndClearsOnRestoredLyrics()
    {
        var harness = new Harness();
        harness.Controller.SetContentMode(LyricsContentMode.AllLyrics);
        harness.Controller.Update(null, EmptyTimeline(), localFileMissing: true);
        harness.Controller.Show();
        harness.Controller.Hide();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Presentation.PrimaryText,
                Is.EqualTo(LyricsOverlayPresentationState.LocalFileMissingText));
            Assert.That(harness.Controller.Presentation.AllLines, Is.Empty);
            Assert.That(harness.Controller.Presentation.IsLocalFileMissing, Is.True);
        });

        harness.Controller.Update(TimedLyrics() with { Lines = [Line("A"), Line("B")] }, Timeline("A", "B"));
        harness.Controller.Show();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Presentation.IsLocalFileMissing, Is.False);
            Assert.That(harness.Controller.Presentation.AllLines.Select(line => line.Text), Is.EqualTo(new[] { "A", "B" }));
        });
    }

    [Test]
    public void ExternalLrcMenuCommandUsesAvailabilityAndRoutesToSharedAction()
    {
        var harness = new Harness();
        var opens = 0;
        harness.Controller.OpenExternalLyricsRequested += () => opens++;
        harness.Controller.SetExternalLyricsAvailability(false);
        harness.Controller.Show();
        Assert.That(harness.View.CanOpenExternalLyrics, Is.False);
        harness.View.RequestCommand(OverlayCommand.OpenLrcExternally);
        Assert.That(opens, Is.Zero);

        harness.Controller.SetExternalLyricsAvailability(true);
        Assert.That(harness.View.CanOpenExternalLyrics, Is.True);
        harness.View.RequestCommand(OverlayCommand.OpenLrcExternally);
        Assert.That(opens, Is.EqualTo(1));
    }

    [Test]
    public void ShowRecoversOverlayWhenItsMonitorWasRemovedWhileHidden()
    {
        var harness = new Harness(new OverlayPosition(500, 260));
        harness.Controller.Show();
        harness.Controller.Hide();
        harness.WorkAreas = [new OverlayWorkArea(3000, 0, 640, 160, true)];

        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.Position, Is.EqualTo(new OverlayPosition(3000, 0)));
            Assert.That(harness.View.OverlayWidth, Is.EqualTo(640));
            Assert.That(harness.View.OverlayHeight, Is.EqualTo(160));
            Assert.That(harness.PositionStore.Saved.Last(), Is.EqualTo(new OverlayPosition(3000, 0)));
            Assert.That(harness.PositionStore.SavedPreferences.Last().Width, Is.EqualTo(640));
            Assert.That(harness.PositionStore.SavedPreferences.Last().Height, Is.EqualTo(160));
        });
    }

    [TestCase(WindowState.Maximized)]
    [TestCase(WindowState.Minimized)]
    public void ShowNormalizesAbnormalHiddenWindowStateAndRestoresSavedFloatingGeometry(WindowState state)
    {
        var preferences = OverlayPreferences.Default with { Width = 1040, Height = 280 };
        var harness = new Harness(new OverlayPosition(500, 260), preferences);
        harness.Controller.Show();
        harness.Controller.Hide();
        harness.View.SimulateAbnormalState(state);
        harness.View.CompleteResize(1920, 1080);

        Assert.That(harness.PositionStore.Saved, Is.Empty,
            "Geometry observed while the window is in an abnormal state must not replace floating geometry.");

        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.WindowState, Is.EqualTo(WindowState.Normal));
            Assert.That(harness.View.GeometryRecoveryRequired, Is.False);
            Assert.That(harness.View.Position, Is.EqualTo(new OverlayPosition(500, 260)));
            Assert.That(harness.View.OverlayWidth, Is.EqualTo(1040));
            Assert.That(harness.View.OverlayHeight, Is.EqualTo(280));
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(2));
            Assert.That(harness.CreatedViews, Is.EqualTo(1));
        });
    }

    [Test]
    public void LockedPositionStillAllowsResizeAndPersistsNormalGeometry()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.SetLocked(true);
        harness.View.CompleteResize(1100, 300);

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.Interaction!.CanDragOnLyrics, Is.False);
            Assert.That(harness.View.Interaction.CanResize, Is.True);
            Assert.That(harness.PositionStore.SavedPreferences.Last().Width, Is.EqualTo(1100));
            Assert.That(harness.PositionStore.SavedPreferences.Last().Height, Is.EqualTo(300));
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
        var preferences = new OverlayPreferences(true, true, false, LyricsContentMode.OneLine, 1200, 377);
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
        harness.Controller.SetContentMode(LyricsContentMode.OneLine);
        harness.Controller.SetWidth(1200);
        harness.Controller.SetHeight(377);

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.Interaction, Is.EqualTo(harness.Controller.Interaction));
            Assert.That(harness.PositionStore.SavedPreferences, Has.Count.EqualTo(6));
            Assert.That(changes, Has.Count.EqualTo(6));
            Assert.That(harness.Controller.Interaction,
                Is.EqualTo(new OverlayInteractionState(true, true, false, LyricsContentMode.OneLine, 1200, 377)));
        });
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, true, false)]
    public void EffectiveTopmostCombinesConfiguredPreferenceAndEditorSuppression(
        bool configuredTopmost, bool suppressed, bool expectedEffectiveTopmost)
    {
        var preferences = OverlayPreferences.Default with { Topmost = configuredTopmost };
        var harness = new Harness(preferences: preferences);
        harness.Controller.SetEditorModalTopmostSuppressed(suppressed);
        harness.Controller.Show();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Topmost, Is.EqualTo(configuredTopmost));
            Assert.That(harness.Controller.EffectiveTopmost, Is.EqualTo(expectedEffectiveTopmost));
            Assert.That(harness.View.Interaction!.Topmost, Is.EqualTo(configuredTopmost));
            Assert.That(harness.View.EffectiveTopmost, Is.EqualTo(expectedEffectiveTopmost));
            Assert.That(harness.PositionStore.SavedPreferences, Is.Empty,
                "Temporary editor suppression must not write the user's preference.");
        });
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, true)]
    public void ReleasingEditorSuppressionUsesLatestConfiguredTopmostPreference(
        bool initialPreference, bool latestPreference, bool expectedEffectiveTopmost)
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { Topmost = initialPreference });
        harness.Controller.Show();
        harness.Controller.SetEditorModalTopmostSuppressed(true);

        harness.Controller.SetTopmost(latestPreference);
        Assert.That(harness.Controller.EffectiveTopmost, Is.False);
        harness.Controller.SetEditorModalTopmostSuppressed(false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Topmost, Is.EqualTo(latestPreference));
            Assert.That(harness.Controller.EffectiveTopmost, Is.EqualTo(expectedEffectiveTopmost));
            Assert.That(harness.View.EffectiveTopmost, Is.EqualTo(expectedEffectiveTopmost));
            Assert.That(harness.PositionStore.Preferences.Topmost, Is.EqualTo(latestPreference));
        });
    }

    [Test]
    public void SuppressionRequestedBeforeOverlayCreationAppliesOnShowWithoutChangingVisibilityOnRelease()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { Topmost = true });
        harness.Controller.SetEditorModalTopmostSuppressed(true);
        harness.Controller.Show();
        Assert.That(harness.View.EffectiveTopmost, Is.False);

        harness.Controller.Hide();
        harness.Controller.SetEditorModalTopmostSuppressed(false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Topmost, Is.True);
            Assert.That(harness.View.EffectiveTopmost, Is.True);
            Assert.That(harness.Controller.IsVisible, Is.False);
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void SuppressionCanBeAppliedWhenNoOverlayInstanceExists()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { Topmost = true });

        Assert.DoesNotThrow(() => harness.Controller.SetEditorModalTopmostSuppressed(true));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.HasCreatedWindow, Is.False);
            Assert.That(harness.Controller.EffectiveTopmost, Is.False);
            Assert.That(harness.PositionStore.SavedPreferences, Is.Empty);
        });
    }

    [Test]
    public void HiddenOverlayRemainsHiddenWhileSuppressionIsReleased()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { Topmost = true });
        harness.Controller.Show();
        harness.Controller.Hide();
        harness.Controller.SetEditorModalTopmostSuppressed(true);
        Assert.That(harness.View.EffectiveTopmost, Is.False);

        harness.Controller.SetEditorModalTopmostSuppressed(false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.View.EffectiveTopmost, Is.True);
            Assert.That(harness.Controller.IsVisible, Is.False);
            Assert.That(harness.View.ShowWithoutActivationCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void SuppressionRemainsActiveUntilExplicitlyReleasedAfterModalReturn()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with { Topmost = true });
        harness.Controller.Show();
        harness.Controller.SetEditorModalTopmostSuppressed(true);

        // MainWindow releases suppression only in its finally block after ShowDialog returns.
        // A cancelled WPF Closing event keeps the modal call active and cannot reach that finally.
        Assert.That(harness.View.EffectiveTopmost, Is.False);
        harness.Controller.SetEditorModalTopmostSuppressed(false);

        Assert.That(harness.View.EffectiveTopmost, Is.True);
    }

    [Test]
    public void LockRejectsDragButUnlockedPartialClickThroughAllowsLyricDragCompletion()
    {
        var harness = new Harness();
        harness.Controller.Show();
        harness.Controller.SetLocked(true);
        harness.View.CompleteDrag(new(300, 400));
        harness.Controller.SetLocked(false);
        harness.Controller.SetClickThrough(true);
        harness.View.CompleteDrag(new(500, 600));

        Assert.That(harness.PositionStore.Saved, Is.EqualTo(new[] { new OverlayPosition(500, 600) }));
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
        harness.Controller.SetContentMode(LyricsContentMode.OneLine);
        Assert.Multiple(() =>
        {
            Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo("A"));
            Assert.That(harness.View.LastState.SecondaryText, Is.Empty);
        });

        harness.Controller.Update(TimedLyrics(), new(null, null, 0, Line("First")));
        Assert.Multiple(() =>
        {
            Assert.That(harness.View.LastState!.PrimaryText, Is.EqualTo("First"));
            Assert.That(harness.View.LastState.SecondaryText, Is.Empty);
        });
    }

    [Test]
    public void ViewCommandsUseTheSameControllerTransitions()
    {
        var harness = new Harness();
        var opened = 0;
        harness.Controller.OpenControlPanelRequested += () => opened++;
        harness.Controller.Show();

        harness.View.RequestCommand(OverlayCommand.ToggleLocked);
        harness.View.RequestCommand(OverlayCommand.SetOneLine);
        harness.View.RequestCommand(OverlayCommand.OpenControlPanel);
        harness.View.RequestCommand(OverlayCommand.Hide);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Locked, Is.True);
            Assert.That(harness.Controller.Interaction.ContentMode, Is.EqualTo(LyricsContentMode.OneLine));
            Assert.That(opened, Is.EqualTo(1));
            Assert.That(harness.Controller.IsVisible, Is.False);
        });
    }

    [Test]
    public void AllLyricsPresentationUpdatesRolesWhenTimelineCurrentChanges()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with
            { ContentMode = LyricsContentMode.AllLyrics });
        harness.Controller.Show();
        var lyrics = new LyricsSnapshotPayload("track", true, true, "local", [
            Line("A"), Line("B"), Line("C"), Line("D"), Line("E")
        ], null);

        harness.Controller.Update(lyrics, new(2, Line("C"), 3, Line("D")));
        var first = harness.View.LastState!;
        harness.Controller.Update(lyrics, new(3, Line("D"), 4, Line("E")));
        var second = harness.View.LastState!;

        Assert.Multiple(() =>
        {
            Assert.That(first.CurrentIndex, Is.EqualTo(2));
            Assert.That(first.AllLines.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(2));
            Assert.That(second.AllLines.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(3));
            Assert.That(second.CurrentIndex, Is.EqualTo(3));
        });
    }

    [Test]
    public void AllLyricsForwardAndBackwardSeekReplaceContextAroundExactNewOccurrence()
    {
        var harness = new Harness(preferences: OverlayPreferences.Default with
            { ContentMode = LyricsContentMode.AllLyrics, Height = 220 });
        harness.Controller.Show();
        var lines = Enumerable.Range(0, 100).Select(index => Line($"line {index}")).ToArray();
        var lyrics = new LyricsSnapshotPayload("track", true, true, "local", lines, null);

        harness.Controller.Update(lyrics, new(10, lines[10], 11, lines[11]));
        harness.Controller.Update(lyrics, new(80, lines[80], 81, lines[81]));
        var forward = harness.View.LastState!.AllLines;
        harness.Controller.Update(lyrics, new(2, lines[2], 3, lines[3]));
        var backward = harness.View.LastState!.AllLines;

        Assert.Multiple(() =>
        {
            Assert.That(forward.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(80));
            Assert.That(forward.Select(line => line.Index), Is.EqualTo(new[] { 79, 80, 81, 82 }));
            Assert.That(backward.Single(line => line.Role == LyricLineRole.Current).Index, Is.EqualTo(2));
            Assert.That(backward.Select(line => line.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        });
    }

    [Test]
    public void SwitchingContentModesNeverChangesPersistentOverlayGeometry()
    {
        var preferences = OverlayPreferences.Default with { Width = 1000, Height = 420 };
        var harness = new Harness(preferences: preferences);
        harness.Controller.Show();
        foreach (var mode in new[] { LyricsContentMode.OneLine, LyricsContentMode.TwoLines, LyricsContentMode.AllLyrics })
        {
            harness.Controller.SetContentMode(mode);
            Assert.That(harness.View.OverlayWidth, Is.EqualTo(1000));
            Assert.That(harness.View.OverlayHeight, Is.EqualTo(420));
            Assert.That(harness.PositionStore.Preferences.Width, Is.EqualTo(1000));
            Assert.That(harness.PositionStore.Preferences.Height, Is.EqualTo(420));
        }
    }

    [Test]
    public void AllLyricsContextRecalculatesImmediatelyWhenHeightChanges()
    {
        var preferences = OverlayPreferences.Default with
        {
            ContentMode = LyricsContentMode.AllLyrics,
            Height = 150
        };
        var harness = new Harness(preferences: preferences);
        harness.Controller.Show();
        var lines = Enumerable.Range(0, 100).Select(index => Line($"line {index}")).ToArray();
        var lyrics = new LyricsSnapshotPayload("track", true, true, "local", lines, null);
        harness.Controller.Update(lyrics, new(50, lines[50], 51, lines[51]));
        var initial = harness.View.LastState!.AllLines.Select(line => line.Index).ToArray();

        harness.View.CompleteResize(900, 500);
        var expanded = harness.View.LastState!.AllLines.Select(line => line.Index).ToArray();
        harness.View.CompleteResize(900, 150);
        var reduced = harness.View.LastState!.AllLines.Select(line => line.Index).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(initial, Is.EqualTo(new[] { 50, 51 }));
            Assert.That(expanded.Length, Is.GreaterThan(initial.Length));
            Assert.That(expanded, Does.Contain(51));
            Assert.That(expanded, Does.Contain(52));
            Assert.That(reduced, Is.EqualTo(new[] { 50, 51 }));
            Assert.That(reduced.Intersect(expanded).Count(), Is.EqualTo(reduced.Length));
            Assert.That(harness.View.LastState.AllLines.Single(line => line.Role == LyricLineRole.Current).Index,
                Is.EqualTo(50));
            Assert.That(harness.Controller.Interaction.Height, Is.EqualTo(150));
        });
    }

    [Test]
    public void TimingContextCommandsRouteToTheExistingTimingHandler()
    {
        var harness = new Harness();
        var commands = new List<OverlayCommand>();
        harness.Controller.TimingCommandRequested += commands.Add;
        harness.Controller.Show();
        var expected = new[]
        {
            OverlayCommand.AdjustCurrentLinePlus100,
            OverlayCommand.AdjustGlobalPlus500,
            OverlayCommand.ResetGlobalTiming
        };
        foreach (var command in expected) harness.View.RequestCommand(command);
        Assert.That(commands, Is.EqualTo(expected));
    }

    [Test]
    public void HorizontalResizeCompletionPersistsWidthAndRecoversWhenItWouldHideTheWindow()
    {
        var harness = new Harness(new OverlayPosition(1750, 300));
        harness.Controller.Show();
            harness.View.CompleteResize(1700, 420);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Controller.Interaction.Width, Is.EqualTo(1700));
            Assert.That(harness.Controller.Interaction.Height, Is.EqualTo(420));
            Assert.That(harness.PositionStore.SavedPreferences.Last().Width, Is.EqualTo(1700));
            Assert.That(harness.PositionStore.SavedPreferences.Last().Height, Is.EqualTo(420));
            Assert.That(OverlayPositionResolver.MeaningfullyIntersects(
                new(harness.View.Position.Left, harness.View.Position.Top, harness.View.OverlayWidth,
                    harness.View.OverlayHeight), new(0, 0, 1920, 1040)), Is.True);
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
                () => WorkAreas);
        }

        public FakePositionStore PositionStore { get; } = new();
        public IReadOnlyList<OverlayWorkArea> WorkAreas { get; set; } = [new(0, 0, 1920, 1040, true)];
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
        public bool CloseToTray { get; set; }
        public OverlayPosition? LoadOverlayPosition() => Position;
        public void SaveOverlayPosition(OverlayPosition position) => Saved.Add(position);
        public void SaveOverlayGeometry(OverlayPosition position, OverlayPreferences preferences)
        {
            Saved.Add(position);
            Preferences = preferences;
            SavedPreferences.Add(preferences);
        }
        public OverlayPreferences LoadOverlayPreferences() => Preferences;
        public void SaveOverlayPreferences(OverlayPreferences preferences)
        {
            Preferences = preferences;
            SavedPreferences.Add(preferences);
        }
        public bool LoadCloseControlPanelToTray() => CloseToTray;
        public void SaveCloseControlPanelToTray(bool value) => CloseToTray = value;
    }

    private sealed class FakeView : ILyricsOverlayView
    {
        public bool IsVisible { get; private set; }
        public WindowState WindowState { get; private set; } = WindowState.Normal;
        public bool GeometryRecoveryRequired { get; private set; }
        public double Left => Position.Left;
        public double Top => Position.Top;
        public double OverlayWidth => Interaction?.Width ?? 900;
        public double OverlayHeight => Interaction?.Height ?? OverlayPreferences.DefaultHeight;
        public event Action? CloseRequested;
        public event Action<OverlayPosition, double, double>? DragCompleted;
        public event Action<OverlayCommand>? CommandRequested;
        public event Action<double, double>? OverlaySizeChanged;
        public event Action<OverlayPosition, double, double>? GeometryChangeCompleted;
        public OverlayPosition Position { get; private set; }
        public int ShowWithoutActivationCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int CloseForShutdownCalls { get; private set; }
        public int SetLyricsCalls { get; private set; }
        public LyricsOverlayPresentationState? LastState { get; private set; }
        public OverlayInteractionState? Interaction { get; private set; }
        public List<LyricsOverlayPresentationState> RenderedStates { get; } = [];
        public bool CanOpenExternalLyrics { get; private set; }

        public void SetPosition(OverlayPosition position) => Position = position;
        public void NormalizeWindowState() => WindowState = WindowState.Normal;
        public void CompleteGeometryRecovery() => GeometryRecoveryRequired = false;
        public bool EffectiveTopmost { get; private set; }
        public void ApplyInteractionState(OverlayInteractionState state, bool effectiveTopmost)
        {
            Interaction = state;
            EffectiveTopmost = effectiveTopmost;
        }
        public void ApplyExternalLyricsAvailability(bool canOpen) => CanOpenExternalLyrics = canOpen;
        public void ApplyTimingState(bool currentLineEnabled, bool globalTimingEnabled, long globalOffsetMs) { }
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
        public void SimulateAbnormalState(WindowState state)
        {
            WindowState = state;
            GeometryRecoveryRequired = true;
        }
        public void CompleteDrag(OverlayPosition position)
        {
            Position = position;
            DragCompleted?.Invoke(position, OverlayWidth, OverlayHeight);
        }
        public void RequestCommand(OverlayCommand command) => CommandRequested?.Invoke(command);
        public void CompleteResize(double width, double height)
        {
            OverlaySizeChanged?.Invoke(width, height);
            GeometryChangeCompleted?.Invoke(Position, width, height);
        }
    }
}
