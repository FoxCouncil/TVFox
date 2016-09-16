using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DirectShowLib;
// ReSharper disable SuspiciousTypeConversion.Global

namespace TvFox
{
    public sealed partial class VideoForm : Form
    {
        private readonly DsDevice m_videoIn;
        private readonly DsDevice m_audioIn;

        private DsDevice m_audioOut;

        private IBaseFilter m_videoInFilter;
        private IBaseFilter m_videoOutFilter;
        private IBaseFilter m_audioInFilter;
        private IBaseFilter m_audioOutFilter;

        private IFilterGraph m_filterGraph;
        private IGraphBuilder m_graphBuilder;
        private IMediaControl m_mediaControl;
        private IVideoWindow m_videoWindow;
        private IMediaFilter m_mediaFilter;
        private IMediaEventEx m_mediaEvent;

        private bool m_doubleClickCheck;
        private DateTime m_doubleClickTimer;

        private readonly Control m_videoContainer;
        private readonly ContextMenu m_contextMenu;
        private IPin m_outVideoPin;
        private IPin m_inVideoPin;
        private IPin m_outAudioPin;
        private IPin m_inAudioPin;

        const float kTargetRatio3x2 = 3f / 2f;
        const float kTargetRatio16x9 = 16f / 9f;
        const float kTargetRatio4X3 = 4f / 3f;
        const float kTargetRatio16X10 = 16f / 10f;

        private const int kWmGraphNotify = 0x0400 + 13;

        public bool ShouldClose
        {
            get;
            set;
        } = false;

