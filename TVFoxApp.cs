//   !!  // TVFox
// *.-". // MIT License
//  | |  // Copyright 2020 The Fox Council 

using DirectShowLib;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TVFox.Properties;

using static TVFox.Enums;

namespace TVFox
{
    public class TVFoxApp : ApplicationContext
    {
        [DllImport("user32")]
        private static extern int ShowCursor(bool bShow);

        [DllImport("kernel32")]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        private static bool _isMouseHidden;

        public const string DEVICE_DETECTION_STRING_KEY = "Game Capture ";

        public static Dictionary<Guid, string> MediaStubTypeDictionary;

        private readonly Control _syncForm = new Control();
        private readonly NotifyIcon _trayIcon;
        private readonly Timer _timer;

        private AboutForm _aboutForm;
        private VideoForm _videoForm;

        private ToolStripMenuItem _contextMenuSettings;
        private ToolStripMenuItem _contextMenuShowHideWindow;
        private ToolStripMenuItem _contextMenuFullscreenWindow;
        private ToolStripMenuItem _contextMenuMute;
        private ToolStripMenuItem _contextMenuSignalDetection;
        private ToolStripMenuItem _contextMenuVideoInfoData;
        private ToolStripMenuItem _contextMenuDebug;
        private ToolStripMenuItem _contextMenuDebugShowFps;
        private ToolStripMenuItem _contextMenuSettingDimensionLock;
        private ToolStripMenuItem _contextMenuSettingRunOnStartup;
        private ToolStripMenuItem _contextMenuSettingSourceDevice;
        private ToolStripMenuItem _contextMenuSettingSourceFormat;
        private ToolStripMenuItem _contextMenuSettingSourceFramerate;
        private ToolStripMenuItem _contextMenuSettingSourceResolution;

        public TVFoxAppState CurrentState { get; private set; } = TVFoxAppState.FirstStart;

        public VideoFormState CurrentWindowState { get; } = VideoFormState.FirstStart;

        public static DsDevice CurrentInputVideoDevice { get; set; }

        public static DsDevice CurrentInputAudioDevice { get; set; }

        public static IBaseFilter CurrentInputVideoSignalFilter { get; set; }
        
        public static ContextMenuStrip ContextMenuStrip { get; private set; }

        public TVFoxApp()
        {
            MediaStubTypeDictionary = new Dictionary<Guid, string>();

            foreach (var subtype in typeof(MediaSubType).GetFields())
            {
                var value = (Guid)subtype.GetValue(null);

                if (!MediaStubTypeDictionary.ContainsKey(value))
                {
                    MediaStubTypeDictionary.Add(value, subtype.Name);
                }
            }

            MediaStubTypeDictionary.Add(new Guid("{34363258-0000-0010-8000-00aa00389b71}"), "X264");

            CurrentInputVideoSignalFilter = DirectShowHelper.CreateFilter(FilterCategory.VideoInputDevice, CurrentInputVideoDevice.Name);

            SetupContextMenus();

            _syncForm.Show();
            _syncForm.CreateControl();

            _trayIcon = new NotifyIcon { Text = Application.ProductName, Icon = Resources.TvFox, Visible = true, ContextMenuStrip = ContextMenuStrip };
            _trayIcon.DoubleClick += (cSender, cArgs) => ToggleWindow();

            _timer = new Timer { Interval = 50 };
            _timer.Tick += (cSender, cArgs) => CheckSignalState();
            _timer.Start();

            _aboutForm = new AboutForm();

            Application.ApplicationExit += (sender, args) =>
            {
                _trayIcon.Visible = false;
            };
        }

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
        private void ToggleWindow()
        {
            if (_videoForm != null)
            {
                if (_videoForm.Visible)
                {
                    _videoForm.Hide();
                }
                else
                {
                    _videoForm.Show();
                }
            }

            UpdateContextMenu();
        }

