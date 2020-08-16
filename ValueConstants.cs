//   !!  // TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System;
using System.Collections.Generic;
using System.Text;

namespace TVFox
{
    public static class ValueConstants
    {
        public const float TEN_MILL = 10000000f;

        public static readonly float[] FrameTimes = { 166833f, 200000f, 333667f, 400000f };
        public static readonly float[] FrameRates = { TEN_MILL / FrameTimes[0], TEN_MILL / FrameTimes[1], TEN_MILL / FrameTimes[2], TEN_MILL / FrameTimes[3] };
    }
}
