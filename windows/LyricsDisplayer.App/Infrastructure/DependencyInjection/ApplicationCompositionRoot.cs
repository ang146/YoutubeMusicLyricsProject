using System.IO;
using LyricsDisplayer.Core.Library;
using LyricsDisplayer.Core.Logging;
using LyricsDisplayer.Core.Playback;
using LyricsDisplayer.Core.Settings;
using LyricsDisplayer.Infrastructure.Commands;
using LyricsDisplayer.Infrastructure.Errors;
using LyricsDisplayer.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using Unity;
using Unity.Lifetime;
using Unity.Resolution;

namespace LyricsDisplayer.Infrastructure.DependencyInjection;

/// <summary>Owns application registrations and the one root resolution performed during startup.</summary>
public sealed class ApplicationCompositionRoot : IDisposable
{
    private readonly IUnityContainer _container = new UnityContainer();
    private readonly ApplicationLifetimeState _lifetime = new();
    private readonly string _applicationDataDirectory;
    private bool _initialized;

    internal CrashReportService CrashReports { get; }
    internal FatalExceptionCoordinator FatalExceptionCoordinator { get; }
    internal ApplicationLifetimeState Lifetime => _lifetime;
    internal ILogger<App> AppLogger => _container.Resolve<ILogger<App>>();
    internal bool IsInitialized => _initialized;

    public ApplicationCompositionRoot(string? applicationDataDirectory = null)
    {
        _applicationDataDirectory = applicationDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricsDisplayer");
        CrashReports = new CrashReportService(Path.Combine(_applicationDataDirectory, "Logs", "Crash"));
        FatalExceptionCoordinator = new FatalExceptionCoordinator(CrashReports, WriteLog, WriteFatalLog,
            () => { _lifetime.BeginFatalShutdown(); });
        _container.RegisterInstance(_lifetime);
        _container.RegisterInstance<IApplicationLifetimeState>(_lifetime);
        _container.RegisterInstance(CrashReports);
    }

