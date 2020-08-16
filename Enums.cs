//   !!  // TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System;
using System.Collections.Generic;
using System.Text;

namespace TVFox
{
    public static class Enums
    {
        public enum TVFoxAppState
        {
            FirstStart,
            NoSignal,
            Signal
        }

        public enum VideoFormState
        {
            FirstStart,
            Hidden,
            Shown,
            Fullscreen
        }

        public enum CommandButtons
        {
            None = 0,
            VolumeUp = 18,
            VolumeDown = 19,
            Mute = 20,
            Power = 21
        }

        [FlagsAttribute]
        public enum EXECUTION_STATE : uint
        {
            ES_SYSTEM_REQUIRED = 0x00000001,
            ES_DISPLAY_REQUIRED = 0x00000002,
            // Legacy flag, should not be used.
            // ES_USER_PRESENT   = 0x00000004,
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
        }
    }
}
