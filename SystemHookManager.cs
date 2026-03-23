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

        // (WM_NCHITTEST / HTCLIENT removed — client area is now checked geometrically)

        // Keyboard injection flag — set when the keystroke was synthesised (e.g. ClipboardHelper Ctrl+C)
        private const uint LLKHF_INJECTED = 0x00000010;

        // Virtual key codes
        private const int VK_SHIFT   = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_ALT     = 0x12;
        private const int VK_ESCAPE  = 0x1B;
        private const int VK_A       = 0x41;
        private const int VK_C       = 0x43;
        private const int VK_V       = 0x56;
        private const int VK_X       = 0x58;

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

        /// <summary>Screen position where the most recent left button down occurred.
        /// Captured before any mouse-up processing; safe to read in OnMouseUp handlers.</summary>
        public static MousePoint LastButtonDownPos => _buttonDownPos;

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

        /// <summary>User pressed Ctrl+C, Ctrl+X, or Ctrl+V — popup should hide to let the shortcut reach the target app.</summary>
        public static event EventHandler? OnClipboardShortcut;

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
                    // Walk up to the root window so that UWP/WinUI/WebView2 leaf HWNDs
                    // (whose ClientToScreen can fail or return wrong coordinates) are handled
                    // correctly. The root window always has reliable Win32 geometry.
                    IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                    if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;
                    // Exclude system shell windows (taskbar, desktop) from drag tracking.
                    // These windows have a client area but never contain selectable text.
                    _mouseDownInClient = IsPointInClientArea(rootHwnd, hookStruct.pt)
                                        && !IsSystemShellWindow(rootHwnd);

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

                        var pt = hookStruct.pt; // capture for closure
                        if (_isDoubleClick)
                        {
                            // Word selection is complete at mouse-up time
                            ThreadPool.QueueUserWorkItem(_ => OnDoubleClickRelease?.Invoke(null, pt));
                        }
                        else if (_isLongPressed && dx <= DragThreshold && dy <= DragThreshold)
                        {
                            ThreadPool.QueueUserWorkItem(_ => OnLongPress?.Invoke(null, pt));
                        }
                        else if (!_isLongPressed && (dx > DragThreshold || dy > DragThreshold))
                        {
                            ThreadPool.QueueUserWorkItem(_ => OnMouseUp?.Invoke(null, pt));
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

                        // ── Clipboard shortcuts — dismiss popup ──────────────────
                        if (_ctrlDown && (vk == VK_C || vk == VK_X || vk == VK_V))
                            OnClipboardShortcut?.Invoke(null, EventArgs.Empty);

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

        // ── Client-area / shell helpers ──────────────────────────────────────────
        /// <summary>
        /// Returns true for known Windows shell windows that should never trigger
        /// drag-selection detection: taskbar, secondary taskbar, and desktop.
        /// All Win32 calls; no UIA, no messages sent to the target window.
        /// </summary>
        private static bool IsSystemShellWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var buf = new System.Text.StringBuilder(64);
            GetClassName(hwnd, buf, buf.Capacity);
            return buf.ToString() is "Shell_TrayWnd"          // main taskbar
                                  or "Shell_SecondaryTrayWnd" // secondary-monitor taskbar
                                  or "Progman"                // desktop
                                  or "WorkerW";               // desktop (with wallpaper/icons)
        }

        /// <summary>
        /// Returns true when <paramref name="screenPt"/> lies inside the client area of
        /// <paramref name="hwnd"/>. Uses purely geometric Win32 calls — never sends a
        /// message to the target window, so it cannot block the hook thread.
        /// Falls back to the full window rect when ClientToScreen fails or returns an
        /// empty rect (can happen with UWP / WinUI / WebView2 windows).
        /// </summary>
        private static bool IsPointInClientArea(IntPtr hwnd, MousePoint screenPt)
        {
            if (hwnd == IntPtr.Zero) return false;
            if (GetClientRect(hwnd, out RECT clientRect) && clientRect.Right > 0 && clientRect.Bottom > 0)
            {
                POINT origin = new POINT { x = 0, y = 0 };
                if (ClientToScreen(hwnd, ref origin))
                {
                    return screenPt.X >= origin.x &&
                           screenPt.X <  origin.x + clientRect.Right &&
                           screenPt.Y >= origin.y &&
                           screenPt.Y <  origin.y + clientRect.Bottom;
                }
            }
            // Fallback: check against the full window rect (includes title bar, but always
            // returns valid screen coords — precise enough to exclude other app windows).
            if (!GetWindowRect(hwnd, out RECT winRect)) return false;
            return screenPt.X >= winRect.Left && screenPt.X < winRect.Right &&
                   screenPt.Y >= winRect.Top  && screenPt.Y < winRect.Bottom;
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
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(MousePoint pt);

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

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
