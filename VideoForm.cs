#region Header

//   !!  // TvFox - VideoForm.cs
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
using TvFox.Properties;

#endregion

// ReSharper disable SuspiciousTypeConversion.Global

namespace TvFox
{
    public sealed partial class VideoForm : Form
    {
        public VideoForm(DsDevice videoIn, DsDevice audioIn)
        {
            StartPosition = FormStartPosition.Manual;

            _videoIn = videoIn;
            _audioIn = audioIn;

            Controls.Add(_videoContainer = new Control { BackColor = Color.Red });

            InitializeComponent();

            Location = Settings.Default.WindowPosition;
            Size = Settings.Default.WindowSize;
            WindowState = Settings.Default.WindowState;

            SetupEvents();
            SetupAudioOut();
            SetupFilter();
            SetupDirectShow();

            // Media Format
            DetectAllFormats();
            DetectCurrentFormat();

            _doubleClickCheck = false;

            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            UpdateStyles();

            HandleWindowResize();

            overlayTopLeft.Visible = Settings.Default.ShowFps;
            overlayTopLeft.BringToFront();

            overlayTopRight.Visible = Settings.Default.Mute;
            overlayTopRight.BringToFront();

            overlayBottomCenter.Visible = false;
            overlayBottomCenter.BringToFront();

            VolumeSet(Settings.Default.Mute ? VOLUME_OFF : Settings.Default.Volume);

            _videoContainer.Focus();
        }

        #region Private Members

        private readonly DsDevice _videoIn;
        private readonly DsDevice _audioIn;

        private DsDevice _audioOut;

        private IBaseFilter _videoInFilter;
        private IBaseFilter _videoOutFilter;
        private IBaseFilter _audioInFilter;
        private IBaseFilter _audioOutFilter;

        private IAMStreamConfig _streamConfig;
        private IFilterGraph _filterGraph;
        private IGraphBuilder _graphBuilder;
        private IMediaControl _mediaControl;
        private IVideoWindow _videoWindow;
        private IMediaFilter _mediaFilter;
        private IMediaEventEx _mediaEvent;
        private IBasicAudio _basicAudio;

        private bool _doubleClickCheck;
        private DateTime _doubleClickTimer;
        private DateTime _muteTime = DateTime.MinValue;

        private readonly Control _videoContainer;

        private AMMediaType _currentMediaType;
        private VideoInfoHeader _currentVideoInfo;

        private IPin _outVideoPin;
        private IPin _inVideoPin;
        private IPin _outAudioPin;
        private IPin _inAudioPin;

        private Size _oldSize;
        private Point _oldLocation;
        private FormWindowState _oldState;

        private int _centerTextVisibilityTimer;

        private readonly Dictionary<string, List<Tuple<Size, float, float>>> _supportedFormats = new Dictionary<string, List<Tuple<Size, float, float>>>();

        #endregion

        #region Private Constants

        private const int VOLUME_MAX = 0;
        private const int VOLUME_OFF = -4200;
        private const int VOLUME_MIN = -10000;
        private const int VOLUME_STEP = 42 * 2;

        private const int WM_GRAPH_NOTIFY = 0x0400 + 13;
        private const int CENTER_TEXT_UI_VISIBILITY_TIME = 15;

        #endregion

        #region Public Properties

        public bool ShouldClose { get; set; } = false;

        public IReadOnlyDictionary<string, List<Tuple<Size, float, float>>> SupportedFormats => _supportedFormats;

        public Size SourceSize => new Size(_currentVideoInfo.BmiHeader.Width, _currentVideoInfo.BmiHeader.Height);

        public float SourceFramerate => App.TEN_MILL / SourceFrametime;

        public float SourceFrametime => _currentVideoInfo.AvgTimePerFrame;

        public string SourceFormat => App.MediaStubTypeDictionary[_currentMediaType.subType];

        public string SourceDevice => _videoIn.Name;

        public bool IsFullscreen { get; private set; }

        #endregion

        #region Setup Methods

