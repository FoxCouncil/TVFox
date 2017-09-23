#region Header

//   !!  // TvFox - Extensions.cs
// *.-". // Created: 2017-01-04 [5:20 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 6:55 PM

#endregion

#region Usings

using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DirectShowLib;

#endregion

namespace TvFox
{
    public static class AppExtensions
    {
        /// <summary>
        ///     Enumerates all filters of the selected category and returns the IBaseFilter for the filter described in
        ///     friendlyname
        /// </summary>
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

        public static bool IsVisible(this Form form)
        {
            if (form == null)
            {
                return false;
            }

            return form.Visible;
        }

        public static string GetName(this IBaseFilter filter)
        {
            filter.QueryVendorInfo(out var venderInfo);

            return venderInfo;
        }

        /// <summary></summary>
        /// <param name="filter"></param>
        /// <param name="pinname"></param>
        /// <returns></returns>
        public static IPin GetPin(this IBaseFilter filter, string pinname)
        {
            var hr = filter.EnumPins(out var epins);

            DsError.ThrowExceptionForHR(hr);

            var fetched = Marshal.AllocCoTaskMem(4);
            var pins = new IPin[1];

            while (epins.Next(1, pins, fetched) == 0)
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