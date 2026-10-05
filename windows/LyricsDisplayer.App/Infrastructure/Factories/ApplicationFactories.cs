using System.Windows.Interop;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Infrastructure.Commands;
using Microsoft.Extensions.Logging;
using Unity;
using Unity.Resolution;

namespace LyricsDisplayer.Infrastructure.Factories;

public interface ISettingsWindowFactory { SettingsWindow Create(); }
public interface ILyricsOverlayViewFactory { ILyricsOverlayView Create(); }
public interface IMainLyricsViewModelFactory
{
    IMainLyricsViewModel Create(Action openEditor, Func<bool> canOpenEditor, Action openSettings);
}
public interface IBuiltInLyricsEditorFactory
{
    EditorWindowCreationResult Create(LocalTrackRecord track);
}
public interface IActiveLrcFileWatcherFactory
{
    ActiveLrcFileWatcher Create(string path, string? fingerprint, Action<ActiveLrcFileObservation> changed);
}
public interface IGlobalHotkeyServiceFactory
{
    GlobalHotkeyService Create(nint windowHandle, Action toggleOverlay, Action toggleClickThrough);
}
public interface ITrayLifecycleServiceFactory
{
    TrayLifecycleService Create(Action openControlPanel, Func<bool> toggleOverlay, Action exitApplication);
}

public sealed record EditorWindowCreationResult(EditorAssetStatus Status, string? Error,
    BuiltInLyricsEditorWindow? Window)
{
    public bool Succeeded => Status == EditorAssetStatus.Ready && Window is not null;
}

public sealed class SettingsWindowFactory(IUnityContainer container) : ISettingsWindowFactory
{
    public SettingsWindow Create() => container.Resolve<SettingsWindow>();
}

public sealed class LyricsOverlayViewFactory(IUnityContainer container) : ILyricsOverlayViewFactory
{
    public ILyricsOverlayView Create() => container.Resolve<ILyricsOverlayView>();
}

public sealed class MainLyricsViewModelFactory(ICommandFactory commands,
    ILogger<MainLyricsWindowViewModel> logger) : IMainLyricsViewModelFactory
{
    public IMainLyricsViewModel Create(Action openEditor, Func<bool> canOpenEditor, Action openSettings)
    {
        var editorCommand = commands.Create("application.open-built-in-editor", _ => openEditor(),
            _ => canOpenEditor(), CommandScope.Application);
        var settingsCommand = commands.Create("application.open-settings", _ => openSettings(),
            scope: CommandScope.Application);
        return new MainLyricsWindowViewModel(editorCommand, settingsCommand, logger);
    }
}

public sealed class BuiltInLyricsEditorFactory(IUnityContainer container, LyricsLibrary library,
    IApplicationLifetimeState lifetime) : IBuiltInLyricsEditorFactory
{
    public EditorWindowCreationResult Create(LocalTrackRecord track)
    {
        var loaded = library.LoadForEditing(track);
        if (loaded.Status != EditorAssetStatus.Ready || loaded.Asset is null)
            return new(loaded.Status, loaded.Error, null);
        var viewModel = container.Resolve<IBuiltInLyricsEditorViewModel>(
            new DependencyOverride<EditorAssetSnapshot>(loaded.Asset));
        return new(EditorAssetStatus.Ready, null, new BuiltInLyricsEditorWindow(viewModel, library, lifetime));
    }
}

public sealed class ActiveLrcFileWatcherFactory(ILogger<ActiveLrcFileWatcher> logger)
    : IActiveLrcFileWatcherFactory
{
    public ActiveLrcFileWatcher Create(string path, string? fingerprint, Action<ActiveLrcFileObservation> changed) =>
        new(logger, path, fingerprint, changed);
}

public sealed class GlobalHotkeyServiceFactory(ILogger<GlobalHotkeyService> logger)
    : IGlobalHotkeyServiceFactory
{
    public GlobalHotkeyService Create(nint windowHandle, Action toggleOverlay, Action toggleClickThrough) =>
        new(logger, new Win32GlobalHotkeyPlatform(windowHandle), toggleOverlay, toggleClickThrough);
}

public sealed class TrayLifecycleServiceFactory : ITrayLifecycleServiceFactory
{
    public TrayLifecycleService Create(Action openControlPanel, Func<bool> toggleOverlay, Action exitApplication) =>
        new(new WindowsTrayIcon(), openControlPanel, toggleOverlay, exitApplication);
}
