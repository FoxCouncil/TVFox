#region Header

//   !!  // TvFox - Keyboard.cs
// *.-". // Created: 2017-08-28 [8:13 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 6:54 PM

#endregion

#region Usings

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

#endregion

namespace TvFox
{
    public static class Keyboard
    {
        [DllImport("user32")]
        private static extern short GetKeyState(int vKey);

        public static KeyStateInfo GetState(Keys key)
        {
            var keyState = GetKeyState((int) key);
            var bits = BitConverter.GetBytes(keyState);
            bool toggled = bits[0] > 0, pressed = bits[1] > 0;

            return new KeyStateInfo(key, pressed, toggled);
        }
    }
}