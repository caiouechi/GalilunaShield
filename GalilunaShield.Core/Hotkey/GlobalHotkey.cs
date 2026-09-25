using System.Runtime.InteropServices;

namespace GalilunaShield.Hotkey;

/// <summary>
/// Registers a system-wide hotkey (works even when the console window is not focused)
/// using a dedicated thread that runs a Win32 message loop.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [Flags]
    private enum Modifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Ctrl = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const int HotkeyId = 0x4753; // "GS"

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Thread _thread;
    private readonly Action _onPressed;
    private readonly Modifiers _modifiers;
    private readonly uint _virtualKey;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private Exception? _registrationError;

    public string Description { get; }

    public GlobalHotkey(string combo, Action onPressed)
    {
        _onPressed = onPressed;
        (_modifiers, _virtualKey, Description) = Parse(combo);
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "GlobalHotkey" };
    }

    public void Start()
    {
        _thread.Start();
        _ready.Wait();
        if (_registrationError is not null)
        {
            throw _registrationError;
        }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, (uint)(_modifiers | Modifiers.NoRepeat), _virtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            _registrationError = new InvalidOperationException(
                $"Could not register hotkey {Description} (Win32 error {err}). It may already be in use by another program.");
            _ready.Set();
            return;
        }

        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY && (int)msg.wParam == HotkeyId)
            {
                try { _onPressed(); }
                catch (Exception ex) { Console.Error.WriteLine($"Hotkey handler failed: {ex.Message}"); }
            }
        }

        UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private static (Modifiers, uint, string) Parse(string combo)
    {
        var mods = Modifiers.None;
        uint vk = 0;
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pretty = new List<string>();

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= Modifiers.Ctrl; pretty.Add("Ctrl"); break;
                case "alt": mods |= Modifiers.Alt; pretty.Add("Alt"); break;
                case "shift": mods |= Modifiers.Shift; pretty.Add("Shift"); break;
                case "win":
                case "windows": mods |= Modifiers.Win; pretty.Add("Win"); break;
                default:
                    if (vk != 0) throw new ArgumentException($"Hotkey '{combo}' has more than one non-modifier key.");
                    vk = ToVirtualKey(part);
                    pretty.Add(part.ToUpperInvariant());
                    break;
            }
        }

        if (vk == 0) throw new ArgumentException($"Hotkey '{combo}' has no main key (e.g. 'Ctrl+Alt+S').");
        return (mods, vk, string.Join("+", pretty));
    }

    private static uint ToVirtualKey(string key)
    {
        key = key.ToUpperInvariant();
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            return key[0]; // VK codes for 0-9 and A-Z match ASCII
        }

        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);
        }

        return key switch
        {
            "SPACE" => 0x20,
            "PAUSE" => 0x13,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "SCROLLLOCK" => 0x91,
            _ => throw new ArgumentException($"Unsupported hotkey key '{key}'."),
        };
    }
}
