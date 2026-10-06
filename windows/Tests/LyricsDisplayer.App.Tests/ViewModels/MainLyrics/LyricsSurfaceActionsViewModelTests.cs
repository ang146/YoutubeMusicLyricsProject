using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Infrastructure.Factories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class LyricsSurfaceActionsViewModelTests
{
    [Test]
    public void SharedTimingCommandsRouteCurrentLineAndAllLyricsDeltas()
    {
        var timing = Substitute.For<ICurrentLyricsTimingService>();
        timing.AdjustCurrentLine(Arg.Any<long>()).Returns(new TimingAdjustmentResult(TimingAdjustmentStatus.Succeeded));
        timing.ShiftAll(Arg.Any<long>()).Returns(new TimingAdjustmentResult(TimingAdjustmentStatus.Succeeded));
        var actions = CreateMain(timing: timing);

        actions.AdjustCurrentLineMinus500.Command.Execute(null);
        actions.AdjustCurrentLineMinus100.Command.Execute(null);
        actions.AdjustCurrentLinePlus100.Command.Execute(null);
        actions.AdjustCurrentLinePlus500.Command.Execute(null);
        actions.ShiftAllMinus500.Command.Execute(null);
        actions.ShiftAllMinus100.Command.Execute(null);
        actions.ShiftAllPlus100.Command.Execute(null);
        actions.ShiftAllPlus500.Command.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(timing.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(timing.AdjustCurrentLine))
                .Select(call => (long)call.GetArguments()[0]!), Is.EqualTo(new long[] { -500, -100, 100, 500 }));
            Assert.That(timing.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(timing.ShiftAll))
                .Select(call => (long)call.GetArguments()[0]!), Is.EqualTo(new long[] { -500, -100, 100, 500 }));
            Assert.That(typeof(LyricsSurfaceActionsViewModel).GetProperties()
                .Any(property => property.Name.Contains("Reset", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(actions.OpenSettingsCommand.Identity.Scope, Is.EqualTo(CommandScope.Application));
        });
    }

    [Test]
    public void AvailabilityEventsRefreshOnlyRelevantCommandGroups()
    {
        var timing = Substitute.For<ICurrentLyricsTimingService>();
        timing.AdjustCurrentLine(Arg.Any<long>()).Returns(new TimingAdjustmentResult(TimingAdjustmentStatus.Succeeded));
        timing.ShiftAll(Arg.Any<long>()).Returns(new TimingAdjustmentResult(TimingAdjustmentStatus.Succeeded));
        var actions = CreateMain(timing: timing);
        var currentInvalidations = 0;
        var allInvalidations = 0;
        actions.AdjustCurrentLineMinus500.Command.CanExecuteChanged += (_, _) => currentInvalidations++;
        actions.ShiftAllMinus500.Command.CanExecuteChanged += (_, _) => allInvalidations++;

        timing.CanAdjustCurrentLine.Returns(true);
        timing.AvailabilityChanged += Raise.Event<Action>();
        Assert.Multiple(() =>
        {
            Assert.That(actions.AdjustCurrentLineMinus500.Command.CanExecute(null), Is.True);
            Assert.That(actions.ShiftAllMinus500.Command.CanExecute(null), Is.False);
            Assert.That(currentInvalidations, Is.EqualTo(1));
            Assert.That(allInvalidations, Is.Zero);
        });

        timing.CanShiftAll.Returns(true);
        timing.AvailabilityChanged += Raise.Event<Action>();
        Assert.Multiple(() =>
        {
            Assert.That(actions.ShiftAllMinus500.Command.CanExecute(null), Is.True);
            Assert.That(currentInvalidations, Is.EqualTo(1));
            Assert.That(allInvalidations, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExternalLyricsAndEditorAvailabilityComeFromTheirServices()
    {
        var timing = Substitute.For<ICurrentLyricsTimingService>();
        var file = Substitute.For<ICurrentLyricsFileService>();
        var editor = Substitute.For<IBuiltInLyricsEditorService>();
        file.Status.Returns("No current local LRC");
        var actions = CreateMain(timing: timing, file: file, editor: editor);
        var externalInvalidations = 0;
        var editorInvalidations = 0;
        actions.OpenLrcExternally.Command.CanExecuteChanged += (_, _) => externalInvalidations++;
        actions.OpenBuiltInEditor.Command.CanExecuteChanged += (_, _) => editorInvalidations++;

        Assert.That(actions.OpenLrcExternally.Command.CanExecute(null), Is.False);
        Assert.That(actions.OpenBuiltInEditor.Command.CanExecute(null), Is.False);
        file.CanOpen.Returns(true);
        file.AvailabilityChanged += Raise.Event<Action>();
        editor.CanOpen.Returns(true);
        editor.AvailabilityChanged += Raise.Event<Action>();
        actions.OpenLrcExternally.Command.Execute(null);
        actions.OpenBuiltInEditor.Command.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(actions.OpenLrcExternally.Command.CanExecute(null), Is.True);
            Assert.That(actions.OpenBuiltInEditor.Command.CanExecute(null), Is.True);
            Assert.That(externalInvalidations, Is.EqualTo(1));
            Assert.That(editorInvalidations, Is.EqualTo(1));
            file.Received(1).OpenExternally();
            editor.Received(1).Open();
        });

        file.Status.Returns("Watching current LRC");
        file.StatusChanged += Raise.Event<Action>();
        Assert.That(actions.ExternalLyricsStatus, Is.EqualTo("Watching current LRC"));
    }

    [Test]
    public void OverlayActionsContainOnlyOverlayCommandsAndRouteThroughInteractionService()
    {
        var overlay = Substitute.For<ILyricsOverlayInteractionService>();
        var initial = OverlayInteractionState.FromPreferences(OverlayPreferences.Default);
        overlay.Interaction.Returns(initial);
        var actions = new OverlayLyricsSurfaceActionsViewModel(TestCommandFactory.Instance,
            Substitute.For<ICurrentLyricsTimingService>(), Substitute.For<ICurrentLyricsFileService>(),
            Substitute.For<IBuiltInLyricsEditorService>(), overlay,
            NullLogger<OverlayLyricsSurfaceActionsViewModel>.Instance);

        actions.OpenLyricsWindowCommand.Execute(null);
        actions.SetOneLineCommand.Execute(null);
        actions.SetTwoLinesCommand.Execute(null);
        actions.SetAllLyricsCommand.Execute(null);
        actions.ToggleLockedCommand.Execute(null);
        actions.ToggleClickThroughCommand.Execute(null);
        actions.HideCommand.Execute(null);

        overlay.Received(1).RequestOpenLyricsWindow();
        overlay.Received(1).SetContentMode(LyricsContentMode.OneLine);
        overlay.Received(1).SetContentMode(LyricsContentMode.TwoLines);
        overlay.Received(1).SetContentMode(LyricsContentMode.AllLyrics);
        overlay.Received(1).SetLocked(!initial.Locked);
        overlay.Received(1).SetClickThrough(!initial.ClickThrough);
        overlay.Received(1).Hide();
        Assert.That(typeof(IOverlayLyricsSurfaceActionsViewModel).GetProperties()
            .Any(property => property.Name == "OpenSettingsCommand"), Is.False);

        var updated = initial with { Locked = !initial.Locked };
        overlay.InteractionStateChanged += Raise.Event<Action<OverlayInteractionState>>(updated);
        Assert.That(actions.Interaction, Is.EqualTo(updated));
    }

    private static MainLyricsSurfaceActionsViewModel CreateMain(
        ICurrentLyricsTimingService? timing = null,
        ICurrentLyricsFileService? file = null,
        IBuiltInLyricsEditorService? editor = null)
    {
        var factory = Substitute.For<ISettingsWindowFactory>();
        var settings = new SettingsWindowService(factory, NullLogger<SettingsWindowService>.Instance);
        return new(TestCommandFactory.Instance,
            timing ?? Substitute.For<ICurrentLyricsTimingService>(),
            file ?? Substitute.For<ICurrentLyricsFileService>(),
            editor ?? Substitute.For<IBuiltInLyricsEditorService>(), settings,
            NullLogger<MainLyricsSurfaceActionsViewModel>.Instance);
    }
}