        public VideoForm(DsDevice c_videoIn, DsDevice c_audioIn)
        {
            if (c_videoIn == null)
            {
                throw new ArgumentNullException("The video capture device must not be null!");
            }

            m_videoIn = c_videoIn;

            if (c_audioIn == null)
            {
                throw new ArgumentNullException("The audio capture device must not be null!");
            }

            m_audioIn = c_audioIn;

            InitializeComponent();

            m_videoContainer = new Control { BackColor = Color.Black };
            m_videoContainer.MouseDown += (c_sender, c_args) => MouseClicked(c_args);

            Controls.Add(m_videoContainer);

            AudioOutSetup();
            FilterSetup();
            DirectShowSetup();

            m_doubleClickCheck = false;

            BackColor = Color.Black;
            ShowInTaskbar = true;
            Text = $"TvFox: {App.SourceVideoSize.Width}x{App.SourceVideoSize.Height} {App.SourceFrameRate}fps";

            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            m_contextMenu = new ContextMenu();
            m_contextMenu.MenuItems.Add("Source Dimention Lock", (c_sender, c_args) =>
            {
                ((MenuItem)c_sender).Checked = !((MenuItem)c_sender).Checked;
            });
            m_contextMenu.MenuItems[0].Checked = true;

            m_contextMenu.MenuItems.Add("-");
            m_contextMenu.MenuItems.Add("Exit", (c_sender, c_args) => Application.Exit());

            FormClosing += (c_sender, c_args) =>
            {
                switch (c_args.CloseReason)
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
                            c_args.Cancel = true;
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

            MouseDown += (c_sender, c_args) => MouseClicked(c_args);
            Resize += (c_sender, c_args) => HandleWindowResize();
            VisibleChanged += (c_sender, c_args) => HandleWindowVisibilityChange();

            m_mediaControl.Run();
        }

        private void GetVideoDetails()
        {
            var a_qualityProperties = m_videoOutFilter as IQualProp;

            int piAvgFrameRate;
            a_qualityProperties.get_AvgFrameRate(out piAvgFrameRate);

            Debug.WriteLine(piAvgFrameRate);
        }

        public void ToggleFullscreen()
        {
            OABool a_fullScreenMode;
            m_videoWindow.get_FullScreenMode(out a_fullScreenMode);
            m_videoWindow.put_FullScreenMode(a_fullScreenMode == OABool.True ? OABool.False : OABool.True);
        }

        private void HandleWindowVisibilityChange()
        {
            if (Visible)
            {
                m_mediaControl.Run();
            }
            else
            {
                m_mediaControl.Pause();
            }
        }

        private void HandleWindowResize()
        {
            var a_sourceRatio = App.GetSourceRatio();

            if (m_contextMenu.MenuItems[0].Checked)
            {
                ClientSize = App.SourceVideoSize;
            }
            else
            {
                var a_minWidth = App.SourceVideoSize.Width / 2;
                var a_minHeight = App.SourceVideoSize.Height / 2;

                if (Width <= a_minWidth)
                {
                    Width = a_minWidth;
                }

                if (Height <= a_minHeight)
                {
                    Height = a_minHeight;
                }
            }

            float a_windowRatio = 1f * ClientRectangle.Width / ClientRectangle.Height;

            if (a_windowRatio < a_sourceRatio)
            {
                m_videoContainer.Width = ClientRectangle.Width;
                m_videoContainer.Height = (int)(m_videoContainer.Width / a_sourceRatio);
                m_videoContainer.Top = (ClientRectangle.Height - m_videoContainer.Height) / 2;
                m_videoContainer.Left = 0;
            }
            else
            {
                m_videoContainer.Height = ClientRectangle.Height;
                m_videoContainer.Width = (int)(m_videoContainer.Height * a_sourceRatio);
                m_videoContainer.Top = 0;
                m_videoContainer.Left = (ClientRectangle.Width - m_videoContainer.Width) / 2;
            }

            m_videoWindow.SetWindowPosition(0, 0, m_videoContainer.Width, m_videoContainer.Height);
        }

        private void MouseClicked(MouseEventArgs c_args)
        {
            if (c_args.Button == MouseButtons.Right)
            {
                GetVideoDetails();
                m_contextMenu.Show(this, c_args.Location);
            }
            else
            {
                if (!m_doubleClickCheck)
                {
                    m_doubleClickTimer = DateTime.Now;
                    m_doubleClickCheck = true;
                }
                else
                {
                    if (DateTime.Now - m_doubleClickTimer < TimeSpan.FromSeconds(1))
                    {
                        ToggleFullscreen();
                    }

                    m_doubleClickCheck = false;
                }
            }
        }

        private void AudioOutSetup()
        {
            var a_audioOutputDeviceList = DsDevice.GetDevicesOfCat(FilterCategory.AudioRendererCategory);

            if (a_audioOutputDeviceList.Length == 0)
            {
                throw new ApplicationException("No compatible output audio device found...");
            }

            m_audioOut = a_audioOutputDeviceList.First(c_device => c_device.Name.Contains("Default DirectSound Device"));
        }

        private void AudioOutDispose()
        {
            m_audioOut?.Dispose();
        }

        private void FilterSetup()
        {
            m_videoInFilter = App.CreateFilter(FilterCategory.VideoInputDevice, m_videoIn.Name);
            m_audioInFilter = App.CreateFilter(FilterCategory.AudioInputDevice, m_audioIn.Name);
            m_audioOutFilter = App.CreateFilter(FilterCategory.AudioRendererCategory, m_audioOut.Name);
        }

        private void FilterDispose()
        {
            if (m_videoInFilter != null)
            {
                Marshal.ReleaseComObject(m_videoInFilter);
                m_videoInFilter = null;
            }

            if (m_audioInFilter != null)
            {
                Marshal.ReleaseComObject(m_audioInFilter);
                m_audioInFilter = null;
            }

            if (m_audioOutFilter != null)
            {
                Marshal.ReleaseComObject(m_audioOutFilter);
                m_audioOutFilter = null;
            }
        }

        public void DirectShowSetup()
        {
            int a_hr;

            m_filterGraph = (IFilterGraph)new FilterGraph();

            //Create the Graph Builder
            m_graphBuilder = m_filterGraph as IGraphBuilder;
            m_mediaControl = m_filterGraph as IMediaControl;
            m_videoWindow = m_filterGraph as IVideoWindow;
            m_mediaFilter = m_filterGraph as IMediaFilter;
            m_mediaEvent = m_filterGraph as IMediaEventEx;

            if (m_mediaEvent != null)
            {
                a_hr = m_mediaEvent.SetNotifyWindow(Handle, kWmGraphNotify, IntPtr.Zero);
                DsError.ThrowExceptionForHR(a_hr);
            }

            //Add the Video input device to the graph
            a_hr = m_filterGraph.AddFilter(m_videoInFilter, "Source Video Filter");
            DsError.ThrowExceptionForHR(a_hr);

            m_videoOutFilter = (IBaseFilter)new VideoRenderer();

            a_hr = m_filterGraph.AddFilter(m_videoOutFilter, "Video Renderer");
            DsError.ThrowExceptionForHR(a_hr);

            m_outVideoPin = App.GetPin(m_videoInFilter, "Video Capture");
            m_inVideoPin = App.GetPin(m_videoOutFilter, "Input");

            a_hr = m_graphBuilder.Connect(m_outVideoPin, m_inVideoPin);
            DsError.ThrowExceptionForHR(a_hr);

            // Add Audio Devices
            a_hr = m_filterGraph.AddFilter(m_audioInFilter, "Source Audio Filter");
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_filterGraph.AddFilter(m_audioOutFilter, "Audio Renderer");
            DsError.ThrowExceptionForHR(a_hr);

            var a_syncReferenceSource = m_audioOutFilter as IReferenceClock;

            a_hr = m_mediaFilter.SetSyncSource(a_syncReferenceSource);
            DsError.ThrowExceptionForHR(a_hr);

            a_syncReferenceSource = null;

            m_outAudioPin = App.GetPin(m_audioInFilter, "Audio Capture");
            m_inAudioPin = App.GetPin(m_audioOutFilter, "Audio Input pin (rendered)");

            a_hr = m_graphBuilder.Connect(m_outAudioPin, m_inAudioPin);
            DsError.ThrowExceptionForHR(a_hr);

            m_videoWindow.put_Owner(m_videoContainer.Handle);
            m_videoWindow.put_MessageDrain(m_videoContainer.Handle);
            m_videoWindow.put_WindowStyle(WindowStyle.Child | WindowStyle.ClipSiblings | WindowStyle.ClipChildren);
            m_videoWindow.SetWindowPosition(0, 0, m_videoContainer.Width, m_videoContainer.Height);

            /*var a_dsAudio = (IAMDirectSound)m_audioOutFilter;
            a_dsAudio.SetFocusWindow(Handle, true);*/

            m_mediaControl.Run();
        }

        public void DirectShowDispose()
        {
            int a_hr = 0;

            a_hr = m_mediaControl.Stop();
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_mediaEvent.SetNotifyWindow(IntPtr.Zero, 0, IntPtr.Zero);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_graphBuilder.Disconnect(m_inVideoPin);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_graphBuilder.Disconnect(m_inAudioPin);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_graphBuilder.Disconnect(m_outVideoPin);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_graphBuilder.Disconnect(m_outAudioPin);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_filterGraph.RemoveFilter(m_videoInFilter);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_filterGraph.RemoveFilter(m_audioInFilter);
            DsError.ThrowExceptionForHR(a_hr);

            Marshal.ReleaseComObject(m_videoInFilter);
            m_videoInFilter = null;

            Marshal.ReleaseComObject(m_audioInFilter);
            m_audioInFilter = null;

            a_hr = m_filterGraph.RemoveFilter(m_videoOutFilter);
            DsError.ThrowExceptionForHR(a_hr);

            a_hr = m_filterGraph.RemoveFilter(m_audioOutFilter);
            DsError.ThrowExceptionForHR(a_hr);

            Marshal.ReleaseComObject(m_videoOutFilter);
            m_videoOutFilter = null;

            Marshal.ReleaseComObject(m_audioOutFilter);
            m_audioOutFilter = null;

            m_graphBuilder = null;
            m_mediaControl = null;
            m_videoWindow = null;
            m_mediaFilter = null;
            m_mediaEvent = null;

            Marshal.ReleaseComObject(m_outAudioPin);
            m_outAudioPin = null;

            Marshal.ReleaseComObject(m_inAudioPin);
            m_inAudioPin = null;

            Marshal.ReleaseComObject(m_outVideoPin);
            m_outVideoPin = null;

            Marshal.ReleaseComObject(m_inVideoPin);
            m_inVideoPin = null;

            Marshal.ReleaseComObject(m_filterGraph);
            m_filterGraph = null;
        }
    }
}
