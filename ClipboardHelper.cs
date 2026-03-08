using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Orbit
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
        private const ushort VK_C = 0x43;
        private const ushort VK_V = 0x56;
        private const ushort VK_DELETE = 0x2E;

        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private static readonly object _clipboardLock = new object();

        /// <summary>
        /// Simulates a Ctrl+C keystroke to copy currently selected text,
        /// waits briefly, and returns the clipboard string.
        /// 기존 클립보드 내용을 백업 후 복원하므로 사용자의 클립보드를 덮어쓰지 않습니다.
        /// </summary>
        public static string GetSelectedText()
        {
            lock (_clipboardLock)
            {
                // 기존 클립보드 내용 백업
                IDataObject? backup = null;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try { backup = Clipboard.GetDataObject(); } catch { }
                    Clipboard.Clear();
                });

                // Simulate Ctrl + C
                SimulateKeyStroke(VK_LCONTROL, VK_C);

                Thread.Sleep(100);

                string selectedText = string.Empty;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Clipboard.ContainsText())
                        selectedText = Clipboard.GetText();

                    // 기존 클립보드 복원
                    try
                    {
                        if (backup != null)
                            Clipboard.SetDataObject(backup, true);
                    }
                    catch { }
                });

                return selectedText;
            }
        }

        /// <summary>
        /// Replaces the currently selected text with the new string by simulating Ctrl+V.
        /// </summary>
        public static void ReplaceSelectedText(string newText)
        {
            lock (_clipboardLock)
            {
                IDataObject? backup = null;
                Application.Current.Dispatcher.Invoke(() => 
                {
                    try { backup = Clipboard.GetDataObject(); } catch { }
                    Clipboard.SetText(newText);
                });

                Thread.Sleep(50);

                // Simulate Ctrl + V
                SimulateKeyStroke(VK_LCONTROL, VK_V);

                // Wait for the application to process Paste
                Thread.Sleep(150);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (backup != null)
                            Clipboard.SetDataObject(backup, true);
                    }
                    catch { }
                });
            }
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

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].U.ki.wVk = VK_DELETE;
                inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;

                SendInput((uint)inputs.Length, inputs, INPUT.Size);
            }
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
        /// 안전하게 텍스트를 클립보드에 복사합니다.
        /// </summary>
        public static void CopyToClipboard(string text)
        {
            lock (_clipboardLock)
            {
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
            }
        }

        private static void SimulateKeyStroke(ushort modifier, ushort key)
        {
            INPUT[] inputs = new INPUT[4];

            // Press Modifier (Ctrl)
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = modifier;

            // Press Key (C or V)
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki.wVk = key;

            // Release Key (C or V)
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki.wVk = key;
            inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;

            // Release Modifier (Ctrl)
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki.wVk = modifier;
            inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, INPUT.Size);
        }
    }
}
