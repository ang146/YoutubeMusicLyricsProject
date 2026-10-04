using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class MainLyricsWindowViewModelTests
{
    [Test]
    public void TimedPresentationExposesEverySourceOrderedLineAndFollowsResolvedCurrentLine()
    {
        var documentLines = new[]
        {
            Line(3_000, "Third in time"),
            Line(1_000, "First in time"),
            Line(2_000, string.Empty),
            Line(4_000, "Last in time")
        };
        var lyrics = TimedLyrics(documentLines);
        var timeline = new LyricsTimeline(documentLines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var currentLineChanges = new List<MainLyricsLineViewModel?>();
        viewModel.CurrentLineChanged += currentLineChanges.Add;

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_500), timeline.OrderedLines);
        var originalItems = viewModel.Lines.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Lines.Select(line => line.Text), Is.EqualTo(new[]
            {
                "Third in time", "First in time", string.Empty, "Last in time"
            }));
            Assert.That(viewModel.Lines.Count, Is.EqualTo(documentLines.Length));
            Assert.That(viewModel.CurrentLine?.Text, Is.EqualTo("First in time"));
            Assert.That(viewModel.CurrentLine?.Role, Is.EqualTo(LyricLineRole.Current));
            Assert.That(viewModel.Lines[0].Role, Is.EqualTo(LyricLineRole.Upcoming));
            Assert.That(viewModel.Lines[2].Role, Is.EqualTo(LyricLineRole.Upcoming));
            Assert.That(viewModel.LyricsStatusText, Is.EqualTo("Synchronized lyrics"));
        });

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(3_500), timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CurrentLine?.Text, Is.EqualTo("Third in time"));
            Assert.That(viewModel.CurrentLine?.Role, Is.EqualTo(LyricLineRole.Current));
            Assert.That(viewModel.Lines[0], Is.SameAs(originalItems[0]), "Playback changes should retain document items.");
            Assert.That(viewModel.Lines[1], Is.SameAs(originalItems[1]));
            Assert.That(currentLineChanges.Select(line => line?.Text),
                Is.EqualTo(new string?[] { "First in time", "Third in time" }));
        });
    }

    [Test]
    public void UntimedLyricsRemainCompleteAndDoNotInventCurrentLine()
    {
        var lyrics = new LyricsSnapshotPayload("track", true, false, "local", [], null,
            ["First", string.Empty, "Last"]);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));

        viewModel.UpdateLyricsPresentation(lyrics, LyricsTimeline.Empty.Evaluate(0), []);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Lines.Select(line => line.Text), Is.EqualTo(new[] { "First", string.Empty, "Last" }));
            Assert.That(viewModel.Lines.All(line => line.Role == LyricLineRole.Neutral), Is.True);
            Assert.That(viewModel.CurrentLine, Is.Null);
            Assert.That(viewModel.LyricsStatusText, Is.EqualTo(LyricsPresentationMessages.Untimed));
        });
    }

    [Test]
    public void PendingUnavailableUntimedAndMissingStatesRemainDistinct()
    {
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var emptyTimeline = LyricsTimeline.Empty.Evaluate(0);

        viewModel.UpdateLyricsPresentation(null, emptyTimeline, []);
        var pending = viewModel.LyricsStatusText;
        var pendingMessage = viewModel.EmptyStateMessage;
        viewModel.UpdateLyricsPresentation(new("track", false, false, null, [], null), emptyTimeline, []);
        var unavailable = viewModel.LyricsStatusText;
        viewModel.UpdateLyricsPresentation(
            new("track", true, false, "local", [], null, ["untimed"]), emptyTimeline, []);
        var untimed = viewModel.LyricsStatusText;
        viewModel.UpdateLyricsPresentation(null, emptyTimeline, [], localFileMissing: true);
        var missing = viewModel.LyricsStatusText;

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.EqualTo("Waiting for lyrics"));
            Assert.That(pendingMessage, Is.Not.EqualTo(LyricsPresentationMessages.Unavailable));
            Assert.That(unavailable, Is.EqualTo(LyricsPresentationMessages.Unavailable));
            Assert.That(untimed, Is.EqualTo(LyricsPresentationMessages.Untimed));
            Assert.That(missing, Is.EqualTo(LyricsPresentationMessages.LocalFileMissing));
            Assert.That(new[] { pending, unavailable, untimed, missing }.Distinct().Count(), Is.EqualTo(4));
            Assert.That(LyricsPresentationMessages.Unavailable,
                Is.EqualTo(LyricsOverlayPresentationState.NoLyricsText));
            Assert.That(LyricsPresentationMessages.Untimed,
                Is.EqualTo(LyricsOverlayPresentationState.UntimedLyricsText));
            Assert.That(LyricsPresentationMessages.LocalFileMissing,
                Is.EqualTo(LyricsOverlayPresentationState.LocalFileMissingText));
        });
    }

    [Test]
    public void RepeatedPlaybackPositionWithSameResolvedIndexesDoesNotRemapFullDocument()
    {
        var lines = new[] { Line(1_000, "A"), Line(2_000, "B") };
        var lyrics = TimedLyrics(lines);
        var timeline = new LyricsTimeline(lines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_100), timeline.OrderedLines);
        var presentation = viewModel.Presentation;
        var items = viewModel.Lines.ToArray();
        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_900), timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Presentation, Is.SameAs(presentation));
            Assert.That(viewModel.Lines[0], Is.SameAs(items[0]));
            Assert.That(viewModel.Lines[1], Is.SameAs(items[1]));
        });
    }

    [Test]
    public void EditorEntryUsesTheExistingApplicationCommandAndEnablement()
    {
        var canExecute = false;
        var executed = 0;
        var command = new EditorCommand("application.open-built-in-editor", _ => executed++, _ => canExecute,
            EditorHotkeyScope.Application);
        var viewModel = new MainLyricsWindowViewModel(command);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.OpenBuiltInEditorCommand, Is.SameAs(command));
            Assert.That(viewModel.OpenBuiltInEditorCommand.CanExecute(null), Is.False);
        });

        canExecute = true;
        viewModel.OpenBuiltInEditorCommand.Execute(null);

        Assert.That(executed, Is.EqualTo(1));
    }

    private static LyricsSnapshotPayload TimedLyrics(IReadOnlyList<LyricsLine> lines) =>
        new("track", true, true, "local", lines, null);

    private static LyricsLine Line(long startMs, string text) => new(startMs, startMs, text);
}