        private void SetupEvents()
        {
            LocationChanged += (sender, args) =>
            {
                if (Settings.Default.Fullscreen)
                {
                    return;
                }

                Settings.Default.WindowPosition = Location;
                Settings.Default.Save();
            };

            SizeChanged += (sender, args) =>
            {
                if (Settings.Default.Fullscreen)
                {
                    return;
                }

                Settings.Default.WindowSize = Size;
                Settings.Default.WindowState = WindowState;

                Settings.Default.Save();
            };

            FormClosing += (sender, args) =>
            {
                switch (args.CloseReason)
                {
                    case CloseReason.None:
                        break;
                    case CloseReason.WindowsShutDown:
                        break;
                    case CloseReason.MdiFormClosing:
                        break;
                    case CloseReason.UserClosing:
                        if (!ShouldClose)
                        {
                            args.Cancel = true;
                            Hide();
                            return;
                        }
                        break;
                    case CloseReason.TaskManagerClosing:
                        break;
                    case CloseReason.FormOwnerClosing:
                        break;
                    case CloseReason.ApplicationExitCall:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                DirectShowDispose();
            };

            _videoContainer.KeyUp += (sender, args) => HandleKeyUp(args);
            _videoContainer.MouseDown += (sender, args) => HandleMouseClick(args);

            MouseDown += (sender, args) => HandleMouseClick(args);
            MouseWheel += (sender, args) => HandleMouseWheel(args);
            Resize += (sender, args) => HandleWindowResize();
            VisibleChanged += (sender, args) => HandleWindowVisibilityChange();
            Infrared.Remote += InfraredOnRemote;
        }

        private void InfraredOnRemote(CommandButtons commandButtons)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<CommandButtons>(InfraredOnRemote), commandButtons);
                return;
            }

