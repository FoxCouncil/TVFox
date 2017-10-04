#region Header

//   !!  // TvFox - App.cs
// *.-". // Created: 2017-01-03 [10:17 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 6:58 PM

#endregion

#region Usings

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DirectShowLib;
using Microsoft.Win32;
using TvFox.Properties;

#endregion

// ReSharper disable SuspiciousTypeConversion.Global

namespace TvFox
{
    internal class App : ApplicationContext
    {
        public const float TEN_MILL = 10000000f;

        public static readonly float[] FrameTimes = { 166833f, 200000f, 333667f, 400000f };
        public static readonly float[] FrameRates = { TEN_MILL / FrameTimes[0], TEN_MILL / FrameTimes[1], TEN_MILL / FrameTimes[2], TEN_MILL / FrameTimes[3] };

        public static Dictionary<Guid, string> MediaStubTypeDictionary;

        private readonly Control _syncForm = new Control();
        private readonly NotifyIcon _trayIcon;

        private static bool _isMouseHidden;

        private MenuItem _contextMenuSettings;
        private MenuItem _contextMenuShowHideWindow;
        private MenuItem _contextMenuSignalDetection;
        private MenuItem _contextMenuVideoInfoData;
        private MenuItem _contextMenuDebug;
        private MenuItem _contextMenuDebugShowFps;
        private MenuItem _contextMenuSettingDimensionLock;
        private MenuItem _contextMenuSettingRunOnStartup;
        private MenuItem _contextMenuSettingSourceDevice;
        private MenuItem _contextMenuSettingSourceFormat;
        private MenuItem _contextMenuSettingSourceFramerate;
        private MenuItem _contextMenuSettingSourceResolution;

        private VideoForm _videoForm;

        public App()
        {
            _syncForm.Show();
            _syncForm.CreateControl();

            Infrared.Initialize();

            LoadMediaSubtypeStrings();

            CurrentInputVideoSignalFilter = AppExtensions.CreateFilter(FilterCategory.VideoInputDevice, CurrentInputVideoDevice.Name);

            ThreadExit += (sender, args) =>
            {
                Infrared.Dispose();

                _trayIcon.Visible = false;
            };

            this.

            StateChanged += state =>
            {
                switch (state)
                {
                    case AppState.NoSignal:
                    {
                        SignalDispose();
                        break;
                    }

                    case AppState.FirstStart:
                    {
                        Debug.WriteLine("Welcome to TvFox");
                        break;
                    }

                    case AppState.Signal:
                    {
                        SignalProcess();
                        break;
                    }

                    default:
                    {
                        throw new ArgumentOutOfRangeException(nameof(state), state, null);
                    }
                }
            };

            SetupContextMenu();
            SetupSettingsMenu();

            _trayIcon = new NotifyIcon { Text = Application.ProductName, Icon = Resources.TvFox, Visible = true, ContextMenu = ContextMenu };
            _trayIcon.DoubleClick += (cSender, cArgs) => ToggleWindow();

            var timer = new Timer { Interval = 50 };
            timer.Tick += (cSender, cArgs) => CheckSignalState();
            timer.Start();

            RunOnStartupCheck();
            ReadUserSettings();

            Infrared.Remote += InfraredOnRemote;
        }

        public AppState CurrentState { get; private set; } = AppState.FirstStart;

        public WindowState CurrentWindowState { get; } = WindowState.FirstStart;

        public static DsDevice CurrentInputVideoDevice { get; set; }

        public static DsDevice CurrentInputAudioDevice { get; set; }

        public static IBaseFilter CurrentInputVideoSignalFilter { get; set; }

        public static bool HideMouseCursor
        {
            get => _isMouseHidden;
            set
            {
                if (value)
                {
                    if (_isMouseHidden)
                    {
                        return;
                    }

                    ShowCursor(false);
                    _isMouseHidden = true;
                }
                else
                {
                    if (!_isMouseHidden)
                    {
                        return;
                    }

                    ShowCursor(true);
                    _isMouseHidden = false;
                }
            }
        }

