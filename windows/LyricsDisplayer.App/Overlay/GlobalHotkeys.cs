using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer;

[Flags]
public enum GlobalHotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    NoRepeat = 0x4000
}

public readonly record struct GlobalHotkeyGesture(GlobalHotkeyModifiers Modifiers, uint VirtualKey);

public interface IGlobalHotkeyPlatform : IDisposable
{
    event Action<int>? Pressed;
    bool Register(int id, GlobalHotkeyGesture gesture);
    void Unregister(int id);
}

public sealed class GlobalHotkeyService : IDisposable
{
    public const int ToggleOverlayId = 0x4C01;
    public const int ToggleClickThroughId = 0x4C02;
    public static readonly GlobalHotkeyGesture ToggleOverlayGesture =
        new(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt |
            GlobalHotkeyModifiers.Shift | GlobalHotkeyModifiers.NoRepeat, 0x4C);
    public static readonly GlobalHotkeyGesture ToggleClickThroughGesture =
        new(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt |
            GlobalHotkeyModifiers.Shift | GlobalHotkeyModifiers.NoRepeat, 0x54);

    private readonly IGlobalHotkeyPlatform _platform;
    private readonly Action _toggleOverlay;
    private readonly Action _toggleClickThrough;
    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly HashSet<int> _registered = [];
    private bool _started;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        IGlobalHotkeyPlatform platform,
        Action toggleOverlay,
        Action toggleClickThrough)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _platform = platform;
        _toggleOverlay = toggleOverlay;
        _toggleClickThrough = toggleClickThrough;
    }

    public IReadOnlyCollection<int> RegisteredIds => _registered;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _platform.Pressed += OnPressed;
        Register(ToggleOverlayId, ToggleOverlayGesture, "Ctrl+Alt+Shift+L");
        Register(ToggleClickThroughId, ToggleClickThroughGesture, "Ctrl+Alt+Shift+T");
    }

    public void Stop()
    {
        if (!_started) return;
        _platform.Pressed -= OnPressed;
        foreach (var id in _registered.ToArray()) _platform.Unregister(id);
        _registered.Clear();
        _started = false;
    }

    public void Dispose()
    {
        Stop();
        _platform.Dispose();
    }

    private void Register(int id, GlobalHotkeyGesture gesture, string description)
    {
        if (_platform.Register(id, gesture))
        {
            _registered.Add(id);
            _logger.LogInformation("Registered global shortcut {Gesture}.", description);
        }
        else
        {
            _logger.LogWarning("Global shortcut {Gesture} is unavailable; the application will continue without it.",
                description);
        }
    }

    private void OnPressed(int id)
    {
        if (!_registered.Contains(id)) return;
        if (id == ToggleOverlayId)
        {
            _logger.LogInformation("Global overlay-visibility shortcut triggered.");
            _toggleOverlay();
        }
        else if (id == ToggleClickThroughId)
        {
            _logger.LogInformation("Global click-through shortcut triggered.");
            _toggleClickThrough();
        }
    }
}

public sealed class Win32GlobalHotkeyPlatform : IGlobalHotkeyPlatform
{
    private const int HotkeyMessage = 0x0312;
    private readonly nint _windowHandle;
    private readonly HwndSource _source;

    public Win32GlobalHotkeyPlatform(nint windowHandle)
    {
        if (windowHandle == nint.Zero) throw new ArgumentException("A window handle is required.", nameof(windowHandle));
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle) ??
                  throw new InvalidOperationException("The window source is unavailable.");
        _source.AddHook(WindowProcedure);
    }

    public event Action<int>? Pressed;

    public bool Register(int id, GlobalHotkeyGesture gesture) =>
        RegisterHotKey(_windowHandle, id, (uint)gesture.Modifiers, gesture.VirtualKey);

    public void Unregister(int id) => UnregisterHotKey(_windowHandle, id);

    public void Dispose() => _source.RemoveHook(WindowProcedure);

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != HotkeyMessage) return nint.Zero;
        handled = true;
        Pressed?.Invoke(wParam.ToInt32());
        return nint.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
