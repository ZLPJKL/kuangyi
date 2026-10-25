using System.Windows.Input;
using System.Windows.Interop;
using KuangYi.App.Interop;
using KuangYi.Core.Services;

namespace KuangYi.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4B59;

    private HwndSource? _source;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? Pressed;

    public string CurrentGesture { get; private set; } = "F2";

    public string? LastError { get; private set; }

    public bool IsRegistered => _registered;

    public bool Register(string gesture)
    {
        Unregister();

        if (!HotkeyGesture.TryParse(gesture, out var parsed, out var error))
        {
            LastError = error;
            return false;
        }

        EnsureSource();

        var ok = NativeMethods.RegisterHotKey(
            _source!.Handle,
            HotkeyId,
            parsed.Modifiers | NativeMethods.ModNoRepeat,
            parsed.VirtualKey);

        if (!ok)
        {
            LastError = "快捷键已被其他程序占用";
            AppLog.Warn($"hotkey registration failed: {gesture}");
            return false;
        }

        _registered = true;
        CurrentGesture = gesture;
        LastError = null;
        return true;
    }

    public void Unregister()
    {
        if (_registered && _source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _registered = false;
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("KuangYi.HotkeyWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}

public sealed record ParsedHotkey(uint Modifiers, uint VirtualKey);

public static class HotkeyGesture
{
    public static bool TryParse(string? text, out ParsedHotkey parsed, out string? error)
    {
        parsed = new ParsedHotkey(0, 0);
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "快捷键格式不正确";
            return false;
        }

        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => NativeMethods.ModControl,
                "ALT" => NativeMethods.ModAlt,
                "SHIFT" => NativeMethods.ModShift,
                "WIN" or "WINDOWS" or "META" => NativeMethods.ModWin,
                _ => 0u,
            };

            if (flag == 0)
            {
                error = $"无法识别的修饰键：{parts[i]}";
                return false;
            }

            modifiers |= flag;
        }

        var keyText = parts[^1];
        Key key;
        if (keyText.Length == 1 && char.IsAsciiDigit(keyText[0]))
        {
            key = (Key)((int)Key.D0 + (keyText[0] - '0'));
        }
        else if (!Enum.TryParse(keyText, ignoreCase: true, out key))
        {
            error = $"无法识别的按键：{keyText}";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = "该按键不支持注册为全局热键";
            return false;
        }

        var keyValue = (int)key;
        var isFunctionKey = keyValue >= (int)Key.F1 && keyValue <= (int)Key.F24;
        if (modifiers == 0 && !isFunctionKey)
        {
            error = "字母或数字热键必须搭配 Ctrl / Alt / Shift / Win";
            return false;
        }

        if (key is Key.LWin or Key.RWin)
        {
            error = "不能使用 Win 键本身作为热键";
            return false;
        }

        parsed = new ParsedHotkey(modifiers, virtualKey);
        return true;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