        public static ContextMenu ContextMenu { get; private set; }

        public event Action<AppState> StateChanged;

        private static void ReadUserSettings()
        {
            if (Settings.Default.WindowPosition == Point.Empty)
            {
                Settings.Default.WindowPosition = Screen.PrimaryScreen.WorkingArea.Center();
            }

            if (Settings.Default.WindowSize == Size.Empty)
            {
                Settings.Default.WindowSize = new Size(640, 480);
            }

            Settings.Default.Save();
        }

        private static void RunOnStartupToggle()
        {
            var runOnStartupList = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (Settings.Default.RunOnStartup)
            {
                runOnStartupList?.SetValue(Application.ProductName, Application.ExecutablePath);
            }
            else
            {
                runOnStartupList?.DeleteValue(Application.ProductName);
            }
        }

        public static void LoadMediaSubtypeStrings()
        {
            var registeredSubtypes = typeof(MediaSubType).GetFields();

            var dictionary = new Dictionary<Guid, string>();

            foreach (var subtype in registeredSubtypes)
            {
                var value = (Guid)subtype.GetValue(null);

                if (!dictionary.ContainsKey(value))
                {
                    dictionary.Add(value, subtype.Name);
                }
            }

            dictionary.Add(new Guid("{34363258-0000-0010-8000-00aa00389b71}"), "X264");

            MediaStubTypeDictionary = dictionary;
        }

        private void RunOnStartupCheck()
        {
            var runOnStartupList = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            var value = (string) runOnStartupList?.GetValue(Application.ProductName);

            if (value != null && value == Application.ExecutablePath)
            {
                Settings.Default.RunOnStartup = true;
            }
            else
            {
                Settings.Default.RunOnStartup = false;
            }

            Settings.Default.Save();

            _contextMenuSettingRunOnStartup.Checked = Settings.Default.RunOnStartup;
        }

        private void ToggleWindow()
        {
            if (_videoForm.Visible)
            {
                _videoForm.Hide();
            }
            else
            {
                _videoForm.Show();
            }

            UpdateContextMenu();
        }

        private void SetState(AppState cAppState)
        {
            if (CurrentState == cAppState)
            {
                return;
            }

            CurrentState = cAppState;

            StateChanged?.Invoke(CurrentState);
        }

        #region Signal Handlers

        private void SignalProcess()
        {
            var oldLocation = Settings.Default.WindowPosition;

            _videoForm = new VideoForm(CurrentInputVideoDevice, CurrentInputAudioDevice)
            {
                BackColor = Color.Black,
                Icon = Resources.TvFox,
                ShowInTaskbar = true,
                Text = Application.ProductName,
                KeyPreview = true
            };

            _videoForm.ChangeFormat(Settings.Default.Frametime);

            _videoForm.VisibleChanged += (sender, args) => UpdateContextMenu();

            _videoForm.Show();

            _videoForm.FullscreenSet(Settings.Default.Fullscreen);

            _videoForm.Location = oldLocation;

            UpdateContextMenu();
        }

        private void SignalDispose()
        {
            if (_videoForm != null)
            {
                if (!_videoForm.IsDisposed)
                {
                    _videoForm.ShouldClose = true;
                    _videoForm.Close();
                }

                _videoForm = null;
            }

            UpdateContextMenu();
        }

        private void CheckSignalState()
        {
            var videoDecoderInterface = CurrentInputVideoSignalFilter as IAMAnalogVideoDecoder;

            var signalDetected = false;

            videoDecoderInterface?.get_HorizontalLocked(out signalDetected);

            SetState(signalDetected ? AppState.Signal : AppState.NoSignal);
        }

        #endregion

        #region Context/Settings Menu

