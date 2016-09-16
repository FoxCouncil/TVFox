using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DirectShowLib;
using TvFox.Properties;

// ReSharper disable SuspiciousTypeConversion.Global

namespace TvFox
{
    internal class App : ApplicationContext
    {
        public event Action<AppState> StateChanged;
        public event Action<WindowState> WindowStateChanged;

        public AppState CurrentState
        {
            get;
            private set;
        } = AppState.FirstStart;

        public WindowState CurrentWindowState
        {
            get;
            private set;
        } = WindowState.FirstStart;

        private readonly Timer m_timer;
        private readonly NotifyIcon m_trayIcon;
        private readonly ContextMenu m_contextMenu;
        private VideoForm m_videoForm;

        public App()
        {
            CurrentInputVideoSignalFilter = CreateFilter(FilterCategory.VideoInputDevice, CurrentInputVideoDevice.Name);

            ThreadExit += (c_sender, c_args) =>
            {
                m_trayIcon.Visible = false;
            };

            StateChanged += c_state =>
            {
                switch (c_state)
                {
                    case AppState.NoSignal:
                        SignalDispose();
                        break;
                    case AppState.FirstStart:
                        Debug.WriteLine("Welcome to TvFox");
                        break;
                    case AppState.Signal:
                        SignalProcess();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(c_state), c_state, null);
                }
            };

            m_contextMenu = new ContextMenu();
            m_contextMenu.MenuItems.Add(new MenuItem { Text = "Show Window", Enabled = false });
            m_contextMenu.MenuItems.Add("-");
            m_contextMenu.MenuItems.Add(new MenuItem { Text = "No Signal Detected", Enabled = false });
            m_contextMenu.MenuItems.Add("-");
            m_contextMenu.MenuItems.Add(new MenuItem { Text = "Video Info:", Enabled = false });
            m_contextMenu.MenuItems.Add(new MenuItem { Text = "N/A", Enabled = false });
            m_contextMenu.MenuItems.Add("-");
            m_contextMenu.MenuItems.Add("E&xit", (c_sender, c_args) => ExitThread());

            m_contextMenu.MenuItems[0].Click += (c_sender, c_args) => ToggleWindow();

            m_trayIcon = new NotifyIcon { Text = "TvFox", Icon = Resources.TvFox, Visible = true, ContextMenu = m_contextMenu };
            m_trayIcon.DoubleClick += (c_sender, c_args) => ToggleWindow();

            m_timer = new Timer { Interval = 50 };
            m_timer.Tick += (c_sender, c_args) => CheckSignalState();
            m_timer.Start();
        }

        private void SignalProcess()
        {
            DetectVideoFormat();

            m_videoForm = new VideoForm(CurrentInputVideoDevice, CurrentInputAudioDevice)
            {
                Icon = Resources.TvFox,
                ClientSize = SourceVideoSize,
                StartPosition = FormStartPosition.CenterScreen
            };

            m_videoForm.Show();

            UpdateContextMenu();
        }

        private void SignalDispose()
        {
            if (m_videoForm != null)
            {
                if (!m_videoForm.IsDisposed)
                {
                    m_videoForm.ShouldClose = true;
                    m_videoForm.Close();
                }

                m_videoForm = null;
            }

            UpdateContextMenu();
        }

        private void UpdateContextMenu()
        {
            if (CurrentState == AppState.Signal)
            {
                m_contextMenu.MenuItems[0].Text = m_videoForm.Visible ? "Hide Window" : "Show Window";
                m_contextMenu.MenuItems[0].Enabled = true;
                m_contextMenu.MenuItems[2].Text = "Signal Detected";
                m_contextMenu.MenuItems[5].Text = $"{SourceVideoSize.Width}x{SourceVideoSize.Height} {SourceFrameRate}fps";
            }
            else if (CurrentState == AppState.NoSignal)
            {
                m_contextMenu.MenuItems[0].Enabled = false;
                m_contextMenu.MenuItems[2].Text = "No Signal Detected";
                m_contextMenu.MenuItems[5].Text = "N/A";
            }
        }

        private void CheckSignalState()
        {
            var a_videoDecoderInterface = CurrentInputVideoSignalFilter as IAMAnalogVideoDecoder;

            var a_signalDetected = false;

            if (a_videoDecoderInterface != null)
            {
                a_videoDecoderInterface.get_HorizontalLocked(out a_signalDetected);
            }

            SetState(a_signalDetected ? AppState.Signal : AppState.NoSignal);
        }

        internal static float GetSourceRatio()
        {
            return 1f * SourceVideoSize.Width / SourceVideoSize.Height;
        }

