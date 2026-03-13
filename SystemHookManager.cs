using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Orbital
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
        private const int LongPressMs = 300;

        private static MousePoint _buttonDownPos;
        private static bool _mouseDownInClient = false;
        private static Timer? _longPressTimer;
        private static bool _isLongPressed = false;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr _mouseHookID = IntPtr.Zero;
        private static HookProc _mouseProc = MouseHookCallback;

        /// <summary>마우스 버튼이 눌릴 때 항상 발생 (팝업 외부 클릭 감지용)</summary>
        public static event EventHandler<MousePoint>? OnAnyMouseDown;

        /// <summary>마우스 드래그(텍스트 선택) 완료 시 발생</summary>
        public static event EventHandler<MousePoint>? OnMouseUp;

        /// <summary>마우스 버튼을 300ms 이상 누르고 있을 때 발생 (선택 없이 팝업 트리거용)</summary>
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

        public static bool StartMouseHook(out int errorCode)
        {
            _mouseHookID = SetHook(_mouseProc, out errorCode);
            return _mouseHookID != IntPtr.Zero;
        }

        public static void StopMouseHook()
        {
            if (_mouseHookID == IntPtr.Zero) return;
            _longPressTimer?.Dispose();
            _longPressTimer = null;
            UnhookWindowsHookEx(_mouseHookID);
            _mouseHookID = IntPtr.Zero;
        }

        private static IntPtr SetHook(HookProc proc, out int errorCode)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                IntPtr hook = SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName!), 0);
                errorCode = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                return hook;
            }
        }

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

                    if (_mouseDownInClient)
                    {
                        int dx = Math.Abs(hookStruct.pt.X - _buttonDownPos.X);
                        int dy = Math.Abs(hookStruct.pt.Y - _buttonDownPos.Y);

                        if (_isLongPressed && dx <= DragThreshold && dy <= DragThreshold)
                        {
                            // 300ms 이상 누른 후 움직임 없이 릴리즈 → 롱프레스
                            OnLongPress?.Invoke(null, hookStruct.pt);
                        }
                        else if (!_isLongPressed && (dx > DragThreshold || dy > DragThreshold))
                        {
                            // 드래그 후 릴리즈 → 텍스트 선택
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

            // 현재 커서 위치가 눌린 위치에서 크게 벗어나지 않은 경우만 롱프레스 후보로 표시
            // 실제 이벤트는 마우스 릴리즈 시점에 발생 (드래그 의도 여부 확정 후)
            GetCursorPos(out MousePoint currentPos);
            int dx = Math.Abs(currentPos.X - _buttonDownPos.X);
            int dy = Math.Abs(currentPos.Y - _buttonDownPos.Y);

            if (dx <= DragThreshold && dy <= DragThreshold)
            {
                _isLongPressed = true;
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