            switch (commandButtons)
            {
                case CommandButtons.Mute:
                {
                    ToggleMute();
                    break;
                }

                case CommandButtons.VolumeUp:
                {
                    VolumeIncrement(VOLUME_STEP);
                    break;
                }

                case CommandButtons.VolumeDown:
                {
                    VolumeDecrement(VOLUME_STEP);
                    break;
                }
            }
        }

        private void SetupAudioOut()
        {
            var aAudioOutputDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.AudioRendererCategory);

            if (aAudioOutputDeviceList.Length == 0)
            {
                throw new ApplicationException("No compatible output audio device found...");
            }

            _audioOut = aAudioOutputDeviceList.First(cDevice => cDevice.Name.Contains("Default DirectSound Device"));
        }

        private void SetupFilter()
        {
            _videoInFilter = AppExtensions.CreateFilter(FilterCategory.VideoInputDevice, _videoIn.Name);
            _audioInFilter = AppExtensions.CreateFilter(FilterCategory.AudioInputDevice, _audioIn.Name);
            _audioOutFilter = AppExtensions.CreateFilter(FilterCategory.AudioRendererCategory, _audioOut.Name);
        }

        public void SetupDirectShow()
        {
            int aHr;

            _filterGraph = (IFilterGraph) new FilterGraph();

            //Create the Graph Builder
            _graphBuilder = _filterGraph as IGraphBuilder;
            _mediaControl = _filterGraph as IMediaControl;
            _videoWindow = _filterGraph as IVideoWindow;
            _mediaFilter = _filterGraph as IMediaFilter;
            _mediaEvent = _filterGraph as IMediaEventEx;
            _basicAudio = _filterGraph as IBasicAudio;

            if (_mediaEvent != null)
            {
                aHr = _mediaEvent.SetNotifyWindow(Handle, WM_GRAPH_NOTIFY, IntPtr.Zero);
                DsError.ThrowExceptionForHR(aHr);
            }

            //Add the Video input device to the graph
            aHr = _filterGraph.AddFilter(_videoInFilter, "Source Video Filter");
            DsError.ThrowExceptionForHR(aHr);

            _videoOutFilter = (IBaseFilter) new VideoRenderer();

            aHr = _filterGraph.AddFilter(_videoOutFilter, "Video Renderer");
            DsError.ThrowExceptionForHR(aHr);

            _outVideoPin = _videoInFilter.GetPin("Video Capture");
            _streamConfig = _outVideoPin as IAMStreamConfig;

            _inVideoPin = _videoOutFilter.GetPin("Input");

            if (_graphBuilder == null)
            {
                throw new ApplicationException("DirectShow GraphBuilder object was null!");
            }

            aHr = _graphBuilder.Connect(_outVideoPin, _inVideoPin);
            DsError.ThrowExceptionForHR(aHr);

            // Add Audio Devices
            aHr = _filterGraph.AddFilter(_audioInFilter, "Source Audio Filter");
            DsError.ThrowExceptionForHR(aHr);

            aHr = _filterGraph.AddFilter(_audioOutFilter, "Audio Renderer");
            DsError.ThrowExceptionForHR(aHr);

            var aSyncReferenceSource = _audioOutFilter as IReferenceClock;

            if (_mediaFilter == null)
            {
                throw new ApplicationException("DirectShow MediaFilter was null!");
            }

            aHr = _mediaFilter.SetSyncSource(aSyncReferenceSource);
            DsError.ThrowExceptionForHR(aHr);

            _outAudioPin = _audioInFilter.GetPin("Audio Capture");
            _inAudioPin = _audioOutFilter.GetPin("Audio Input pin (rendered)");

            aHr = _graphBuilder.Connect(_outAudioPin, _inAudioPin);
            DsError.ThrowExceptionForHR(aHr);

            if (_videoWindow == null)
            {
                throw new ApplicationException("DirectShow IVideoWindow was null!");
            }

            _videoWindow.put_Owner(_videoContainer.Handle);
            _videoWindow.put_MessageDrain(_videoContainer.Handle);
            _videoWindow.put_WindowStyle(WindowStyle.Child | WindowStyle.ClipChildren);
            _videoWindow.SetWindowPosition(0, 0, _videoContainer.Width, _videoContainer.Height);
        }

        #endregion

        #region Format Utils

        private void DetectAllFormats()
        {
            int formatCount;
            int formatSize;

            _streamConfig.GetNumberOfCapabilities(out formatCount, out formatSize);

            var taskMemory = Marshal.AllocCoTaskMem(formatSize);

            for (var formatId = 0; formatId < formatCount; formatId++)
            {
                AMMediaType pmtConfig;

                _streamConfig.GetStreamCaps(formatId, out pmtConfig, taskMemory);

                var sourceFormatInfo = (VideoInfoHeader) Marshal.PtrToStructure(pmtConfig.formatPtr, typeof(VideoInfoHeader));
                var sourceFormatCaps = (VideoStreamConfigCaps) Marshal.PtrToStructure(taskMemory, typeof(VideoStreamConfigCaps));

                var mediaFormatTypeName = App.MediaStubTypeDictionary[pmtConfig.subType];

                if (!_supportedFormats.ContainsKey(mediaFormatTypeName))
                {
                    _supportedFormats.Add(mediaFormatTypeName, new List<Tuple<Size, float, float>>());
                }

                var formatGeneric =
                    new Tuple<Size, float, float>(
                        new Size(sourceFormatInfo.BmiHeader.Width, sourceFormatInfo.BmiHeader.Height),
                        1f * App.TEN_MILL / sourceFormatCaps.MaxFrameInterval, 1f * App.TEN_MILL / sourceFormatCaps.MinFrameInterval);

                if (!_supportedFormats[mediaFormatTypeName].Contains(formatGeneric))
                {
                    _supportedFormats[mediaFormatTypeName].Add(formatGeneric);
                }
            }

            Marshal.FreeCoTaskMem(taskMemory);
        }

        private void DetectCurrentFormat()
        {
            var hr = _streamConfig.GetFormat(out var mediaType);

            DsError.ThrowExceptionForHR(hr);

            _currentMediaType = mediaType;
            _currentVideoInfo = (VideoInfoHeader) Marshal.PtrToStructure(_currentMediaType.formatPtr, typeof(VideoInfoHeader));

            if (Settings.Default.Format == Guid.Empty)
            {
                Settings.Default.Format = _currentMediaType.subType;
                Settings.Default.Save();
            }

            if (Settings.Default.Frametime.Equals(default(float)))
            {
                Settings.Default.Frametime = SourceFrametime;
                Settings.Default.Save();
            }
        }

        public void ChangeFormat(float frametime, Size? videoSize = null, string format = null)
        {
            if (SourceFrametime.Equals(frametime))
            {
                return;
            }

            var wasVisible = Visible;

            Hide();

            var hr = _mediaControl.Stop();
            DsError.ThrowExceptionForHR(hr);

            //hr = _graphBuilder.Disconnect(_outVideoPin);
            //DsError.ThrowExceptionForHR(hr);

            Settings.Default.Frametime = frametime;
            Settings.Default.Save();

            _currentVideoInfo.AvgTimePerFrame = (long) frametime;

            Marshal.StructureToPtr(_currentVideoInfo, _currentMediaType.formatPtr, true);

            hr = _streamConfig.SetFormat(_currentMediaType);
            DsError.ThrowExceptionForHR(hr);

            //hr = _graphBuilder.Connect(_outVideoPin, _inVideoPin);
            //DsError.ThrowExceptionForHR(hr);

            hr = _mediaControl.Run();
            DsError.ThrowExceptionForHR(hr);

            DetectCurrentFormat();

            if (wasVisible)
            {
                Show();
            }
        }

        #endregion

        #region Gets n Sets

        private float GetSourceRatio()
        {
            var sourceSize = SourceSize;

            return 1f * sourceSize.Width / sourceSize.Height;
        }

        private int GetVideoDetails()
        {
            var aQualityProperties = _videoOutFilter as IQualProp;

            var piAvgFrameRate = 0;

            aQualityProperties?.get_AvgFrameRate(out piAvgFrameRate);

            return piAvgFrameRate;
        }

        public void FullscreenSet(bool value)
        {
            Settings.Default.Fullscreen = value;
            Settings.Default.Save();

            if (value)
            {
                _oldSize = Size;
                _oldLocation = Location;
                _oldState = WindowState;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = _oldState;
                FormBorderStyle = FormBorderStyle.Sizable;

                Location = _oldLocation;
                Size = _oldSize;
            }

            IsFullscreen = value;
            App.HideMouseCursor = value;

            HandleWindowResize();
        }

        public void VolumeIncrement(int amount)
        {
            /* Maybe Later?
            if (Settings.Default.Mute)
            {
                ToggleMute();
                return;
            }
            */

            VolumeSet(Settings.Default.Volume += amount);
        }

        public void VolumeDecrement(int amount)
        {
            VolumeSet(Settings.Default.Volume -= amount);
        }

        public void VolumeSet(int volume, bool showStatus = true)
        {
            if (volume > VOLUME_MAX)
            {
                volume = 0;
            }
            else if (volume < VOLUME_OFF && volume != VOLUME_MIN)
            {
                volume = VOLUME_OFF;
            }
            else if (volume < VOLUME_MIN)
            {
                volume = VOLUME_MIN;
            }

            if (Settings.Default.Mute)
            {
                volume = VOLUME_MIN;
                showStatus = false;
            }

            Settings.Default.Volume = volume;
            Settings.Default.Save();

            _basicAudio.put_Volume(volume);

            if (volume == VOLUME_MIN || !showStatus)
            {
                return;
            }

            var absOffVol = Math.Abs(VOLUME_OFF);
            var revVol = volume + absOffVol;
            var percentageVol = .0d;

            if (revVol != 0)
            {
                percentageVol = Math.Floor((float) revVol / absOffVol * 100);
            }

            var subCalc = percentageVol / 10;
            var numOfVolBlocks = subCalc < 5 ? Math.Ceiling(subCalc) : Math.Floor(subCalc);

            SetStatusText($"VOLUME |{new string('█', (int) numOfVolBlocks),-10}| {percentageVol,-3}");
        }

        public void SetStatusText(string text)
        {
            overlayBottomCenter.Text = text;

            var x = Size.Width / 2 - overlayBottomCenter.Width / 2;
            overlayBottomCenter.Location = new Point(x, overlayBottomCenter.Location.Y);

            overlayBottomCenter.Visible = true;

            _centerTextVisibilityTimer = CENTER_TEXT_UI_VISIBILITY_TIME;
        }

        #endregion

        #region Toggles

        public void ToggleFullscreen()
        {
            FullscreenSet(!Settings.Default.Fullscreen);
        }

        public void ToggleMute()
        {
            if ((DateTime.Now - _muteTime).Milliseconds < 250)
            {
                return;
            }

            Settings.Default.Mute = !Settings.Default.Mute;

            if (Settings.Default.Mute)
            {
                Settings.Default.MutedVolume = Settings.Default.Volume;
                VolumeSet(VOLUME_MIN);
                _centerTextVisibilityTimer = 0;
            }
            else
            {
                VolumeSet(Settings.Default.MutedVolume);
                Settings.Default.MutedVolume = VOLUME_MAX;
            }

            Settings.Default.Save();

            overlayTopRight.Visible = Settings.Default.Mute;

            _muteTime = DateTime.Now;
        }

        #endregion

        #region Event Handlers

        private void HandleWindowVisibilityChange()
        {
            if (Visible)
            {
                _mediaControl.Run();
            }
            else
            {
                _mediaControl.Pause();
            }
        }

        public void HandleWindowResize()
        {
            Size sourceSize;
            float sourceRatio;

            try
            {
                sourceSize = SourceSize;
                sourceRatio = GetSourceRatio();
            }
            catch (Exception)
            {
                if (_currentVideoInfo != null)
                {
                    throw;
                }

                Debug.WriteLine("Video Info is null, not cleanly shutdown.");
                Application.Exit();
                return;
            }

            if (Settings.Default.SourceDemensionLock && WindowState != FormWindowState.Maximized && !Settings.Default.Fullscreen)
            {
                ClientSize = sourceSize;
            }
            else
            {
                var minWidth = sourceSize.Width / 2;
                var minHeight = sourceSize.Height / 2;

                if (Width <= minWidth)
                {
                    Width = minWidth;
                }

                if (Height <= minHeight)
                {
                    Height = minHeight;
                }
            }

            var windowRatio = 1f * ClientRectangle.Width / ClientRectangle.Height;

            if (windowRatio < sourceRatio)
            {
                _videoContainer.Width = ClientRectangle.Width;
                _videoContainer.Height = (int) (_videoContainer.Width / sourceRatio);
                _videoContainer.Top = (ClientRectangle.Height - _videoContainer.Height) / 2;
                _videoContainer.Left = 0;
            }
            else
            {
                _videoContainer.Height = ClientRectangle.Height;
                _videoContainer.Width = (int) (_videoContainer.Height * sourceRatio);
                _videoContainer.Top = 0;
                _videoContainer.Left = (ClientRectangle.Width - _videoContainer.Width) / 2;
            }

            _videoWindow.SetWindowPosition(0, 0, _videoContainer.Width, _videoContainer.Height);
        }

        private void HandleKeyUp(KeyEventArgs args)
        {
            switch (args.KeyCode)
            {
                case Keys.Escape:
                {
                    if (IsFullscreen)
                    {
                        ToggleFullscreen();
                    }
                }
                    break;

                case Keys.Enter:
                {
                    if ((args.Modifiers & Keys.Alt) != 0)
                    {
                        ToggleFullscreen();
                    }
                }
                    break;

                case Keys.VolumeMute:
                case Keys.Space:
                {
                    ToggleMute();
                }
                    break;
            }
        }

        private void HandleMouseClick(MouseEventArgs cArgs)
        {
            if (cArgs.Button == MouseButtons.Right)
            {
                if (IsFullscreen)
                {
                    return;
                }

                App.ContextMenu.Show(this, cArgs.Location);
            }
            else if (cArgs.Button == MouseButtons.Left)
            {
                if (!_doubleClickCheck)
                {
                    _doubleClickTimer = DateTime.Now;
                    _doubleClickCheck = true;
                }
                else
                {
                    if (DateTime.Now - _doubleClickTimer < TimeSpan.FromSeconds(1))
                    {
                        ToggleFullscreen();
                    }

                    _doubleClickCheck = false;
                }
            }
            else if (cArgs.Button == MouseButtons.Middle)
            {
                ToggleMute();
            }
        }

        private void HandleMouseWheel(MouseEventArgs args)
        {
            if (args.Delta > 0)
            {
                VolumeIncrement(args.Delta);
            }
            else
            {
                VolumeDecrement(Math.Abs(args.Delta));
            }
        }

        private void HandleTimedKeyboardState()
        {
            if (!_videoContainer.Focused)
            {
                return;
            }

            if (Keyboard.GetState(Keys.Up).IsPressed)
            {
                VolumeIncrement(VOLUME_STEP);
            }
            else if (Keyboard.GetState(Keys.Down).IsPressed)
            {
                VolumeDecrement(VOLUME_STEP);
            }
        }

        private void HandleTimerTick(object sender, EventArgs e)
        {
            HandleTimedKeyboardState();

            if (overlayBottomCenter.Visible)
            {
                _centerTextVisibilityTimer -= 1;

                if (_centerTextVisibilityTimer < 0)
                {
                    _centerTextVisibilityTimer = 0;
                    overlayBottomCenter.Visible = false;
                }
            }

            if (!Settings.Default.ShowFps)
            {
                return;
            }

            var framrate = GetVideoDetails() / 100f;
            overlayTopLeft.Text = $"{framrate:F} fps";
        }

        #endregion

        #region Dispose Methods

        public void DirectShowDispose()
        {
            DsUtils.FreeAMMediaType(_currentMediaType);

            var aHr = _mediaControl.Stop();
            DsError.ThrowExceptionForHR(aHr);

            aHr = _mediaEvent.SetNotifyWindow(IntPtr.Zero, 0, IntPtr.Zero);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _graphBuilder.Disconnect(_inVideoPin);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _graphBuilder.Disconnect(_inAudioPin);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _graphBuilder.Disconnect(_outVideoPin);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _graphBuilder.Disconnect(_outAudioPin);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _filterGraph.RemoveFilter(_videoInFilter);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _filterGraph.RemoveFilter(_audioInFilter);
            DsError.ThrowExceptionForHR(aHr);

            Marshal.ReleaseComObject(_videoInFilter);
            _videoInFilter = null;

            Marshal.ReleaseComObject(_audioInFilter);
            _audioInFilter = null;

            aHr = _filterGraph.RemoveFilter(_videoOutFilter);
            DsError.ThrowExceptionForHR(aHr);

            aHr = _filterGraph.RemoveFilter(_audioOutFilter);
            DsError.ThrowExceptionForHR(aHr);

            Marshal.ReleaseComObject(_videoOutFilter);
            _videoOutFilter = null;

            Marshal.ReleaseComObject(_audioOutFilter);
            _audioOutFilter = null;

            _graphBuilder = null;
            _mediaControl = null;
            _videoWindow = null;
            _mediaFilter = null;
            _mediaEvent = null;

            Marshal.ReleaseComObject(_outAudioPin);
            _outAudioPin = null;

            Marshal.ReleaseComObject(_inAudioPin);
            _inAudioPin = null;

            Marshal.ReleaseComObject(_streamConfig);
            _streamConfig = null;

            Marshal.ReleaseComObject(_outVideoPin);
            _outVideoPin = null;

            Marshal.ReleaseComObject(_inVideoPin);
            _inVideoPin = null;

            Marshal.ReleaseComObject(_filterGraph);
            _filterGraph = null;
        }

        #endregion
    }
}