//   !!  // TVFox - https://github.com/FoxCouncil/TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System;
using System.Windows.Forms;

namespace TVFox.Windows
{
    public static class Utilities
    {
        /* User32 Helper Methods */
        public static bool IsMouseVisible { get; private set; } = true;

        public static void SetAlwaysOnTop(IntPtr? hWnd, bool alwaysOnTop)
        {
            if (!hWnd.HasValue)
            {
                return;
            }

            User32.SetWindowPos(hWnd.Value, alwaysOnTop ? User32.HWND_TOPMOST : User32.HWND_NOTOPMOST, 0, 0, 0, 0, User32.TOPMOST_FLAGS);
        }

        public static void SetMouseVisibility(bool mouseVisibility)
        {
            if (mouseVisibility && IsMouseVisible || !mouseVisibility && !IsMouseVisible)
            {
                return;
            }

            _ = User32.ShowCursor(mouseVisibility);
            IsMouseVisible = mouseVisibility;
        }

        public static short GetKeyState(int key)
        {
            return User32.GetKeyState(key);
        }

        public static bool IsWindowAlwaysOnTop(IntPtr? hWnd)
        {
            if (!hWnd.HasValue)
            {
                return false;
            }

            var flags = GetWindowLongPtr(hWnd.Value, (int)User32.GWL.GWL_EXSTYLE);
            return ((uint)flags & (uint)User32.WindowStyles.WS_EX_TOPMOST) != 0;
        }

        // This static method is required because Win32 does not support
        // GetWindowLongPtr directly
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
            {
                return User32.GetWindowLongPtr64(hWnd, (User32.GWL)nIndex);
            }
            else
            {
                return User32.GetWindowLongPtr32(hWnd, (User32.GWL)nIndex);
            }
        }

        /* Kernel32 Helper Methods */
        internal static EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags)
        {
            return Kernel32.SetThreadExecutionState(esFlags);
        }
    }
}
