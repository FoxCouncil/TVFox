//   !!  // TVFox - https://github.com/FoxCouncil/TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using System.Windows.Forms;

namespace TVFox
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