        private void SetupContextMenu()
        {
            ContextMenu = new ContextMenu();
            ContextMenu.MenuItems.Add(_contextMenuShowHideWindow = new MenuItem { Text = "Show &Window", Enabled = false });
            ContextMenu.MenuItems.Add("-");
            ContextMenu.MenuItems.Add(_contextMenuSettings = new MenuItem { Text = "&Settings" });
            ContextMenu.MenuItems.Add("-");
            ContextMenu.MenuItems.Add(_contextMenuSignalDetection = new MenuItem { Text = "No Signal Detected", Enabled = false });
            ContextMenu.MenuItems.Add(_contextMenuVideoInfoData = new MenuItem { Text = "N/A", Enabled = false });
            ContextMenu.MenuItems.Add("-");
            ContextMenu.MenuItems.Add("&About", (sender, args) => new AboutBox().Show());
            ContextMenu.MenuItems.Add("E&xit", (cSender, cArgs) => ExitThread());
            ContextMenu.MenuItems[0].Click += (cSender, cArgs) => ToggleWindow();
        }

        private void SetupSettingsMenu()
        {
            _contextMenuSettings.MenuItems.Add(_contextMenuSettingSourceDevice = new MenuItem { Enabled = false });
            _contextMenuSettings.MenuItems.Add(_contextMenuSettingSourceResolution = new MenuItem());
            _contextMenuSettings.MenuItems.Add(_contextMenuSettingSourceFramerate = new MenuItem());
            _contextMenuSettings.MenuItems.Add(_contextMenuSettingSourceFormat = new MenuItem());
            _contextMenuSettings.MenuItems.Add("-");

            _contextMenuSettings.MenuItems.Add(_contextMenuSettingDimensionLock = new MenuItem { Text = "Source Dimension Lock", Checked = Settings.Default.SourceDemensionLock });
            _contextMenuSettingDimensionLock.Click += (sender, args) =>
            {
                _contextMenuSettingDimensionLock.Checked = Settings.Default.SourceDemensionLock = !Settings.Default.SourceDemensionLock;
                Settings.Default.Save();
                _videoForm?.HandleWindowResize();
            };

            _contextMenuSettings.MenuItems.Add(_contextMenuSettingRunOnStartup = new MenuItem { Text = "Run On Windows Startup", Checked = Settings.Default.RunOnStartup });
            _contextMenuSettingRunOnStartup.Click += (sender, args) =>
            {
                _contextMenuSettingRunOnStartup.Checked = Settings.Default.RunOnStartup = !Settings.Default.RunOnStartup;
                Settings.Default.Save();
                RunOnStartupToggle();
            };

            _contextMenuSettings.MenuItems.Add("-");
            _contextMenuSettings.MenuItems.Add(_contextMenuDebug = new MenuItem { Text = "&Debug" });
            _contextMenuDebug.MenuItems.Add(_contextMenuDebugShowFps = new MenuItem { Text = "Display FPS", Checked = Settings.Default.ShowFps });
            _contextMenuDebugShowFps.Click += (sender, args) =>
            {
                _contextMenuDebugShowFps.Checked = Settings.Default.ShowFps = !Settings.Default.ShowFps;
                Settings.Default.Save();

                _videoForm.overlayTopLeft.Visible = Settings.Default.ShowFps;
            };
        }

