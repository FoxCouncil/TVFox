using System;
using System.Collections.Generic;
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
    public sealed partial class VideoForm : Form
    {
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

        private Dictionary<string, List<Tuple<Size, float, float>>> _supportedFormats = new Dictionary<string, List<Tuple<Size, float, float>>>();

        const float KTargetRatio3X2 = 3f / 2f;
        const float KTargetRatio16X9 = 16f / 9f;
        const float KTargetRatio4X3 = 4f / 3f;
        const float KTargetRatio16X10 = 16f / 10f;

        private const int KWmGraphNotify = 0x0400 + 13;

        public bool ShouldClose
        {
            get;
            set;
        } = false;

        public IReadOnlyDictionary<string, List<Tuple<Size, float, float>>> SupportedFormats => _supportedFormats;

        public Size SourceSize => new Size(_currentVideoInfo.BmiHeader.Width, _currentVideoInfo.BmiHeader.Height);

        public float SourceFramerate => App.TenMill / SourceFrametime;

        public float SourceFrametime => _currentVideoInfo.AvgTimePerFrame;

        public string SourceFormat => App.MediaStubTypeDictionary[_currentMediaType.subType];

        public string SourceDevice => _videoIn.Name;

        public VideoForm(DsDevice videoIn, DsDevice audioIn)
        {
            StartPosition = FormStartPosition.Manual;
            // ChangeFormat(59.9401779f);

            _videoIn = videoIn;
            _audioIn = audioIn;

            Controls.Add(_videoContainer = new Control { BackColor = Color.Black });

            InitializeComponent();

            Location = Properties.Settings.Default.WindowPosition;
            Size = Properties.Settings.Default.WindowSize;
            WindowState = Properties.Settings.Default.WindowState;

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

            overlayTopLeft.Visible = Properties.Settings.Default.ShowFps;
            overlayTopLeft.BringToFront();
        }

        #region Setup Methods

        private void SetupEvents()
        {
            LocationChanged += (sender, args) =>
            {
                

                if (!Properties.Settings.Default.Fullscreen)
                {
                    Properties.Settings.Default.WindowPosition = Location;
                    Properties.Settings.Default.Save(); 
                }
            };

            SizeChanged += (sender, args) =>
            {
                if (!Properties.Settings.Default.Fullscreen)
                {
                    Properties.Settings.Default.WindowSize = Size;
                    Properties.Settings.Default.WindowState = WindowState;

                    Properties.Settings.Default.Save();
                }
            };

            _videoContainer.MouseDown += (sender, args) => MouseClicked(args);

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

                FilterDispose();
                AudioOutDispose();
            };
        
            MouseDown += (sender, args) => MouseClicked(args);
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
                aHr = _mediaEvent.SetNotifyWindow(Handle, KWmGraphNotify, IntPtr.Zero);
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

            if (Properties.Settings.Default.Format == Guid.Empty)
            {
                Properties.Settings.Default.Format = _currentMediaType.subType;
                Properties.Settings.Default.Save();
            }

            if (Properties.Settings.Default.Frametime == default(float))
            {
                Properties.Settings.Default.Frametime = SourceFrametime;
                Properties.Settings.Default.Save();
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

            Properties.Settings.Default.Frametime = frametime;
            Properties.Settings.Default.Save();

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

        private int GetVideoDetails()
        {
            var aQualityProperties = _videoOutFilter as IQualProp;

            int piAvgFrameRate;

            aQualityProperties.get_AvgFrameRate(out piAvgFrameRate);

            return piAvgFrameRate;
        }

        private Size oldSize;
        private Point oldLocation;
        private FormWindowState oldState;

        public void FullscreenToggle()
        {
            FullscreenSet(!Properties.Settings.Default.Fullscreen);
        }

        public void FullscreenSet(bool value)
        {
            Properties.Settings.Default.Fullscreen = value;
            Properties.Settings.Default.Save();

            if (value)
            {
                oldSize = Size;
                oldLocation = Location;
                oldState = WindowState;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = oldState;
                FormBorderStyle = FormBorderStyle.Sizable;

                Location = oldLocation;
                Size = oldSize;
            }

            HandleWindowResize();
        }

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

        private float GetSourceRatio()
        {
            var sourceSize = SourceSize;

            return 1f * sourceSize.Width / sourceSize.Height;
        }

        public void HandleWindowResize()
        {
            var sourceSize = SourceSize;
            var sourceRatio = GetSourceRatio();

            if (Properties.Settings.Default.SourceDemensionLock && WindowState != FormWindowState.Maximized && !Properties.Settings.Default.Fullscreen)
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

            float windowRatio = 1f * ClientRectangle.Width / ClientRectangle.Height;

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

        private void MouseClicked(MouseEventArgs cArgs)
        {
            if (cArgs.Button == MouseButtons.Right)
            {
                int volume;
                _basicAudio.get_Volume(out volume);
                Debug.WriteLine(volume);

                App.ContextMenu.Show(this, cArgs.Location);
            }
            else
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
                        FullscreenToggle();
                    }

                    _doubleClickCheck = false;
                }
            }
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
                Marshal.ReleaseComObject(_videoInFilter);
                _videoInFilter = null;
            }

            if (_audioInFilter != null)
            {
                Marshal.ReleaseComObject(_audioInFilter);
                _audioInFilter = null;
            }

            if (_audioOutFilter != null)
            {
                Marshal.ReleaseComObject(_audioOutFilter);
                _audioOutFilter = null;
            }
        }

        public void DirectShowDispose()
        {
            int aHr;

            DsUtils.FreeAMMediaType(_currentMediaType);

            aHr = _mediaControl.Stop();
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

        private void mainTimer_Tick(object sender, EventArgs e)
        {
            if (Settings.Default.ShowFps)
            {
                var framrate = (float)GetVideoDetails() / 100f;
                overlayTopLeft.Text = $"{framrate:F} fps";
            }
        }
    }
}
