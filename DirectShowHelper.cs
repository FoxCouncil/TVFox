//   !!  // TVFox - https://github.com/FoxCouncil/TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using DirectShowLib;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace TVFox
{
    public static class DirectShowHelper
    {
        /// <summary>Enumerates all filters of the selected category and returns the IBaseFilter for the filter described in friendlyname</summary>
        /// <param name="category">Category of the filter</param>
        /// <param name="friendlyname">Friendly name of the filter</param>
        /// <returns>IBaseFilter for the device</returns>
        public static IBaseFilter CreateFilter(Guid category, string friendlyname)
        {
            object source = null;
            var iid = typeof(IBaseFilter).GUID;

            foreach (var device in DsDevice.GetDevicesOfCat(category).Where(device => string.Compare(device.Name, friendlyname, StringComparison.Ordinal) == 0))
            {
                device.Mon.BindToObject(null, null, ref iid, out source);

                break;
            }

            return (IBaseFilter) source;
        }

         public static string GetName(this IBaseFilter filter)
        {
            filter.QueryVendorInfo(out var venderInfo);

            return venderInfo;
        }

        public static IPin GetPin(this IBaseFilter filter, string pinname)
        {
            var hr = filter.EnumPins(out var epins);

            DsError.ThrowExceptionForHR(hr);

            var pins = new IPin[1];

            while (epins.Next(1, pins, out int _) == 0)
            {
                pins[0].QueryPinInfo(out var pinfo);

                var found = pinfo.name == pinname;

                DsUtils.FreePinInfo(pinfo);

                if (found)
                {
                    return pins[0];
                }
            }

            return null;
        }

        public static Point Center(this Rectangle rectangle)
        {
            return new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);
        }
    }
}
