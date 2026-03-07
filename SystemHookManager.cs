using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orbit
{
    public static class SystemHookManager
    {
        // Hook IDs
        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;

        // Mouse Messages
        private const int WM_LBUTTONUP = 0x0202;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        private static IntPtr _mouseHookID = IntPtr.Zero;
        private static HookProc _mouseProc = MouseHookCallback;

        // Event to fire when left mouse button is released
        public static event EventHandler<MousePoint> OnMouseUp;

        [StructLayout(LayoutKind.Sequential)]
        public struct MousePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public MousePoint pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void StartMouseHook()
        {
            _mouseHookID = SetHook(_mouseProc);
        }

        public static void StopMouseHook()
        {
            if (_mouseHookID == IntPtr.Zero) return;
            UnhookWindowsHookEx(_mouseHookID);
            _mouseHookID = IntPtr.Zero;
        }

        private static IntPtr SetHook(HookProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                OnMouseUp?.Invoke(null, hookStruct.pt);
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
