using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PondashiEX.UI;

internal static class NativeWindowTheme
{
    private static readonly ConditionalWeakTable<Control, ThemeState> States = new();

    public static void Apply(Control control, bool darkMode) =>
        States.GetValue(control, static target => new ThemeState(target)).Apply(darkMode);

    private sealed class ThemeState
    {
        private readonly Control _control;
        private bool? _darkMode;
        private bool _applying;

        public ThemeState(Control control)
        {
            _control = control;
            control.HandleCreated += (_, _) => ApplyCaption();
        }

        public void Apply(bool darkMode)
        {
            var changed = _darkMode != darkMode;
            _darkMode = darkMode;
            if (!_control.IsHandleCreated || _control.IsDisposed || _applying)
            {
                return;
            }

            _applying = true;
            try
            {
                if (changed)
                {
                    // Reapply native control themes when the project changes color mode.
                    SendMessage(_control.Handle, 0x031A /* WM_THEMECHANGED */, IntPtr.Zero, IntPtr.Zero);
                }

                ApplyCaption();
            }
            finally
            {
                _applying = false;
            }
        }

        private void ApplyCaption()
        {
            if (_control is not Form || !_control.IsHandleCreated || _control.IsDisposed
                || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                return;
            }

            var darkMode = _darkMode == true && !SystemInformation.HighContrast;
            var enabled = darkMode ? 1 : 0;
            var border = darkMode ? ColorTranslator.ToWin32(Color.FromArgb(78, 82, 88)) : -1;
            var caption = darkMode ? ColorTranslator.ToWin32(Color.FromArgb(30, 32, 35)) : -1;
            var text = darkMode ? ColorTranslator.ToWin32(Color.FromArgb(238, 240, 242)) : -1;
            _ = DwmSetWindowAttribute(_control.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(_control.Handle, 34 /* DWMWA_BORDER_COLOR */, ref border, sizeof(int));
            _ = DwmSetWindowAttribute(_control.Handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(_control.Handle, 36 /* DWMWA_TEXT_COLOR */, ref text, sizeof(int));
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);
}
