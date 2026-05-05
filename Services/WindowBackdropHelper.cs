using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Orbital.Services
{
    internal enum WindowBackdropKind
    {
        MainWindow,
        TransientWindow
    }

    internal static class WindowBackdropHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_MICA_EFFECT = 1029;

        private const int DWMSBT_MAINWINDOW = 2;
        private const int DWMSBT_TRANSIENTWINDOW = 3;

        public static void TryApply(Window window, bool useDarkMode, WindowBackdropKind kind)
        {
            if (window is null)
            {
                return;
            }

            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                int dark = useDarkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                {
                    return;
                }

                int backdropType = kind == WindowBackdropKind.MainWindow
                    ? DWMSBT_MAINWINDOW
                    : DWMSBT_TRANSIENTWINDOW;

                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                {
                    DwmSetWindowAttribute(
                        hwnd,
                        DWMWA_SYSTEMBACKDROP_TYPE,
                        ref backdropType,
                        sizeof(int));
                    return;
                }

                int mica = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref mica, sizeof(int));
            }
            catch
            {
                // Backdrop is cosmetic. Ignore failures and keep default WPF rendering.
            }
        }
    }
}
