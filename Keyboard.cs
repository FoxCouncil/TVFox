using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TvFox
{
    public static class Keyboard
    {
        [DllImport("user32")]
        private static extern short GetKeyState(int vKey);

        public static KeyStateInfo GetState(Keys key)
        {
            var keyState = GetKeyState((int)key);
            var bits = BitConverter.GetBytes(keyState);
            bool toggled = bits[0] > 0, pressed = bits[1] > 0;

            return new KeyStateInfo(key, pressed, toggled);
        }
    }
}
