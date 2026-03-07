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

        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        /// <summary>
        /// Simulates a Ctrl+C keystroke to copy currently selected text,
        /// waits briefly, and returns the clipboard string.
        /// </summary>
        public static string GetSelectedText()
        {
            // Optional: backup existing clipboard if needed.
            Application.Current.Dispatcher.Invoke(() => Clipboard.Clear());

            // Simulate Ctrl + C
            SimulateKeyStroke(VK_LCONTROL, VK_C);

            // Wait a little bit for clipboard structure to be filled
            Thread.Sleep(100);

            string selectedText = string.Empty;
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (Clipboard.ContainsText())
                {
                    selectedText = Clipboard.GetText();
                }
            });

            return selectedText;
        }

        /// <summary>
        /// Replaces the currently selected text with the new string by simulating Ctrl+V.
        /// </summary>
        public static void ReplaceSelectedText(string newText)
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                Clipboard.SetText(newText);
            });

            Thread.Sleep(50);

            // Simulate Ctrl + V
            SimulateKeyStroke(VK_LCONTROL, VK_V);
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
