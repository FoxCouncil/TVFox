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
    }
}
