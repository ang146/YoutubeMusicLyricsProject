# Lyrics Displayer — Coding Agent Rules

This file contains repository-wide rules for Codex and other coding agents. Read it before making changes. Feature-specific details live under `docs/`; do not repeat all architecture rules in every task prompt.

## 1. Workflow

- Work on the branch requested by the task.
- Leave changes **uncommitted** unless the user explicitly asks you to commit.
- Do not merge branches unless explicitly requested.
- Keep scope narrow. Do not turn a small fix into an unrelated refactor.
- Inspect the existing implementation before adding a parallel abstraction.
- This project is still pre-release. Do **not** add migration/compatibility machinery for superseded development-only state unless explicitly requested.
- Preserve user-owned local lyrics and metadata. Never silently overwrite authoritative local edits with provider data.

## 2. Required architecture direction

Application construction must converge on one Unity composition root.

- Prefer constructor injection.
- Application ViewModels and managed application services must not be constructed ad hoc throughout the UI.
- Do not scatter `new XxxViewModel(...)` or `new XxxService(...)` when that type belongs to the container/factory-managed application graph.
- Ordinary ViewModels/services must not access `UnityContainer`, `IUnityContainer`, or call `Resolve<T>()` themselves.
- Container resolution belongs at the application composition root, focused factory implementations, or unavoidable framework boundaries.
- Do not add `ServiceFactory.GetContainer()` / global service-locator access.

### Fixed graph vs runtime creation

Use Unity constructor injection for fixed dependency graphs.

Use a focused factory when object creation has runtime parameters or lifecycle semantics, for example:

- Built-in editor sessions with a fixed `EditorTrack`
- windows/sessions that require single-instance or recreate-after-close behaviour
- future provider/search sessions with runtime input

Do not create a factory for every trivial class merely for consistency.

## 3. ViewModels

Main application ViewModels should expose a meaningful interface:

```csharp
public interface IFooViewModel
{
    // public contract used by Views/consumers
}

public sealed class FooViewModel : ViewModelBase, IFooViewModel
{
}
```

- Register `IXxxViewModel -> XxxViewModel` in Unity.
- Consumers should depend on the interface where practical.
- Do not create empty marker interfaces just to satisfy a pattern; expose the real consumer-facing contract.
- Tests of `XxxViewModel` itself instantiate the concrete implementation.
- Tests of a consumer of another ViewModel should normally mock that ViewModel's interface.

`ViewModelBase` owns common notification mechanics only:

- `INotifyPropertyChanged`
- `SetProperty`
- `OnPropertyChanged`
- explicit dependent-property notification
- protected logger access once logging infrastructure is wired

Do not turn it into a god ViewModel or hide application services inside it.

`EditableViewModelBase` is for simple editable/settings-style state. Do not migrate the Built-in Lyrics Editor's document/history dirty model onto it.

## 4. Logging

Use typed/category logging.

Preferred ViewModel pattern:

```csharp
public FooViewModel(
    ILogger<FooViewModel> logger,
    ...)
    : base(logger)
{
}
```

`ViewModelBase` may expose:

```csharp
protected ILogger Logger { get; }
```

Passing `ILogger<FooViewModel>` to the non-generic base `ILogger` must preserve the `FooViewModel` category.

Services may likewise use `ILogger<ConcreteService>`.

Reuse/adapt the existing Lyrics Displayer file logging backend rather than creating a second unrelated logging store.

Log meaningful events:

- construction / initialization / lifecycle
- command start/success/failure
- save/load/open/close operations
- meaningful state transitions
- recoverable abnormal states
- operation failures

Do **not** spam logs with:

- every playback tick
- every `PropertyChanged`
- every renderer refresh
- full lyrics payloads during ordinary operation

Use sensible levels:

- Debug: construction, initialization, command/lifecycle diagnostics
- Information: meaningful successful application/user operations
- Warning: recoverable abnormal states
- Error: failed operations
- Critical: unrecoverable/fatal failures

## 5. Commands

Application commands should use shared generic infrastructure:

- `RelayCommand`
- `AsyncRelayCommand`
- `ICommandFactory`

The old app-wide use of the name `EditorCommand` is too specific and should be replaced by the shared command abstraction during the M12 infrastructure refactor.

Outside command infrastructure itself:

- create commands through `ICommandFactory`
- do not scatter direct `new RelayCommand(...)` / `new AsyncRelayCommand(...)`
- keep command `CanExecute` invalidation selective
- do not call global `CommandManager.InvalidateRequerySuggested()` for every property change
- async commands are non-reentrant by default unless a real feature explicitly needs concurrency

Command construction should support central logging and exception handling without forcing every ViewModel command handler to repeat boilerplate.

## 6. Exception handling

The global WPF/AppDomain crash reporter is the **last-resort fatal boundary**, not the normal command error path.

Recoverable command/service failures should:

1. be caught at the appropriate application boundary
2. be logged with useful category/context
3. be routed through the shared application exception/error handling infrastructure
4. allow the app to continue where recovery is valid

Do not silently swallow exceptions.

Do not blanket-catch everything and pretend success.

Unrecoverable failures may escalate to the existing global fatal reporter and controlled shutdown path.

## 7. Testing

- NUnit is mandatory for .NET tests.
- Prefer NSubstitute for mocks when mocking is useful, unless the repository later establishes another mocking framework.
- Ordinary isolated unit tests should not start a Unity container.
- Instantiate the concrete class under test and mock its interfaces/dependencies.
- Add composition/registration smoke tests separately where they provide value.
- Do not add brittle WPF UI automation for behaviour that can be tested at ViewModel/application-policy level.
- Run the smallest relevant tests/build during narrow fixes; run the complete relevant suite before closing substantial tasks/milestones.