        private void UpdateContextMenu()
        {
            var hasSignal = CurrentState == AppState.Signal;

            _contextMenuShowHideWindow.Text = _videoForm.IsVisible() ? "Hide Window" : "Show Window";

            _contextMenuSettingSourceDevice.Enabled = false;
            _contextMenuSettingSourceDevice.Text = $"Device ({CurrentInputVideoDevice.Name})";

            _contextMenuSettingSourceResolution.Enabled = hasSignal;
            _contextMenuSettingSourceResolution.Text = hasSignal ? $"Resolution ({_videoForm?.SourceSize.Width}x{_videoForm?.SourceSize.Height})" : "Resolution";
            _contextMenuSettingSourceResolution.MenuItems.Clear();

            _contextMenuSettingSourceFramerate.Enabled = hasSignal;
            _contextMenuSettingSourceFramerate.Text = hasSignal ? $"Framerate ({_videoForm?.SourceFramerate:F} fps)" : "Framerate";
            _contextMenuSettingSourceFramerate.MenuItems.Clear();

            _contextMenuSettingSourceFormat.Enabled = hasSignal;
            _contextMenuSettingSourceFormat.Text = hasSignal ? $"Format ({_videoForm?.SourceFormat})" : "Format";
            _contextMenuSettingSourceFormat.MenuItems.Clear();

            _contextMenuDebug.Enabled = hasSignal;

            if (hasSignal && _videoForm != null)
            {
                foreach (var format in _videoForm.SupportedFormats.Keys)
                {
                    _contextMenuSettingSourceFormat.MenuItems.Add(new MenuItem { Text = format, Checked = format == _videoForm.SourceFormat, Enabled = false });
                }

                var currentFormatSupported = _videoForm.SupportedFormats[_videoForm.SourceFormat];

                var foundSizes = new HashSet<Size>();

                foreach (var subFormat in currentFormatSupported)
                {
                    if (foundSizes.Contains(subFormat.Item1))
                    {
                        continue;
                    }

                    _contextMenuSettingSourceResolution.MenuItems.Add(new MenuItem { Text = $"{subFormat.Item1.Width}x{subFormat.Item1.Height}", Checked = subFormat.Item1 == _videoForm.SourceSize });

                    foundSizes.Add(subFormat.Item1);
                }

                if (_contextMenuSettingSourceResolution.MenuItems.Count == 1)
                {
                    _contextMenuSettingSourceResolution.MenuItems.Clear();
                    _contextMenuSettingSourceResolution.Enabled = false;
                }

                var index = 0;

                foreach (var frameRate in FrameRates)
                {
                    var frameRateOption = new MenuItem { Text = $"{frameRate:F} fps", Checked = Equals(frameRate, _videoForm.SourceFramerate), Tag = FrameTimes[index] };
                    frameRateOption.Click += (sender, args) => _videoForm.ChangeFormat((float)((MenuItem)sender).Tag);
                    _contextMenuSettingSourceFramerate.MenuItems.Add(frameRateOption);

                    index++;
                }
            }

            _contextMenuShowHideWindow.Enabled = hasSignal;

            _contextMenuSignalDetection.Text = hasSignal ? "Signal Detected:" : "No Signal Detected";
            _contextMenuVideoInfoData.Text = hasSignal && _videoForm != null ? $"{_videoForm.SourceSize.Width}x{_videoForm.SourceSize.Height} {_videoForm.SourceFramerate:F} fps" : "N/A";
        }

        #endregion

        #region Infrared Event Handler

        private void InfraredOnRemote(CommandButtons commandButtons)
        {
            // Thread Sync
            if (_syncForm.InvokeRequired)
            {
                _syncForm.Invoke(new Action<CommandButtons>(InfraredOnRemote), commandButtons);

                return;
            }

            if (commandButtons == CommandButtons.Power)
            {
                ToggleWindow();
            }
        }

        #endregion

        #region PInvoke

        [DllImport("user32")]
        private static extern int ShowCursor(bool bShow);

        #endregion

        #region Main Entry Point

        [STAThread]
        private static void Main()
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
            var videoDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (videoDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputVideoDevice = videoDeviceList.First(cDevice => cDevice.Name.Contains("HD60 Pro"));

            if (CurrentInputVideoDevice == null)
            {
                return false;
            }

            var audioDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);

            if (audioDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputAudioDevice = audioDeviceList.First(cDevice => cDevice.Name.Contains("HD60 Pro"));

            if (CurrentInputAudioDevice == null)
            {
                return false;
            }

            return true;
        }

        #endregion
    }
}