        private void ToggleWindow()
        {
            if (m_videoForm.Visible)
            {
                m_videoForm.Hide();
            }
            else
            {
                m_videoForm.Show();
            }

            UpdateContextMenu();
        }

        private static void DetectVideoFormat()
        {
            var a_videoSourceFilter = CreateFilter(FilterCategory.VideoInputDevice, CurrentInputVideoDevice.Name);
            var a_outputPin = GetPin(a_videoSourceFilter, "Video Capture");

            var a_streamConfig = a_outputPin as IAMStreamConfig;
            if (a_streamConfig == null)
            {

                return;
            }

            AMMediaType a_mediaType;

            var a_hr = a_streamConfig.GetFormat(out a_mediaType);
            DsError.ThrowExceptionForHR(a_hr);

            var a_videoInfoHeader = (VideoInfoHeader)Marshal.PtrToStructure(a_mediaType.formatPtr, typeof(VideoInfoHeader));

            SourceVideoSize = new Size(a_videoInfoHeader.BmiHeader.Width, a_videoInfoHeader.BmiHeader.Height);
            SourceFrameRate = 1f * 10000000 / a_videoInfoHeader.AvgTimePerFrame;

            DsUtils.FreeAMMediaType(a_mediaType);

            Marshal.ReleaseComObject(a_outputPin);
            Marshal.ReleaseComObject(a_videoSourceFilter);
        }

        private void SetState(AppState c_appState)
        {
            if (CurrentState == c_appState)
            {
                return;
            }

            CurrentState = c_appState;

            StateChanged?.Invoke(CurrentState);
        }

        private void SetWindowState(WindowState c_windowState)
        {
            if (CurrentWindowState == c_windowState)
            {
                return;
            }

            CurrentWindowState = c_windowState;

            WindowStateChanged?.Invoke(CurrentWindowState);
        }

        public static DsDevice CurrentInputVideoDevice
        {
            get;
            set;
        }

        public static DsDevice CurrentInputAudioDevice
        {
            get;
            set;
        }

        public static IBaseFilter CurrentInputVideoSignalFilter
        {
            get;
            set;
        }

        public static Size SourceVideoSize
        {
            get;
            set;
        }

        public static float SourceFrameRate
        {
            get;
            set;
        }

        [STAThread]
        private static void Main(string[] c_args)
        {
            Application.SetCompatibleTextRenderingDefault(true);
            Application.EnableVisualStyles();

            if (!FindHardware())
            {
                MessageBox.Show("Could not find compatible hardware. Please try again.", "TvFox Error");
                return;
            }

            Application.Run(new App());
        }

        private static bool FindHardware()
        {
            var a_videoDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (a_videoDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputVideoDevice = a_videoDeviceList.First(c_device => c_device.Name.Contains("HD60 Pro"));

            if (CurrentInputVideoDevice == null)
            {
                return false;
            }

            var a_audioDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);

            if (a_audioDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputAudioDevice = a_audioDeviceList.First(c_device => c_device.Name.Contains("HD60 Pro"));

            if (CurrentInputAudioDevice == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Enumerates all filters of the selected category and returns the IBaseFilter for the 
        /// filter described in friendlyname
        /// </summary>
        /// <param name="c_category">Category of the filter</param>
        /// <param name="c_friendlyname">Friendly name of the filter</param>
        /// <returns>IBaseFilter for the device</returns>
        public static IBaseFilter CreateFilter(Guid c_category, string c_friendlyname)
        {
            object a_source = null;
            var a_iid = typeof(IBaseFilter).GUID;

            foreach (var a_device in DsDevice.GetDevicesOfCat(c_category).Where(c_device => string.Compare(c_device.Name, c_friendlyname, StringComparison.Ordinal) == 0))
            {
                a_device.Mon.BindToObject(null, null, ref a_iid, out a_source);
                break;
            }

            return (IBaseFilter)a_source;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="c_filter"></param>
        /// <param name="c_pinname"></param>
        /// <returns></returns>
        public static IPin GetPin(IBaseFilter c_filter, string c_pinname)
        {
            IEnumPins a_epins;
            var a_hr = c_filter.EnumPins(out a_epins);
            DsError.ThrowExceptionForHR(a_hr);
            var a_fetched = Marshal.AllocCoTaskMem(4);
            var a_pins = new IPin[1];

            while (a_epins.Next(1, a_pins, a_fetched) == 0)
            {
                PinInfo a_pinfo;
                a_pins[0].QueryPinInfo(out a_pinfo);
                var a_found = (a_pinfo.name == c_pinname);
                DsUtils.FreePinInfo(a_pinfo);

                if (a_found)
                {
                    return a_pins[0];
                }
            }

            return null;
        }
    }
}