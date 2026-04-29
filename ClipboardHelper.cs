using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Orbital
{
    public static class ClipboardHelper
    {
        // Define Input struct and constants for SendInput
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static readonly int Size = Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_LCONTROL = 0xA2;
        private const ushort VK_A = 0x41;
        private const ushort VK_C = 0x43;
        private const ushort VK_V = 0x56;
        private const ushort VK_DELETE = 0x2E;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_SPACE  = 0x20;
        private const ushort VK_BACK   = 0x08;
        private const ushort VK_ESCAPE = 0x1B;
        private const ushort VK_TAB    = 0x09;
        private const uint WM_INPUT = 0x00FF; // Just for reference
        
        /// <summary>
        /// Magic number set in dwExtraInfo for all keystrokes injected by Orbital.
        /// This allows the low-level hook to ignore our own injections while still
        /// processing those from virtual keyboards like OSK.
        /// </summary>
        public static readonly IntPtr ORBITAL_EXTRA_INFO = (IntPtr)0x4F524254; // 'ORBT'

        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private static readonly object _clipboardLock = new object();

        /// <summary>
        /// Gets the currently selected text.
        /// UI Automation을 먼저 시도하여 클립보드 경합을 피하고,
        /// 실패 시 기존 클립보드 방식(Ctrl+C)으로 폴백합니다.
        /// </summary>
        public static string GetSelectedText()
        {
            // UI Automation으로 먼저 시도 (클립보드 경합 없음)
            string text = GetSelectedTextViaUIA();
            if (!string.IsNullOrEmpty(text))
                return text;

            // 클립보드 방식으로 폴백
            return GetSelectedTextViaClipboard();
        }

        /// <summary>
        /// UI Automation TextPattern을 사용하여 선택된 텍스트를 직접 추출합니다.
        /// 클립보드를 사용하지 않으므로 가상 키보드의 폴링과 충돌하지 않습니다.
        /// </summary>
        private static string GetSelectedTextViaUIA()
        {
            try
            {
                // 현재 포커스된 요소 가져오기 (선택된 텍스트가 있는 창)
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null)
                    return string.Empty;

                // TextPattern 가져오기 시도
                TextPattern? textPattern = null;

                // 자신에서 패턴 시도
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj) &&
                    patternObj is TextPattern tp)
                {
                    textPattern = tp;
                }

                // 부모 요소에서 시도 (일부 앱은 TextPattern이 부모에 있음)
                if (textPattern == null)
                {
                    AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(element);
                    if (parent != null &&
                        parent.TryGetCurrentPattern(TextPattern.Pattern, out patternObj) &&
                        patternObj is TextPattern tp2)
                    {
                        textPattern = tp2;
                        element = parent;
                    }
                }

                if (textPattern == null)
                    return string.Empty;

                // 선택된 텍스트 범위 가져오기
                TextPatternRange[] selection = textPattern.GetSelection();
                if (selection == null || selection.Length == 0)
                    return string.Empty;

                // 선택이 빈 상태(커서만 위치)인지 확인
                TextPatternRange range = selection[0];
                if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                    return string.Empty; // collapsed selection (no text selected)

                // 첫 번째 선택 범위의 텍스트 추출
                string selectedText = range.GetText(-1);
                return selectedText ?? string.Empty;
            }
            catch
            {
                // UIA 접근 실패 시 클립보드 방식으로 폴백하기 위해 빈 문자열 반환
                return string.Empty;
            }
        }

        /// <summary>
        /// Simulates a Ctrl+C keystroke to copy currently selected text,
        /// waits briefly, and returns the clipboard string.
        /// 기존 클립보드 내용을 백업 후 복원하므로 사용자의 클립보드를 덮어쓰지 않습니다.
        /// </summary>
        private static string GetSelectedTextViaClipboard()
        {
            lock (_clipboardLock)
            {
                // 1) Backup + Clear in one Dispatcher call
                IDataObject? backup = Application.Current.Dispatcher.Invoke(() =>
                {
                    IDataObject? bk = RetryFunc(() => Clipboard.GetDataObject(), null);
                    RetryAction(() => Clipboard.Clear());
                    return bk;
                });

                // Simulate Ctrl + C
                SimulateKeyStroke(VK_LCONTROL, VK_C);

                // Poll for clipboard text instead of a fixed sleep: return as soon as
                // the target app has copied, up to 200 ms.
                const int maxWaitMs = 200;
                const int pollIntervalMs = 10;
                int elapsed = 0;
                while (elapsed < maxWaitMs)
                {
                    Thread.Sleep(pollIntervalMs);
                    elapsed += pollIntervalMs;
                    bool hasText = Application.Current.Dispatcher.Invoke(() => RetryFunc(() => Clipboard.ContainsText(), false));
                    if (hasText) break;
                }

                // 2) Read + Restore in one Dispatcher call
                string selectedText = Application.Current.Dispatcher.Invoke(() =>
                {
                    string text = string.Empty;
                    if (RetryFunc(() => Clipboard.ContainsText(), false))
                        text = RetryFunc(() => Clipboard.GetText(), string.Empty);
                    if (backup != null)
                        RetryAction(() => Clipboard.SetDataObject(backup, true));
                    return text;
                });

                return selectedText;
            }
        }

        /// <summary>
        /// Replaces selected text using UI Automation TextPattern, bypassing the clipboard.
        /// Uses Delete + clipboard paste (with retry) as UIA Replace/InsertText may not be available.
        /// Returns true if successful, false otherwise.
        /// </summary>
        private static bool ReplaceSelectedTextViaUIA(string newText)
        {
            try
            {
                AutomationElement? element = AutomationElement.FocusedElement;
                if (element == null)
                    return false;

                TextPattern? textPattern = null;

                if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj) && patternObj is TextPattern tp)
                {
                    textPattern = tp;
                }

                if (textPattern == null)
                {
                    AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(element);
                    if (parent != null && parent.TryGetCurrentPattern(TextPattern.Pattern, out patternObj) && patternObj is TextPattern tp2)
                    {
                        textPattern = tp2;
                        element = parent;
                    }
                }

                if (textPattern == null)
                    return false;

                TextPatternRange[] selection = textPattern.GetSelection();
                if (selection == null || selection.Length == 0)
                    return false;

                TextPatternRange selectedRange = selection[0];
                if (selectedRange.CompareEndpoints(TextPatternRangeEndpoint.Start, selectedRange, TextPatternRangeEndpoint.End) == 0)
                    return false;

                // Delete selected text via UIA
                // Use dynamic to call Replace at runtime (bypasses compile-time method check)
                dynamic dynRange = selectedRange;
                dynRange.Replace(newText, false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Replaces the currently selected text with the new string by simulating Ctrl+V.
        /// Falls back to this method when UI Automation is not available.
        /// </summary>
        private static void ReplaceSelectedTextViaClipboard(string newText)
        {
            lock (_clipboardLock)
            {
                IDataObject? backup = null;
                Application.Current.Dispatcher.Invoke(() => 
                {
                    backup = RetryFunc(() => Clipboard.GetDataObject(), null);
                    RetryAction(() => Clipboard.SetText(newText));
                });

                Thread.Sleep(50);

                // Simulate Ctrl + V
                SimulateKeyStroke(VK_LCONTROL, VK_V);

                // Wait for the application to process Paste
                Thread.Sleep(150);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (backup != null)
                        RetryAction(() => Clipboard.SetDataObject(backup, true));
                });
            }
        }

        /// <summary>
        /// Replaces the currently selected text with the new string.
        /// Tries UI Automation first to avoid clipboard contention, falls back to Ctrl+V method.
        /// </summary>
        public static void ReplaceSelectedText(string newText)
        {
            if (ReplaceSelectedTextViaUIA(newText))
                return;
            ReplaceSelectedTextViaClipboard(newText);
        }

        /// <summary>
        /// 선택된 텍스트를 Delete 키로 삭제합니다. (잘라내기 액션용)
        /// </summary>
        public static void DeleteSelectedText()
        {
            lock (_clipboardLock)
            {
                Thread.Sleep(50);

                INPUT[] inputs = new INPUT[2];

                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].U.ki.wVk = VK_DELETE;
                inputs[0].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].U.ki.wVk = VK_DELETE;
                inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
                inputs[1].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

                SendInput((uint)inputs.Length, inputs, INPUT.Size);
            }
        }

        /// <summary>
        /// Supported key names for SimulateKey: Delete, Backspace, Enter, Space, Escape, Tab.
        /// Returns false when the key name is unrecognised.
        /// </summary>
        public static bool SimulateKey(string keyName)
        {
            ushort vk = keyName.Trim().ToLowerInvariant() switch
            {
                "delete"    => VK_DELETE,
                "backspace" => VK_BACK,
                "enter"     => VK_RETURN,
                "return"    => VK_RETURN,
                "space"     => VK_SPACE,
                "escape"    => VK_ESCAPE,
                "esc"       => VK_ESCAPE,
                "tab"       => VK_TAB,
                _           => 0
            };
            if (vk == 0) return false;

            lock (_clipboardLock)
            {
                Thread.Sleep(50);
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].U.ki.wVk = vk;
                inputs[0].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].U.ki.wVk = vk;
                inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
                inputs[1].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

                SendInput((uint)inputs.Length, inputs, INPUT.Size);
            }
            return true;
        }

        /// <summary>
        /// 현재 클립보드 내용을 Ctrl+V로 붙여넣습니다. (붙여넣기 액션용)
        /// </summary>
        public static void SimulatePaste()
        {
            lock (_clipboardLock)
            {
                Thread.Sleep(50);
                SimulateKeyStroke(VK_LCONTROL, VK_V);
            }
        }

        /// <summary>
        /// Simulates Ctrl+A to select all text in the currently focused control.
        /// </summary>
        public static void SimulateSelectAll()
        {
            lock (_clipboardLock)
            {
                Thread.Sleep(50);
                SimulateKeyStroke(VK_LCONTROL, VK_A);
            }
        }

        /// <summary>
        /// 안전하게 텍스트를 클립보드에 복사합니다.
        /// </summary>
        public static void CopyToClipboard(string text)
        {
            lock (_clipboardLock)
            {
                Application.Current.Dispatcher.Invoke(() => RetryAction(() => Clipboard.SetText(text)));
            }
        }

        private static void RetryAction(Action action, int maxRetries = 10)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try { action(); return; }
                catch (System.Runtime.InteropServices.ExternalException) when (i < maxRetries)
                {
                    int delay = Math.Min(10 * (1 << i), 500);
                    Thread.Sleep(delay);
                }
            }
        }

        private static T RetryFunc<T>(Func<T> func, T fallback, int maxRetries = 10)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try { return func(); }
                catch (System.Runtime.InteropServices.ExternalException) when (i < maxRetries)
                {
                    int delay = Math.Min(10 * (1 << i), 500);
                    Thread.Sleep(delay);
                }
            }
            return fallback;
        }

        private static void SimulateKeyStroke(ushort modifier, ushort key)
        {
            INPUT[] inputs = new INPUT[4];

            // Press Modifier (Ctrl)
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = modifier;
            inputs[0].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

            // Press Key (C or V)
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = key;
            inputs[1].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

            // Release Key (C or V)
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = key;
            inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[2].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

            // Release Modifier (Ctrl)
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = modifier;
            inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[3].U.ki.dwExtraInfo = ORBITAL_EXTRA_INFO;

            SendInput((uint)inputs.Length, inputs, INPUT.Size);
        }
    }
}