    public void Initialize()
    {
        if (_initialized) return;
        var appData = _applicationDataDirectory;
        var logger = new SessionFileLogger(Path.Combine(appData, "Logs", "App"));
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new SessionFileLoggerProvider(logger));
        });
        var settings = new ApplicationSettingsStore(Path.Combine(appData, "settings.json"));

        _container.RegisterInstance(logger);
        _container.RegisterInstance<ILoggerFactory>(loggerFactory);
        _container.RegisterType(typeof(ILogger<>), typeof(Logger<>));
        _container.RegisterInstance(settings);
        _container.RegisterInstance<IOverlaySettingsStore>(settings);
        var library = new LyricsLibrary(LibraryPaths.Resolve(appData), loggerFactory.CreateLogger<LyricsLibrary>());
        _container.RegisterInstance(library);
        library.Initialise();
        _container.RegisterFactory<IExceptionHandler>(c => new ApplicationExceptionHandler(
            c.Resolve<ILogger<ApplicationExceptionHandler>>(), new WpfRecoverableExceptionPresenter()),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ICommandFactory>(c => new ApplicationCommandFactory(
            c.Resolve<ILoggerFactory>(), c.Resolve<IExceptionHandler>()), new ContainerControlledLifetimeManager());
        _container.RegisterType<SnapshotStateTracker>(new ContainerControlledLifetimeManager());
        _container.RegisterType<PlaybackClock>(new ContainerControlledLifetimeManager());
        _container.RegisterFactory<PlaybackStateCoordinator>(c => new PlaybackStateCoordinator(
            c.Resolve<ILogger<PlaybackStateCoordinator>>(), c.Resolve<SnapshotStateTracker>(),
            c.Resolve<PlaybackClock>(), c.Resolve<LyricsLibrary>()),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ILyricsTimingAdjustmentService>(_ => new LyricsTimingAdjustmentService(),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ICurrentLyricsTimingService>(c => new CurrentLyricsTimingService(
            c.Resolve<PlaybackStateCoordinator>(), c.Resolve<LyricsLibrary>(),
            c.Resolve<ILyricsTimingAdjustmentService>(), c.Resolve<ILogger<CurrentLyricsTimingService>>()),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<NamedPipeServer>(c => new NamedPipeServer(
            c.Resolve<ILogger<NamedPipeServer>>(), c.Resolve<PlaybackStateCoordinator>()), new ContainerControlledLifetimeManager());
        _container.RegisterType<ILyricsOverlayView, LyricsOverlayWindow>();
        _container.RegisterFactory<ILyricsOverlayViewFactory>(_ => new LyricsOverlayViewFactory(_container), new ContainerControlledLifetimeManager());
        _container.RegisterFactory<LyricsOverlayController>(c => new LyricsOverlayController(
            c.Resolve<ILogger<LyricsOverlayController>>(),
            () => c.Resolve<ILyricsOverlayViewFactory>().Create(), c.Resolve<IOverlaySettingsStore>(),
            DesktopWorkAreaProvider.GetVisibleWorkAreas),
            new ContainerControlledLifetimeManager());
        _container.RegisterType<ISettingsViewModel, SettingsViewModel>(new ContainerControlledLifetimeManager());
        _container.RegisterFactory<IDebugSettingsPageViewModel>(c => new DebugSettingsPageViewModel(
            c.Resolve<ICommandFactory>(), c.Resolve<ILogger<DebugSettingsPageViewModel>>(),
            library.Paths.LibraryPath, library.Paths.IndexPath, Path.GetDirectoryName(logger.CurrentPath),
            CrashReports.CrashDirectory), new ContainerControlledLifetimeManager());
        _container.RegisterType<IBuiltInLyricsEditorViewModel, BuiltInLyricsEditorViewModel>();
        _container.RegisterFactory<ISettingsWindowFactory>(_ => new SettingsWindowFactory(_container), new ContainerControlledLifetimeManager());
        _container.RegisterFactory<SettingsWindowService>(c => new SettingsWindowService(
            c.Resolve<ISettingsWindowFactory>(), c.Resolve<ILogger<SettingsWindowService>>()), new ContainerControlledLifetimeManager());
        _container.RegisterFactory<IBuiltInLyricsEditorFactory>(c => new BuiltInLyricsEditorFactory(
            _container, c.Resolve<LyricsLibrary>(), c.Resolve<IApplicationLifetimeState>()),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<CurrentLyricsFileService>(c => new CurrentLyricsFileService(
            c.Resolve<PlaybackStateCoordinator>(), c.Resolve<LyricsLibrary>(), c.Resolve<ExternalLrcOpener>(),
            c.Resolve<ILogger<CurrentLyricsFileService>>()), new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ICurrentLyricsFileService>(c => c.Resolve<CurrentLyricsFileService>(),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<IBuiltInLyricsEditorService>(c => new BuiltInLyricsEditorService(
            c.Resolve<PlaybackStateCoordinator>(), c.Resolve<LyricsLibrary>(), c.Resolve<IBuiltInLyricsEditorFactory>(),
            c.Resolve<LyricsOverlayController>(), c.Resolve<IApplicationLifetimeState>(),
            c.Resolve<ILogger<BuiltInLyricsEditorService>>()), new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ILyricsOverlayInteractionService>(c => c.Resolve<LyricsOverlayController>(),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<IMainLyricsSurfaceActionsViewModel>(c => new MainLyricsSurfaceActionsViewModel(
            c.Resolve<ICommandFactory>(), c.Resolve<ICurrentLyricsTimingService>(),
            c.Resolve<ICurrentLyricsFileService>(), c.Resolve<IBuiltInLyricsEditorService>(),
            c.Resolve<SettingsWindowService>(), c.Resolve<ILogger<MainLyricsSurfaceActionsViewModel>>()),
            new ContainerControlledLifetimeManager());
        _container.RegisterFactory<IOverlayLyricsSurfaceActionsViewModel>(c => new OverlayLyricsSurfaceActionsViewModel(
            c.Resolve<ICommandFactory>(), c.Resolve<ICurrentLyricsTimingService>(),
            c.Resolve<ICurrentLyricsFileService>(), c.Resolve<IBuiltInLyricsEditorService>(),
            c.Resolve<ILyricsOverlayInteractionService>(), c.Resolve<ILogger<OverlayLyricsSurfaceActionsViewModel>>()),
            new ContainerControlledLifetimeManager());
        _container.RegisterType<IMainLyricsViewModel, MainLyricsWindowViewModel>(
            new ContainerControlledLifetimeManager());
        _container.RegisterType<IActiveLrcFileWatcherFactory, ActiveLrcFileWatcherFactory>(new ContainerControlledLifetimeManager());
        _container.RegisterType<IGlobalHotkeyServiceFactory, GlobalHotkeyServiceFactory>(new ContainerControlledLifetimeManager());
        _container.RegisterType<ITrayLifecycleServiceFactory, TrayLifecycleServiceFactory>(new ContainerControlledLifetimeManager());
        _container.RegisterFactory<ExternalLrcOpener>(c => new ExternalLrcOpener(
            c.Resolve<ILogger<ExternalLrcOpener>>()), new ContainerControlledLifetimeManager());
        _container.RegisterType<MainWindow>();
        _initialized = true;
    }

    public MainWindow CreateMainWindow()
    {
        Initialize();
        return _container.Resolve<MainWindow>();
    }

    /// <summary>Resolves the non-visual application graph for startup diagnostics and composition tests.</summary>
    public void VerifyServiceGraph()
    {
        Initialize();
        _ = _container.Resolve<ICommandFactory>();
        _ = _container.Resolve<IExceptionHandler>();
        _ = _container.Resolve<PlaybackStateCoordinator>();
        _ = _container.Resolve<NamedPipeServer>();
        _ = _container.Resolve<LyricsOverlayController>();
        _ = _container.Resolve<ISettingsWindowFactory>();
        _ = _container.Resolve<ILyricsOverlayViewFactory>();
        var settingsViewModel = _container.Resolve<ISettingsViewModel>();
        if (!ReferenceEquals(settingsViewModel.DebugPage, _container.Resolve<IDebugSettingsPageViewModel>()))
            throw new InvalidOperationException("The Settings shell and live diagnostics must share one Debug ViewModel.");
        _ = _container.Resolve<ILyricsTimingAdjustmentService>();
        _ = _container.Resolve<ICurrentLyricsTimingService>();
        _ = _container.Resolve<ICurrentLyricsFileService>();
        _ = _container.Resolve<IBuiltInLyricsEditorService>();
        _ = _container.Resolve<IMainLyricsSurfaceActionsViewModel>();
        _ = _container.Resolve<IOverlayLyricsSurfaceActionsViewModel>();
        _ = _container.Resolve<IMainLyricsViewModel>();
        _ = _container.Resolve<IBuiltInLyricsEditorFactory>();
        _ = _container.Resolve<IActiveLrcFileWatcherFactory>();
        _ = _container.Resolve<IGlobalHotkeyServiceFactory>();
        _ = _container.Resolve<ITrayLifecycleServiceFactory>();
        _ = _container.Resolve<ExternalLrcOpener>();
        var unlistedLogger = _container.Resolve<ILogger<UnlistedLoggerCategory>>();
        unlistedLogger.LogInformation("Open-generic typed logger composition check.");

        var record = new LocalTrackRecord("composition-smoke", new UserTrackMetadata(null, null),
            "composition-smoke.lrc", "composition-smoke.json", null, null, []);
        var sidecar = new LyricsSidecar(1, record.LocalTrackId, [], record.UserMetadata,
            new LyricsAsset(record.LyricsRelativePath, null, null, DateTimeOffset.UnixEpoch));
        var editorViewModel = _container.Resolve<IBuiltInLyricsEditorViewModel>(
            new DependencyOverride<EditorAssetSnapshot>(new EditorAssetSnapshot(record, sidecar,
                record.LyricsRelativePath, record.SidecarRelativePath, string.Empty, string.Empty,
                string.Empty, false)));
        editorViewModel.Dispose();
    }

    public void Dispose()
    {
        if (_container.IsRegistered<LyricsLibrary>()) _container.Resolve<LyricsLibrary>().Dispose();
        if (_container.IsRegistered<ILoggerFactory>()) _container.Resolve<ILoggerFactory>().Dispose();
        if (_container.IsRegistered<SessionFileLogger>()) _container.Resolve<SessionFileLogger>().Dispose();
        _container.Dispose();
    }

    private void WriteLog(string level, string category, string message)
    {
        var logger = _container.IsRegistered<SessionFileLogger>() ? _container.Resolve<SessionFileLogger>() : null;
        if (logger is null) return;
        logger.Write(level, category, message);
    }

    private void WriteFatalLog(string level, string category, string message)
    {
        var logger = _container.IsRegistered<SessionFileLogger>() ? _container.Resolve<SessionFileLogger>() : null;
        logger?.WriteMultiline(level, category, message);
    }

    private sealed class UnlistedLoggerCategory { }
}