## 8. WPF views and file organisation

- Keep `App.xaml` and `MainWindow.xaml` at the application root unless there is a deliberate later restructure.
- Other WPF views belong under `Views/`.
- Settings views belong under `Views/Settings/` and each Settings section owns its own UserControl/page view.
- Built-in editor views belong under `Views/Editor/`.
- `SettingsWindow` owns navigation and selected-page hosting; do not inline every page's full XAML into the shell.
- Keep presentation/application logic out of code-behind unless it is genuinely view-only (focus, geometry, ScrollViewer manipulation, etc.).
- Do not add third-party MVVM/UI frameworks without explicit approval.

## 9. Desktop surface boundaries

### Main Lyrics Window

- normal, focusable, non-Topmost Windows window
- complete lyrics document only; no One/Two/All selector
- shared lyrics presentation, not a duplicate timeline engine
- current-line auto-follow is driven by semantic current-line changes, not every playback tick
- manual scrolling temporarily suspends follow; roughly five seconds of inactivity resumes/recenters
- scrollbar chrome may be hidden while scrolling remains enabled
- compact effective title/artist metadata is allowed

### Desktop Lyrics Overlay

- permanently Topmost while visible during normal operation
- temporary Topmost suppression only for the modal Built-in Editor
- OneLine / TwoLines / bounded contextual AllLyrics
- click-through / lock / drag / resize remain overlay-specific
- never move Topmost/click-through/geometry behaviour into shared lyrics presentation

### Settings

- fixed left navigation, independent right-side pages
- sections: General, Lyrics Overlay, Lyrics Window, Media Controller, Editor, Debug
- Debug owns technical diagnostics, paths, raw snapshots, logs/crash access

### Media Controller

Future independent surface. Do not prematurely add transport controls to Main Lyrics Window or Overlay.

## 10. Lyrics presentation and timeline boundaries

Keep these concerns separate:

```text
Lyrics state
!= content mode
!= layout style
!= appearance
!= karaoke animation
!= window behaviour
```

- Timeline selection belongs to Core/application timeline logic, not WPF presentation.
- Shared presentation may classify full-document lines as Past/Current/Upcoming/Status.
- Overlay contextual AllLyrics subset selection is not the same as Main Lyrics Window full-document presentation.
- Do not duplicate timestamp selection arithmetic in Views/ViewModels.

## 11. Built-in Editor invariants

Playback current lyric and editor selection are independent:

```text
Playback Current Line != Editor Selected Line
```

Playback must never automatically move the DataGrid selection.

Current M12 timing authoring:

```text
Shift All Timestamps
[-0.5s] [-0.1s] [+0.1s] [+0.5s]

Selected Line
[-0.5s] [-0.1s] [+0.1s] [+0.5s]

Set Time
```

- Shift All directly edits `EditorDocument` timestamps.
- Selected Line is row-oriented; selecting anywhere on the row is sufficient and all timestamps on that row shift together.
- One shift action is one Undo/Redo unit and participates in normal dirty/Save/Discard behaviour.
- `Set Time` uses playback only when playback identity matches the fixed editor track.
- `Set Time` appends to the first free T1–T5 slot and never overwrites an existing occurrence.
- The live `time | lyric` display may reflect committed unsaved editor text but must not drive selection.
- The old user-facing Global Offset / Reset / Bake workflow is superseded. Do not reintroduce it.

Do not silently sort, repair, interpolate, regionalize, or otherwise rewrite user-authored lyrics.

## 12. Local-first authority

- UTF-8 `.lrc` is authoritative lyric content.
- `track.lyrics.json` is portable metadata authority.
- `%LOCALAPPDATA%\LyricsDisplayer\library-index.db` is a local rebuildable index/cache, not portable authority.
- User edits are never silently overwritten by provider results.
- Effective metadata follows the established user-override/source fallback rules.
- External editing and Built-in Editor saves must preserve the existing safe-write/conflict semantics.

## 13. Browser / transport boundary

The Firefox extension and NativeHost are adapters/transport, not the application lyrics engine.

Do not move into them:

- local library authority
- editor logic
- provider aggregation/search framework
- presentation/timeline policy

NativeHost remains transport-only and must not write ordinary logs to stdout because stdout belongs to Native Messaging.

## 14. Future feature boundaries

Do not silently fold later milestones into unrelated work.

Examples:

- Lyrics Search/provider framework -> M13
- Fill Timestamp Pattern Down / semantic break settings -> M14
- appearance/karaoke/long-line rendering -> M15
- Media Controller -> M16
- additional playback/browser sources -> M17
- installer/distribution/custom-hotkey product polish -> M18

Future Lyrics Window preferences may include true edge-centering with viewport whitespace, title overflow modes (ellipsis / slow pan / marquee), and wheel/drag behaviour choices. Do not implement them unless the task asks for them.

## 15. Documentation

For architectural work, update the relevant files under `docs/` when behaviour/contracts change.

Key references:

- `docs/ARCHITECTURE.md`
- `docs/BUILT_IN_EDITOR.md`
- `docs/LYRICS_TIMING_ADJUSTMENT.md`
- `docs/OVERLAY_INTERACTION.md`
- `docs/FUTURE_FEATURES.md`

When this file conflicts with an explicitly newer user instruction, follow the user instruction and update this file so future agents receive the new rule.
