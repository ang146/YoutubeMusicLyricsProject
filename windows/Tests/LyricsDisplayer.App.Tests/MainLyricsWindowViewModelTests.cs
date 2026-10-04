using System.Collections.Specialized;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Protocol;
using LyricsDisplayer.Core.Timeline;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class MainLyricsWindowViewModelTests
{
    [Test]
    public void EffectiveMetadataUpdatesHeaderAndHidesUnavailableArtist()
    {
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var propertyChanges = new List<string?>();
        viewModel.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Lyrics Displayer"));
            Assert.That(viewModel.EffectiveArtist, Is.Empty);
            Assert.That(viewModel.HasEffectiveArtist, Is.False);
        });

        viewModel.SetEffectiveMetadata(new EffectiveTrackMetadata("Effective title", "Effective artist"));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Effective title"));
            Assert.That(viewModel.EffectiveArtist, Is.EqualTo("Effective artist"));
            Assert.That(viewModel.HasEffectiveArtist, Is.True);
            Assert.That(propertyChanges, Is.EqualTo(new[]
            {
                nameof(viewModel.EffectiveTitle), nameof(viewModel.EffectiveArtist),
                nameof(viewModel.HasEffectiveArtist)
            }));
        });

        viewModel.SetEffectiveMetadata(new EffectiveTrackMetadata("", " "));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Lyrics Displayer"));
            Assert.That(viewModel.EffectiveArtist, Is.Empty);
            Assert.That(viewModel.HasEffectiveArtist, Is.False);
        });
    }

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
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        viewModel.CurrentLineChanged += currentLineChanges.Add;
        viewModel.Lines.CollectionChanged += (_, args) => collectionChanges.Add(args);

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_500), timeline.OrderedLines);
        var originalItems = viewModel.Lines.ToArray();
        collectionChanges.Clear();

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
            Assert.That(viewModel.Lines[0], Is.Not.SameAs(originalItems[0]));
            Assert.That(viewModel.Lines[1], Is.Not.SameAs(originalItems[1]));
            Assert.That(viewModel.Lines[2], Is.Not.SameAs(originalItems[2]));
            Assert.That(viewModel.Lines[3], Is.SameAs(originalItems[3]),
                "Rows whose visible role did not change should keep their immutable row snapshot.");
            Assert.That(collectionChanges.Select(args => args.Action),
                Is.All.EqualTo(NotifyCollectionChangedAction.Replace));
            Assert.That(collectionChanges.SelectMany(args => args.NewItems!.Cast<MainLyricsLineViewModel>())
                    .Select(line => line.DocumentIndex),
                Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(currentLineChanges.Select(line => line?.Text),
                Is.EqualTo(new string?[] { "First in time", "Third in time" }));
        });
    }

    [Test]
    public void SequentialProgressionPublishesEachCurrentLineAndReplacesOnlyChangedRows()
    {
        var documentLines = Enumerable.Range(1, 15)
            .Select(index => Line(index * 1_000, $"Line {index}"))
            .ToArray();
        var lyrics = TimedLyrics(documentLines);
        var timeline = new LyricsTimeline(documentLines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var currentLineChanges = new List<int?>();
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        viewModel.CurrentLineChanged += line => currentLineChanges.Add(line?.DocumentIndex);
        viewModel.Lines.CollectionChanged += (_, args) => collectionChanges.Add(args);

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(2_000), timeline.OrderedLines);
        AssertRoles(viewModel, currentIndex: 1);
        collectionChanges.Clear();

        // Ten sequential lyric transitions ensure each semantic change reaches the observable
        // collection without rebuilding the full document or relying on scroll events.
        for (var currentIndex = 2; currentIndex <= 11; currentIndex++)
        {
            viewModel.UpdateLyricsPresentation(
                lyrics, timeline.Evaluate((currentIndex + 1) * 1_000L), timeline.OrderedLines);

            AssertRoles(viewModel, currentIndex);
            AssertReplacedDocumentIndexes(collectionChanges, currentIndex - 1, currentIndex);
            collectionChanges.Clear();
        }

        Assert.That(currentLineChanges, Is.EqualTo(Enumerable.Range(1, 11).Select(index => (int?)index)));
    }

    [Test]
    public void LargeForwardAndBackwardSeeksReplaceEveryRowWhoseRoleChanges()
    {
        var documentLines = Enumerable.Range(1, 15)
            .Select(index => Line(index * 1_000, $"Line {index}"))
            .ToArray();
        var lyrics = TimedLyrics(documentLines);
        var timeline = new LyricsTimeline(documentLines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Lines.CollectionChanged += (_, args) => collectionChanges.Add(args);

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(4_000), timeline.OrderedLines);
        AssertRoles(viewModel, currentIndex: 3);
        collectionChanges.Clear();

        // A forward seek must update the full affected range, not just the new current row.
        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(12_000), timeline.OrderedLines);
        AssertRoles(viewModel, currentIndex: 11);
        AssertReplacedDocumentIndexes(collectionChanges, Enumerable.Range(3, 9).ToArray());
        collectionChanges.Clear();

        // A backward seek restores Upcoming roles for every row after the new current line.
        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(4_000), timeline.OrderedLines);
        AssertRoles(viewModel, currentIndex: 3);
        AssertReplacedDocumentIndexes(collectionChanges, Enumerable.Range(3, 9).ToArray());
    }

    [Test]
    public void SameResolvedCurrentLineDoesNotReplaceRowsOrRepublishPresentation()
    {
        var lines = new[] { Line(1_000, "A"), Line(2_000, "B") };
        var lyrics = TimedLyrics(lines);
        var timeline = new LyricsTimeline(lines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var collectionChanges = new List<NotifyCollectionChangedEventArgs>();
        var currentLineChanges = 0;
        viewModel.Lines.CollectionChanged += (_, args) => collectionChanges.Add(args);
        viewModel.CurrentLineChanged += _ => currentLineChanges++;

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_100), timeline.OrderedLines);
        var presentation = viewModel.Presentation;
        var items = viewModel.Lines.ToArray();
        collectionChanges.Clear();

        viewModel.UpdateLyricsPresentation(lyrics, timeline.Evaluate(1_900), timeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Presentation, Is.SameAs(presentation));
            Assert.That(viewModel.Lines, Is.EqualTo(items));
            Assert.That(collectionChanges, Is.Empty);
            Assert.That(currentLineChanges, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplacingTrackDocumentRemovesOldRowsAndPublishesNewCurrentLine()
    {
        var oldLines = new[] { Line(1_000, "Old one"), Line(2_000, "Old two") };
        var oldTimeline = new LyricsTimeline(oldLines);
        var viewModel = new MainLyricsWindowViewModel(new EditorCommand("editor", _ => { }));
        var currentLineChanges = new List<MainLyricsLineViewModel?>();
        viewModel.CurrentLineChanged += currentLineChanges.Add;
        viewModel.UpdateLyricsPresentation(TimedLyrics(oldLines), oldTimeline.Evaluate(2_000), oldTimeline.OrderedLines);
        var oldCurrentLine = viewModel.CurrentLine;

        var newLines = new[] { Line(1_500, "New one"), Line(2_500, "New two"), Line(3_500, "New three") };
        var newTimeline = new LyricsTimeline(newLines);
        viewModel.UpdateLyricsPresentation(TimedLyrics(newLines), newTimeline.Evaluate(2_500), newTimeline.OrderedLines);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Lines.Select(line => line.Text),
                Is.EqualTo(new[] { "New one", "New two", "New three" }));
            Assert.That(viewModel.CurrentLine?.Text, Is.EqualTo("New two"));
            Assert.That(viewModel.CurrentLine, Is.Not.SameAs(oldCurrentLine));
            Assert.That(currentLineChanges.Select(line => line?.Text),
                Is.EqualTo(new string?[] { "Old two", "New two" }));
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

    private static void AssertRoles(MainLyricsWindowViewModel viewModel, int currentIndex)
    {
        var expected = Enumerable.Range(0, viewModel.Lines.Count)
            .Select(index => index < currentIndex ? LyricLineRole.Past :
                index == currentIndex ? LyricLineRole.Current : LyricLineRole.Upcoming);
        Assert.That(viewModel.Lines.Select(line => line.Role), Is.EqualTo(expected));
        Assert.That(viewModel.CurrentLine?.DocumentIndex, Is.EqualTo(currentIndex));
    }

    private static void AssertReplacedDocumentIndexes(
        IReadOnlyCollection<NotifyCollectionChangedEventArgs> changes,
        params int[] expectedIndexes)
    {
        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(args => args.Action),
                Is.All.EqualTo(NotifyCollectionChangedAction.Replace));
            Assert.That(changes.SelectMany(args => args.NewItems!.Cast<MainLyricsLineViewModel>())
                    .Select(line => line.DocumentIndex),
                Is.EquivalentTo(expectedIndexes));
        });
    }
}
