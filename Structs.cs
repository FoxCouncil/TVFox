using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TvFox
{
    public struct KeyStateInfo
    {
        public static KeyStateInfo Default => new KeyStateInfo(Keys.None, false, false);

        public KeyStateInfo(Keys key, bool ispressed, bool istoggled)
        {
            Key = key;
            IsPressed = ispressed;
            IsToggled = istoggled;
        }

        public Keys Key { get; }

        public bool IsPressed { get; }

        public bool IsToggled { get; }
    }
}
