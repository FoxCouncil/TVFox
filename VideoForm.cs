using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;
using System.Windows.Forms;
using DirectShowLib;
using TvFox.Properties;

// ReSharper disable SuspiciousTypeConversion.Global

namespace TvFox
{
    public sealed partial class VideoForm : Form
    {
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

        private Control _videoContainer;

        private AMMediaType _currentMediaType;
        private VideoInfoHeader _currentVideoInfo;

        private IPin _outVideoPin;
        private IPin _inVideoPin;
        private IPin _outAudioPin;
        private IPin _inAudioPin;

        private Size _oldSize;
        private Point _oldLocation;
        private FormWindowState _oldState;

        private int _centerTextVisibilityTimer = 0;

        private Dictionary<string, List<Tuple<Size, float, float>>> _supportedFormats = new Dictionary<string, List<Tuple<Size, float, float>>>();

        #endregion

        #region Private Constants

        private const float KTargetRatio3X2 = 3f / 2f;
        private const float KTargetRatio16X9 = 16f / 9f;
        private const float KTargetRatio4X3 = 4f / 3f;
        private const float KTargetRatio16X10 = 16f / 10f;

        private const int VolumeMax = 0;
        private const int VolumeOff = -4200;
        private const int VolumeMin = -10000;
        private const int VolumeStep = 42 * 2;

        private const int WmGraphNotify = 0x0400 + 13;
        private const int CenterTextUiVisibilityTime = 15;

        #endregion

        #region Public Properties

        public bool ShouldClose { get; set; } = false;

        public IReadOnlyDictionary<string, List<Tuple<Size, float, float>>> SupportedFormats => _supportedFormats;

        public Size SourceSize => new Size(_currentVideoInfo.BmiHeader.Width, _currentVideoInfo.BmiHeader.Height);

        public float SourceFramerate => App.TenMill / SourceFrametime;

        public float SourceFrametime => _currentVideoInfo.AvgTimePerFrame;

        public string SourceFormat => App.MediaStubTypeDictionary[_currentMediaType.subType];

        public string SourceDevice => _videoIn.Name;

        public bool IsFullscreen { get; private set; }

        #endregion

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

            VolumeSet(Settings.Default.Mute ? VolumeOff : Settings.Default.Volume);
        }

        #region Setup Methods

        private void SetupEvents()
        {
            LocationChanged += (sender, args) =>
            {
                if (Settings.Default.Fullscreen) return;

                Settings.Default.WindowPosition = Location;
                Settings.Default.Save();
            };

            SizeChanged += (sender, args) =>
            {
                if (Settings.Default.Fullscreen) return;

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

            _filterGraph = (IFilterGraph)new FilterGraph();

            //Create the Graph Builder
            _graphBuilder = _filterGraph as IGraphBuilder;
            _mediaControl = _filterGraph as IMediaControl;
            _videoWindow = _filterGraph as IVideoWindow;
            _mediaFilter = _filterGraph as IMediaFilter;
            _mediaEvent = _filterGraph as IMediaEventEx;
            _basicAudio = _filterGraph as IBasicAudio;

            if (_mediaEvent != null)
            {
                aHr = _mediaEvent.SetNotifyWindow(Handle, WmGraphNotify, IntPtr.Zero);
                DsError.ThrowExceptionForHR(aHr);
            }

            //Add the Video input device to the graph
            aHr = _filterGraph.AddFilter(_videoInFilter, "Source Video Filter");
            DsError.ThrowExceptionForHR(aHr);

            _videoOutFilter = (IBaseFilter)new VideoRenderer();

            aHr = _filterGraph.AddFilter(_videoOutFilter, "Video Renderer");
            DsError.ThrowExceptionForHR(aHr);

            _outVideoPin = _videoInFilter.GetPin("Video Capture");
            _streamConfig = _outVideoPin as IAMStreamConfig;

            _inVideoPin = _videoOutFilter.GetPin("Input");

            aHr = _graphBuilder.Connect(_outVideoPin, _inVideoPin);
            DsError.ThrowExceptionForHR(aHr);

            // Add Audio Devices
            aHr = _filterGraph.AddFilter(_audioInFilter, "Source Audio Filter");
            DsError.ThrowExceptionForHR(aHr);

            aHr = _filterGraph.AddFilter(_audioOutFilter, "Audio Renderer");
            DsError.ThrowExceptionForHR(aHr);

            var aSyncReferenceSource = _audioOutFilter as IReferenceClock;

            aHr = _mediaFilter.SetSyncSource(aSyncReferenceSource);
            DsError.ThrowExceptionForHR(aHr);

            _outAudioPin = _audioInFilter.GetPin("Audio Capture");
            _inAudioPin = _audioOutFilter.GetPin("Audio Input pin (rendered)");

            aHr = _graphBuilder.Connect(_outAudioPin, _inAudioPin);
            DsError.ThrowExceptionForHR(aHr);

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

                var sourceFormatInfo = (VideoInfoHeader)Marshal.PtrToStructure(pmtConfig.formatPtr, typeof(VideoInfoHeader));
                var sourceFormatCaps = (VideoStreamConfigCaps)Marshal.PtrToStructure(taskMemory, typeof(VideoStreamConfigCaps));

                var mediaFormatTypeName = App.MediaStubTypeDictionary[pmtConfig.subType];

                if (!_supportedFormats.ContainsKey(mediaFormatTypeName))
                {
                    _supportedFormats.Add(mediaFormatTypeName, new List<Tuple<Size, float, float>>());
                }

                var formatGeneric =
                    new Tuple<Size, float, float>(
                        new Size(sourceFormatInfo.BmiHeader.Width, sourceFormatInfo.BmiHeader.Height),
                        1f * App.TenMill / sourceFormatCaps.MaxFrameInterval, 1f * App.TenMill / sourceFormatCaps.MinFrameInterval);

                if (!_supportedFormats[mediaFormatTypeName].Contains(formatGeneric))
                {
                    _supportedFormats[mediaFormatTypeName].Add(formatGeneric);
                }
            }

            Marshal.FreeCoTaskMem(taskMemory);
        }

