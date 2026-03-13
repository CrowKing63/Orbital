using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbital
{
    public static class SystemHookManager
    {
        // Hook IDs
        private const int WH_MOUSE_LL    = 14;
        private const int WH_KEYBOARD_LL = 13;

        // Mouse Messages
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP   = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;

        // Keyboard Messages
        private const int WM_KEYDOWN    = 0x0100;
        private const int WM_KEYUP      = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP   = 0x0105;

        // Hit test
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT     = 1;

        // Keyboard injection flag — set when the keystroke was synthesised (e.g. ClipboardHelper Ctrl+C)
        private const uint LLKHF_INJECTED = 0x00000010;

        // Virtual key codes
        private const int VK_SHIFT   = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_ALT     = 0x12;
        private const int VK_ESCAPE  = 0x1B;
        private const int VK_A       = 0x41;

        // Navigation keys that, combined with Shift, select text
        private static readonly HashSet<int> s_navKeys = new()
        {
            0x25, 0x26, 0x27, 0x28, // Arrow: LEFT, UP, RIGHT, DOWN
            0x21, 0x22,             // Page UP, Page DOWN
            0x23, 0x24,             // END, HOME
        };

        // System metrics for double-click detection
        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        // Drag threshold (px)
        private const int DragThreshold = 8;

        // Long-press duration (ms)
        private const int LongPressMs = 300;

        // ── Mouse state ──────────────────────────────────────────────────────────
        private static MousePoint _buttonDownPos;
        private static bool  _mouseDownInClient;
        private static Timer? _longPressTimer;
        private static bool  _isLongPressed;
        private static bool  _isDoubleClick;

        // Double-click tracking
        private static DateTime  _lastClickTime = DateTime.MinValue;
        private static MousePoint _lastClickPos;

        // ── Keyboard state ───────────────────────────────────────────────────────
        private static bool _shiftDown;
        private static bool _ctrlDown;
        private static bool _pendingKeyboardSelection;

        // ── Custom hotkey (set by App at startup / settings change) ──────────────
        /// <summary>Modifier flags: Ctrl=0x02, Alt=0x01, Shift=0x04. 0 = no modifiers required.</summary>
        public static uint HotkeyModifiers  { get; set; }
        /// <summary>Virtual key code for the custom hotkey. 0 = disabled.</summary>
        public static uint HotkeyVirtualKey { get; set; }

        // ── Hook handles ─────────────────────────────────────────────────────────
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr   _mouseHookID    = IntPtr.Zero;
        private static IntPtr   _keyboardHookID = IntPtr.Zero;
        private static readonly HookProc _mouseProc    = MouseHookCallback;
        private static readonly HookProc _keyboardProc = KeyboardHookCallback;

        // ── Public events ────────────────────────────────────────────────────────

        /// <summary>Any mouse button down — used to dismiss the popup when clicking outside.</summary>
        public static event EventHandler<MousePoint>? OnAnyMouseDown;

        /// <summary>Left mouse drag (text selection) completed.</summary>
        public static event EventHandler<MousePoint>? OnMouseUp;

        /// <summary>Left mouse held for ≥ 300 ms without moving (long-press popup trigger).</summary>
        public static event EventHandler<MousePoint>? OnLongPress;

        /// <summary>Double-click detected (fires on LBUTTONDOWN). Kept for back-compat; prefer OnDoubleClickRelease.</summary>
        public static event EventHandler<MousePoint>? OnDoubleClick;

        /// <summary>Double-click mouse button released — OS has completed word selection by now.</summary>
        public static event EventHandler<MousePoint>? OnDoubleClickRelease;

        /// <summary>Keyboard text selection completed (Shift+nav key or Ctrl+A released).</summary>
        public static event EventHandler<MousePoint>? OnKeyboardSelection;

        /// <summary>Escape key pressed — caller should hide the popup.</summary>
        public static event EventHandler? OnEscapePressed;

        /// <summary>User-configured hotkey combination pressed.</summary>
        public static event EventHandler<MousePoint>? OnCustomHotkey;

        // ── Data structs ─────────────────────────────────────────────────────────
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

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;   // LLKHF_INJECTED = 0x10
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ── Public API ───────────────────────────────────────────────────────────
        public static bool StartMouseHook(out int errorCode)
        {
            _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL, out errorCode);
            return _mouseHookID != IntPtr.Zero;
        }

        public static bool StartKeyboardHook(out int errorCode)
        {
            _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL, out errorCode);
            return _keyboardHookID != IntPtr.Zero;
        }

        public static void StopHooks()
        {
            _longPressTimer?.Dispose();
            _longPressTimer = null;

            if (_mouseHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookID);
                _mouseHookID = IntPtr.Zero;
            }
            if (_keyboardHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookID);
                _keyboardHookID = IntPtr.Zero;
            }
        }

        // Keep old name for callers that use it
        public static void StopMouseHook() => StopHooks();

        // ── Hook setup ───────────────────────────────────────────────────────────
        private static IntPtr SetHook(HookProc proc, int hookType, out int errorCode)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule!;
            IntPtr hook = SetWindowsHookEx(hookType, proc,
                GetModuleHandle(curModule.ModuleName!), 0);
            errorCode = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            return hook;
        }

        // ── Mouse hook callback ──────────────────────────────────────────────────
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    OnAnyMouseDown?.Invoke(null, hookStruct.pt);

                    _buttonDownPos = hookStruct.pt;
                    _isLongPressed = false;
                    _isDoubleClick = false;

                    IntPtr hwnd = WindowFromPoint(hookStruct.pt);
                    IntPtr hitParam = (IntPtr)(((hookStruct.pt.Y & 0xFFFF) << 16) | (hookStruct.pt.X & 0xFFFF));
                    IntPtr hitResult = SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, hitParam);
                    _mouseDownInClient = hitResult == (IntPtr)HTCLIENT;

                    bool startLongPressTimer = true;

                    if (_mouseDownInClient)
                    {
                        DateTime now = DateTime.UtcNow;
                        double msSinceLast = (now - _lastClickTime).TotalMilliseconds;
                        int ddx = Math.Abs(hookStruct.pt.X - _lastClickPos.X);
                        int ddy = Math.Abs(hookStruct.pt.Y - _lastClickPos.Y);

                        if (msSinceLast <= GetDoubleClickTime() &&
                            ddx <= GetSystemMetrics(SM_CXDOUBLECLK) &&
                            ddy <= GetSystemMetrics(SM_CYDOUBLECLK))
                        {
                            _isDoubleClick      = true;
                            _lastClickTime      = DateTime.MinValue;
                            startLongPressTimer = false;
                            OnDoubleClick?.Invoke(null, hookStruct.pt);
                        }
                        else
                        {
                            _lastClickTime = now;
                            _lastClickPos  = hookStruct.pt;
                        }
                    }
                    else
                    {
                        _lastClickTime = DateTime.MinValue;
                    }

                    if (startLongPressTimer)
                    {
                        _longPressTimer?.Dispose();
                        _longPressTimer = new Timer(LongPressTimerCallback, null, LongPressMs, Timeout.Infinite);
                    }
                }
                else if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    _longPressTimer?.Dispose();
                    _longPressTimer = null;

                    if (_mouseDownInClient)
                    {
                        int dx = Math.Abs(hookStruct.pt.X - _buttonDownPos.X);
                        int dy = Math.Abs(hookStruct.pt.Y - _buttonDownPos.Y);

                        if (_isDoubleClick)
                        {
                            // Word selection is complete at mouse-up time
                            OnDoubleClickRelease?.Invoke(null, hookStruct.pt);
                        }
                        else if (_isLongPressed && dx <= DragThreshold && dy <= DragThreshold)
                        {
                            OnLongPress?.Invoke(null, hookStruct.pt);
                        }
                        else if (!_isLongPressed && (dx > DragThreshold || dy > DragThreshold))
                        {
                            OnMouseUp?.Invoke(null, hookStruct.pt);
                        }
                    }

                    _isLongPressed = false;
                    _isDoubleClick = false;
                }
                else if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    // Right-click dismisses the popup the same way a left-click outside does
                    OnAnyMouseDown?.Invoke(null, hookStruct.pt);
                }
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        // ── Keyboard hook callback ───────────────────────────────────────────────
        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT ks = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Ignore synthetic keystrokes (e.g. Ctrl+C/V injected by ClipboardHelper)
                if ((ks.flags & LLKHF_INJECTED) == 0)
                {
                    int  vk        = (int)ks.vkCode;
                    bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                    bool isKeyUp   = wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP;

                    if (isKeyDown)
                    {
                        if (vk == VK_SHIFT)   _shiftDown = true;
                        if (vk == VK_CONTROL) _ctrlDown  = true;

                        // Track intent to select text with keyboard
                        if (_shiftDown && s_navKeys.Contains(vk))
                            _pendingKeyboardSelection = true;

                        if (_ctrlDown && vk == VK_A)
                            _pendingKeyboardSelection = true;

                        // ── Custom hotkey ────────────────────────────────────────
                        if (HotkeyVirtualKey != 0 && (uint)vk == HotkeyVirtualKey)
                        {
                            bool wantCtrl  = (HotkeyModifiers & 0x02) != 0;
                            bool wantAlt   = (HotkeyModifiers & 0x01) != 0;
                            bool wantShift = (HotkeyModifiers & 0x04) != 0;

                            bool altDown = (GetAsyncKeyState(VK_ALT) & 0x8000) != 0;

                            if (_ctrlDown  == wantCtrl &&
                                altDown    == wantAlt  &&
                                _shiftDown == wantShift)
                            {
                                GetCursorPos(out MousePoint pt);
                                OnCustomHotkey?.Invoke(null, pt);
                            }
                        }
                    }
                    else if (isKeyUp)
                    {
                        if (vk == VK_ESCAPE)
                            OnEscapePressed?.Invoke(null, EventArgs.Empty);

                        // Shift released after a nav-key selection
                        if (vk == VK_SHIFT && _pendingKeyboardSelection)
                        {
                            _pendingKeyboardSelection = false;
                            GetCursorPos(out MousePoint pt);
                            OnKeyboardSelection?.Invoke(null, pt);
                        }

                        // Ctrl+A released
                        if (vk == VK_A && _ctrlDown && _pendingKeyboardSelection)
                        {
                            _pendingKeyboardSelection = false;
                            GetCursorPos(out MousePoint pt);
                            OnKeyboardSelection?.Invoke(null, pt);
                        }

                        if (vk == VK_SHIFT)   _shiftDown = false;
                        if (vk == VK_CONTROL) _ctrlDown  = false;
                    }
                }
            }
            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        // ── Long-press timer ─────────────────────────────────────────────────────
        private static void LongPressTimerCallback(object? state)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = null;

            GetCursorPos(out MousePoint currentPos);
            int dx = Math.Abs(currentPos.X - _buttonDownPos.X);
            int dy = Math.Abs(currentPos.Y - _buttonDownPos.Y);

            if (dx <= DragThreshold && dy <= DragThreshold)
                _isLongPressed = true;
        }

        // ── Win32 imports ────────────────────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(MousePoint pt);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out MousePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

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