        private void SignalProcess()
        {
            var oldLocation = Settings.Default.WindowPosition;

            _videoForm = new VideoForm(CurrentInputVideoDevice, CurrentInputAudioDevice)
            {
                BackColor = Color.Black,
                Icon = Resources.TvFox,
                ShowInTaskbar = true,
                Text = Application.ProductName,
                KeyPreview = true,
            };

            _videoForm.ChangeFormat(Settings.Default.Frametime);

            _videoForm.VisibleChanged += (sender, args) => {

                if (_videoForm.Visible)
                {
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED  | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                }
                else
                {
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                }

                UpdateContextMenu();
            };

            _videoForm.FullscreenChanged += () =>
            {
                UpdateContextMenu();
            };

            _videoForm.MuteChanged += () =>
            {
                UpdateContextMenu();
            };

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

        private void SetupContextMenus()
        {
            ContextMenuStrip = new ContextMenuStrip();
            ContextMenuStrip.Items.Add(_contextMenuShowHideWindow = new ToolStripMenuItem { Text = "Show &Window", Enabled = false });
            ContextMenuStrip.Items.Add("-");
            
            ContextMenuStrip.Items.Add(_contextMenuFullscreenWindow = new ToolStripMenuItem { Text = "&Fullscreen", Enabled = false });
            _contextMenuFullscreenWindow.Click += (sender, args) => _videoForm?.ToggleFullscreen();

            ContextMenuStrip.Items.Add(_contextMenuMute = new ToolStripMenuItem { Text = "&Mute", Enabled = false });
            _contextMenuMute.Click += (sender, args) => _videoForm?.ToggleMute();
            
            ContextMenuStrip.Items.Add("-");
            ContextMenuStrip.Items.Add(_contextMenuSettings = new ToolStripMenuItem { Text = "&Settings" });
            ContextMenuStrip.Items.Add("-");
            ContextMenuStrip.Items.Add(_contextMenuSignalDetection = new ToolStripMenuItem { Text = "No Signal Detected", Enabled = false });
            ContextMenuStrip.Items.Add(_contextMenuVideoInfoData = new ToolStripMenuItem { Text = "N/A", Enabled = false });
            ContextMenuStrip.Items.Add("-");
            ContextMenuStrip.Items.Add("&About", null, (sender, args) => _aboutForm.ShowDialog());
            ContextMenuStrip.Items.Add("E&xit", null, (cSender, cArgs) => Application.Exit());
            ContextMenuStrip.Items[0].Click += (cSender, cArgs) => ToggleWindow();

            ContextMenuStrip.VisibleChanged += (sender, args) =>
            {
                if (_videoForm != null && _videoForm.IsFullscreen)
                {
                    HideMouseCursor = !ContextMenuStrip.Visible;
                }
            };

            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingSourceDevice = new ToolStripMenuItem { Enabled = false });
            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingSourceResolution = new ToolStripMenuItem());
            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingSourceFramerate = new ToolStripMenuItem());
            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingSourceFormat = new ToolStripMenuItem());
            _contextMenuSettings.DropDownItems.Add("-");

            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingDimensionLock = new ToolStripMenuItem { Text = "Source Dimension Lock", Checked = Settings.Default.SourceDemensionLock });
            _contextMenuSettingDimensionLock.Click += (sender, args) =>
            {
                _contextMenuSettingDimensionLock.Checked = Settings.Default.SourceDemensionLock = !Settings.Default.SourceDemensionLock;
                Settings.Default.Save();
                _videoForm?.HandleWindowResize();
            };

            _contextMenuSettings.DropDownItems.Add(_contextMenuSettingRunOnStartup = new ToolStripMenuItem { Text = "Run On Windows Startup", Checked = Settings.Default.RunOnStartup });
            _contextMenuSettingRunOnStartup.Click += (sender, args) =>
            {
                _contextMenuSettingRunOnStartup.Checked = Settings.Default.RunOnStartup = !Settings.Default.RunOnStartup;
                
                Settings.Default.Save();

                RunOnStartupToggle();
            };

            _contextMenuSettings.DropDownItems.Add("-");
            _contextMenuSettings.DropDownItems.Add(_contextMenuDebug = new ToolStripMenuItem { Text = "&Debug" });
            _contextMenuDebug.DropDownItems.Add(_contextMenuDebugShowFps = new ToolStripMenuItem { Text = "Display FPS", Checked = Settings.Default.ShowFps });
            _contextMenuDebugShowFps.Click += (sender, args) =>
            {
                _contextMenuDebugShowFps.Checked = Settings.Default.ShowFps = !Settings.Default.ShowFps;
                Settings.Default.Save();

                _videoForm.overlayTopLeft.Visible = Settings.Default.ShowFps;
            };
        }

        private void UpdateContextMenu()
        {
            var hasSignal = CurrentState == TVFoxAppState.Signal;
            var formVisible = (_videoForm?.Visible).GetValueOrDefault(false);

            _contextMenuShowHideWindow.Text = formVisible ? "Hide Window" : "Show Window";
            _contextMenuShowHideWindow.Enabled = hasSignal;

            _contextMenuFullscreenWindow.Checked = (_videoForm?.IsFullscreen).GetValueOrDefault(false);
            _contextMenuFullscreenWindow.Enabled = hasSignal && formVisible;
            
            _contextMenuMute.Checked = (_videoForm?.IsMuted).GetValueOrDefault(false);
            _contextMenuMute.Enabled = hasSignal && formVisible;

            _contextMenuSettingSourceDevice.Enabled = false;
            _contextMenuSettingSourceDevice.Text = $"Device ({CurrentInputVideoDevice.Name})";

            _contextMenuSettingSourceResolution.Enabled = hasSignal;
            _contextMenuSettingSourceResolution.Text = hasSignal ? $"Resolution ({_videoForm?.SourceSize.Width}x{_videoForm?.SourceSize.Height})" : "Resolution";
            _contextMenuSettingSourceResolution.DropDownItems.Clear();

            _contextMenuSettingSourceFramerate.Enabled = hasSignal;
            _contextMenuSettingSourceFramerate.Text = hasSignal ? $"Framerate ({_videoForm?.SourceFramerate:F} fps)" : "Framerate";
            _contextMenuSettingSourceFramerate.DropDownItems.Clear();

            _contextMenuSettingSourceFormat.Enabled = hasSignal;
            _contextMenuSettingSourceFormat.Text = hasSignal ? $"Format ({_videoForm?.SourceFormat})" : "Format";
            _contextMenuSettingSourceFormat.DropDownItems.Clear();

            _contextMenuDebug.Enabled = hasSignal;

            if (hasSignal && _videoForm != null)
            {
                foreach (var format in _videoForm.SupportedFormats.Keys)
                {
                    _contextMenuSettingSourceFormat.DropDownItems.Add(new ToolStripMenuItem { Text = format, Checked = format == _videoForm.SourceFormat, Enabled = false });
                }

                var currentFormatSupported = _videoForm.SupportedFormats[_videoForm.SourceFormat];

                var foundSizes = new HashSet<Size>();

                foreach (var subFormat in currentFormatSupported)
                {
                    if (foundSizes.Contains(subFormat.Item1))
                    {
                        continue;
                    }

                    _contextMenuSettingSourceResolution.DropDownItems.Add(new ToolStripMenuItem { Text = $"{subFormat.Item1.Width}x{subFormat.Item1.Height}", Checked = subFormat.Item1 == _videoForm.SourceSize });

                    foundSizes.Add(subFormat.Item1);
                }

                if (_contextMenuSettingSourceResolution.DropDownItems.Count == 1)
                {
                    _contextMenuSettingSourceResolution.DropDownItems.Clear();
                    _contextMenuSettingSourceResolution.Enabled = false;
                }

                var index = 0;

                foreach (var frameRate in ValueConstants.FrameRates)
                {
                    var frameRateOption = new ToolStripMenuItem { Text = $"{frameRate:F} fps", Checked = Equals(frameRate, _videoForm.SourceFramerate), Tag = ValueConstants.FrameTimes[index] };
                    frameRateOption.Click += (sender, args) => _videoForm.ChangeFormat((float)((ToolStripMenuItem)sender).Tag);
                    _contextMenuSettingSourceFramerate.DropDownItems.Add(frameRateOption);

                    index++;
                }
            }

            _contextMenuSignalDetection.Text = hasSignal ? "Signal Detected:" : "No Signal Detected";
            
            var videoStateString = _videoForm == null ? "NO SIGNAL" : $"{_videoForm.SourceSize.Width}x{_videoForm.SourceSize.Height} {_videoForm.SourceFramerate:F0}fps";

            _contextMenuVideoInfoData.Text = hasSignal && _videoForm != null ? videoStateString : "N/A";

            if (_videoForm != null)
            {
                _videoForm.Text = $"TVFox - {videoStateString} - https://github.com/FoxCouncil/TVFox";
            }

            RunOnStartupCheck();
        }

        private void CheckSignalState()
        {
            var videoDecoderInterface = CurrentInputVideoSignalFilter as IAMAnalogVideoDecoder;

            var signalDetected = false;

            videoDecoderInterface?.get_HorizontalLocked(out signalDetected);

            if (signalDetected && CurrentState != TVFoxAppState.Signal)
            {
                CurrentState = TVFoxAppState.Signal;

                SignalProcess();
            }
            else if (!signalDetected && CurrentState != TVFoxAppState.NoSignal)
            {
                CurrentState = TVFoxAppState.NoSignal;

                SignalDispose();
            }
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

        private static bool FindHardware()
        {
            var videoDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (videoDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputVideoDevice = videoDeviceList.FirstOrDefault(cDevice => cDevice.Name.StartsWith(DEVICE_DETECTION_STRING_KEY));

            if (CurrentInputVideoDevice == null)
            {
                return false;
            }

            var audioDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);

            if (audioDeviceList.Length == 0)
            {
                return false;
            }

            CurrentInputAudioDevice = audioDeviceList.FirstOrDefault(cDevice => cDevice.Name.StartsWith(DEVICE_DETECTION_STRING_KEY));

            if (CurrentInputAudioDevice == null)
            {
                return false;
            }

            return true;
        }

        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!FindHardware())
            {
                MessageBox.Show("Could not find compatible hardware. Please try again.", "TvFox Error");
                return;
            }

            Application.Run(new TVFoxApp());
        }
    }
}