        private void DetectCurrentFormat()
        {
            AMMediaType mediaType;

            var hr = _streamConfig.GetFormat(out mediaType);

            DsError.ThrowExceptionForHR(hr);

            _currentMediaType = mediaType;
            _currentVideoInfo = (VideoInfoHeader)Marshal.PtrToStructure(_currentMediaType.formatPtr, typeof(VideoInfoHeader));

            if (Settings.Default.Format == Guid.Empty)
            {
                Settings.Default.Format = _currentMediaType.subType;
                Settings.Default.Save();
            }

            if (Settings.Default.Frametime == default(float))
            {
                Settings.Default.Frametime = SourceFrametime;
                Settings.Default.Save();
            }
        }

        public void ChangeFormat(float frametime, Size? videoSize = null, string format = null)
        {
            if (SourceFrametime == frametime)
            {
                return;
            }

            var wasVisible = Visible;

            Hide();

            var hr = 0;

            hr = _mediaControl.Stop();
            DsError.ThrowExceptionForHR(hr);

            //hr = _graphBuilder.Disconnect(_outVideoPin);
            //DsError.ThrowExceptionForHR(hr);

            Settings.Default.Frametime = frametime;
            Settings.Default.Save();

            _currentVideoInfo.AvgTimePerFrame = (long)frametime;

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
            if (volume > VolumeMax)
            {
                volume = 0;
            }
            else if (volume < VolumeOff && volume != VolumeMin)
            {
                volume = VolumeOff;
            }
            else if (volume < VolumeMin)
            {
                volume = VolumeMin;
            }

            if (Settings.Default.Mute)
            {
                volume = VolumeMin;
                showStatus = false;
            }
  
            Settings.Default.Volume = volume;
            Settings.Default.Save();

            _basicAudio.put_Volume(volume);

            if (volume == VolumeMin || !showStatus)
            {
                return;
            }

            var absOffVol = Math.Abs(VolumeOff);
            var revVol = volume + absOffVol;
            var percentageVol = .0d;

            if (revVol != 0)
            {
                percentageVol = Math.Floor((float)revVol / absOffVol * 100);
            }

            var subCalc = percentageVol / 10;
            var numOfVolBlocks = subCalc < 5 ? Math.Ceiling(subCalc) : Math.Floor(subCalc);

            SetStatusText($"VOLUME |{new string('█', (int)numOfVolBlocks),-10}| {percentageVol,-3}");
        }

        public void SetStatusText(string text)
        {
            overlayBottomCenter.Text = text;

            var x = Size.Width / 2 - overlayBottomCenter.Width / 2;
            overlayBottomCenter.Location = new Point(x, overlayBottomCenter.Location.Y);

            overlayBottomCenter.Visible = true;

            _centerTextVisibilityTimer = CenterTextUiVisibilityTime;
        }

        #endregion

        #region Toggles

        public void ToggleFullscreen()
        {
            FullscreenSet(!Settings.Default.Fullscreen);
        }

        public void ToggleMute()
        {
            Settings.Default.Mute = !Settings.Default.Mute;

            if (Settings.Default.Mute)
            {
                Settings.Default.MutedVolume = Settings.Default.Volume;
                VolumeSet(VolumeMin);
                _centerTextVisibilityTimer = 0;
            }
            else
            {
                VolumeSet(Settings.Default.MutedVolume);
                Settings.Default.MutedVolume = VolumeMax;
            }

            Settings.Default.Save();

            overlayTopRight.Visible = Settings.Default.Mute;
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
            var sourceSize = new Size(0, 0);
            var sourceRatio = 0.0f;

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
                _videoContainer.Height = (int)(_videoContainer.Width / sourceRatio);
                _videoContainer.Top = (ClientRectangle.Height - _videoContainer.Height) / 2;
                _videoContainer.Left = 0;
            }
            else
            {
                _videoContainer.Height = ClientRectangle.Height;
                _videoContainer.Width = (int)(_videoContainer.Height * sourceRatio);
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
                if (IsFullscreen) return;

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
            if (Keyboard.GetState(Keys.Up).IsPressed)
            {
                VolumeIncrement(VolumeStep);
                
            }
            else if (Keyboard.GetState(Keys.Down).IsPressed)
            {
                VolumeDecrement(VolumeStep);
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

                Debug.WriteLine(_centerTextVisibilityTimer);
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

        private void AudioOutDispose()
        {
            _audioOut?.Dispose();
        }

        private void FilterDispose()
        {
            if (_videoInFilter != null)
            {
                _videoInFilter.Stop();
                Marshal.ReleaseComObject(_videoInFilter);
                _videoInFilter = null;
            }

            if (_audioInFilter != null)
            {
                _audioInFilter.Stop();
                Marshal.ReleaseComObject(_audioInFilter);
                _audioInFilter = null;
            }

            if (_audioOutFilter != null)
            {
                _audioOutFilter.Stop();
                Marshal.ReleaseComObject(_audioOutFilter);
                _audioOutFilter = null;
            }
        }

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
