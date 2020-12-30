//   !!  // TVFox - https://github.com/FoxCouncil/TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System;
using System.Runtime.InteropServices;

namespace TVFox.Windows
{
    internal static class Kernel32
    {
        private const string __MODULE_NAME = "kernel32.dll";

        [DllImport(__MODULE_NAME)]
        internal static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    }

    [Flags]
    internal enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        // Legacy flag, should not be used.
        // ES_USER_PRESENT   = 0x00000004,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
    }
}
