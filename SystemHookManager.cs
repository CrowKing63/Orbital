using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbit
{
    public static class SystemHookManager
    {
        // Hook IDs
        private const int WH_MOUSE_LL = 14;

        // Mouse Messages
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP   = 0x0202;

        // Hit test
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT     = 1;

        // 드래그로 간주할 최소 픽셀 거리
        private const int DragThreshold = 8;

        // 롱프레스 시간 (ms)
        private const int LongPressMs = 500;

        private static MousePoint _buttonDownPos;
        private static bool _mouseDownInClient = false;
        private static Timer? _longPressTimer;
        private static bool _isLongPressed = false;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr _mouseHookID = IntPtr.Zero;
        private static HookProc _mouseProc = MouseHookCallback;

        /// <summary>마우스 드래그(텍스트 선택) 완료 시 발생</summary>
        public static event EventHandler<MousePoint>? OnMouseUp;

        /// <summary>마우스 버튼을 500ms 이상 누르고 있을 때 발생 (선택 없이 팝업 트리거용)</summary>
        public static event EventHandler<MousePoint>? OnLongPress;

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
            _longPressTimer?.Dispose();
            _longPressTimer = null;
            UnhookWindowsHookEx(_mouseHookID);
            _mouseHookID = IntPtr.Zero;
        }

        private static IntPtr SetHook(HookProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName!), 0);
            }
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    _buttonDownPos = hookStruct.pt;
                    _isLongPressed = false;

                    // 클릭한 위치가 창의 콘텐츠 영역인지 확인
                    IntPtr hwnd = WindowFromPoint(hookStruct.pt);
                    IntPtr hitParam = (IntPtr)(((hookStruct.pt.Y & 0xFFFF) << 16) | (hookStruct.pt.X & 0xFFFF));
                    IntPtr hitResult = SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, hitParam);
                    _mouseDownInClient = hitResult == (IntPtr)HTCLIENT;

                    // 롱프레스 타이머 시작
                    _longPressTimer?.Dispose();
                    _longPressTimer = new Timer(LongPressTimerCallback, null, LongPressMs, Timeout.Infinite);
                }
                else if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    // 타이머 취소
                    _longPressTimer?.Dispose();
                    _longPressTimer = null;

                    // 롱프레스가 이미 발생했으면 드래그 이벤트 스킵
                    if (!_isLongPressed && _mouseDownInClient)
                    {
                        int dx = Math.Abs(hookStruct.pt.X - _buttonDownPos.X);
                        int dy = Math.Abs(hookStruct.pt.Y - _buttonDownPos.Y);

                        if (dx > DragThreshold || dy > DragThreshold)
                        {
                            OnMouseUp?.Invoke(null, hookStruct.pt);
                        }
                    }

                    _isLongPressed = false;
                }
            }
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private static void LongPressTimerCallback(object? state)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = null;

            // 현재 커서 위치가 눌린 위치에서 크게 벗어나지 않은 경우만 롱프레스로 인정
            GetCursorPos(out MousePoint currentPos);
            int dx = Math.Abs(currentPos.X - _buttonDownPos.X);
            int dy = Math.Abs(currentPos.Y - _buttonDownPos.Y);

            if (dx <= DragThreshold && dy <= DragThreshold)
            {
                _isLongPressed = true;
                OnLongPress?.Invoke(null, currentPos);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(MousePoint pt);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out MousePoint lpPoint);

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
