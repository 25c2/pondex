using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PondashiEX.Input;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly HookProcedure _procedure;
    private IntPtr _hook;

    public GlobalKeyboardHook()
    {
        _procedure = HookCallback;
    }

    public event EventHandler<GlobalKeyEventArgs>? KeyChanged;

    public bool IsEnabled => _hook != IntPtr.Zero;

    public void Enable()
    {
        if (IsEnabled)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _procedure, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("バックグラウンド入力用キーボードフックを開始できません。",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public void Disable()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Disable();

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;
            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<KeyboardData>(lParam);
                var key = (Keys)data.VirtualKey | Control.ModifierKeys;
                KeyChanged?.Invoke(this, new GlobalKeyEventArgs(key, isDown));
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardData
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure procedure,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

public sealed class GlobalKeyEventArgs : EventArgs
{
    public GlobalKeyEventArgs(Keys keyData, bool isDown)
    {
        KeyData = keyData;
        IsDown = isDown;
    }

    public Keys KeyData { get; }

    public bool IsDown { get; }
}

