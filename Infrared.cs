#region Header

//   !!  // TvFox - Infrared.cs
// *.-". // Created: 2017-09-22 [4:46 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 5:59 PM

#endregion

#region Usings

using System;
using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;

#endregion

namespace TvFox
{
    internal static class Infrared
    {
        private static readonly object LockObj = new object();

        internal const int PACKET_ACK = 0x00;

        private const int CONFIGURATION_ID = 1;
        private const int INTERFACE_ID = 0;
        private const int WRITE_TIMEOUT = 2000;
        private const int PACKET_SIZE = 8;
        private const int TIME_HEADER = 2400;
        private const int TIME_SPACE = 600;
        private const int TIME_PULSE = 1200;
        private const int TIME_TOLERANCE = 100;

        private const double INTERVAL_AMOUNT = 21.33; // in µs

        private const ReadEndpointID READ_ENDPOINT_ID = ReadEndpointID.Ep01;
        private const WriteEndpointID WRITE_ENDPOINT_ID = WriteEndpointID.Ep02;

        private static readonly UsbDeviceFinder InguanaIrFinder = new UsbDeviceFinder(0x1781, 0x0938);

        private static UsbDevice _device;
        private static UsbEndpointReader _reader;
        private static UsbEndpointWriter _writer;

        private static int _infraredState = -1;
        private static bool _infraredReceiving;
        private static double _infraredAccumulator;
        private static readonly StringBuilder BitArrayBuilder = new StringBuilder();

        private static DateTime _lastCommandTime = DateTime.MinValue;

        public static event Action<CommandButtons> Remote;

        public static void Initialize()
        {
            UsbDevice.UsbErrorEvent += UsbDevice_UsbErrorEvent;

            _device = UsbDevice.OpenUsbDevice(InguanaIrFinder);

            DeviceCapture();
            EventsListen();
            GetVersion();
            ReceiveEnable();
        }

        public static void Dispose()
        {
            ReceiveDisable();
            EventsIgnore();
            DeviceRelease();

            UsbDevice.Exit();
        }

        public static void GetVersion()
        {
            WritePacket(UsbPacket.GeneratePacket(UsbPacketDirection.HostToTransceiver, UsbPacketType.Version));
        }

        public static void ReceiveEnable()
        {
            WritePacket(UsbPacket.GeneratePacket(UsbPacketDirection.HostToTransceiver, UsbPacketType.RecieverEnable));
        }

        public static void ReceiveDisable()
        {
            WritePacket(UsbPacket.GeneratePacket(UsbPacketDirection.HostToTransceiver, UsbPacketType.RecieverDisable));
        }

        #region Private Methods

        private static void WritePacket(UsbPacket packet)
        {
            var errorCode = _writer.Write(packet.GetByteArray(), WRITE_TIMEOUT, out var bytesWritten);

            if (errorCode != ErrorCode.Ok)
            {
                throw new Exception(errorCode.ToString(), new Exception(UsbDevice.LastErrorString));
            }

            if (bytesWritten != PACKET_SIZE)
            {
                throw new Exception("Packet Size Mismatch!");
            }
        }

        private static void DeviceCapture()
        {
            ((IUsbDevice) _device).SetConfiguration(CONFIGURATION_ID);
            ((IUsbDevice) _device).ClaimInterface(INTERFACE_ID);
        }

        private static void DeviceRelease()
        {
            if (_device.IsOpen)
            {
                ((IUsbDevice) _device).ReleaseInterface(INTERFACE_ID);
            }

            _device.Close();
        }

        private static void EventsListen()
        {
            _reader = _device.OpenEndpointReader(READ_ENDPOINT_ID, 8, EndpointType.Interrupt);
            _writer = _device.OpenEndpointWriter(WRITE_ENDPOINT_ID, EndpointType.Interrupt);

            _reader.DataReceived += Reader_DataReceived;
            _reader.DataReceivedEnabled = true;
        }

        private static void EventsIgnore()
        {
            _reader.DataReceivedEnabled = false;
            _reader.DataReceived -= Reader_DataReceived;
        }

        private static void ResetState(bool state)
        {
            lock (LockObj)
            {
                _infraredReceiving = state;
                _infraredState = -1;
                BitArrayBuilder.Clear();
            }
        }

        #endregion

        #region Event Handlers

        private static void Reader_DataReceived(object sender, EndpointDataEventArgs e)
        {
            if (e.Count == 0)
            {
                return;
            }

            var packet = UsbPacket.ParseByteArray(e.Buffer, e.Count);

            if (packet.Type == UsbPacketType.RecieverData)
            {
                var pulsesData = packet.GetPulses();

                foreach ((bool isPulse, byte length) in pulsesData)
                {
                    // Accumulate!
                    if (length == 127)
                    {
                        _infraredAccumulator += 128 * INTERVAL_AMOUNT;
                    }
                    else
                    {
                        var timeElapsed = (int) Math.Ceiling((length + 1) * INTERVAL_AMOUNT);

                        if ((int)_infraredAccumulator != 0)
                        {
                            timeElapsed += (int) Math.Ceiling(_infraredAccumulator);
                        }

                        _infraredAccumulator = 0;

                        if (DateTime.Now - _lastCommandTime > TimeSpan.FromMilliseconds(250) &&!_infraredReceiving && isPulse && timeElapsed.IsWithinTolerance(TIME_HEADER))
                        {
                            ResetState(true);
                        }
                        else if (_infraredReceiving)
                        {
                            switch (_infraredState)
                            {
                                case -1:
                                {
                                    if (!isPulse && timeElapsed.IsWithinTolerance(TIME_SPACE))
                                    {
                                        _infraredState = 0;
                                    }
                                    else
                                    {
                                        ResetState(false);
                                    }

                                    break;
                                }

                                case 0:
                                case 2:
                                case 4:
                                case 6:
                                case 8:
                                case 10:
                                case 12: // 7-bits
                                {
                                    if (!isPulse)
                                    {
                                        ResetState(false);
                                    }
                                    else
                                    {
                                        if (timeElapsed.IsWithinTolerance(TIME_PULSE))
                                        {
                                            BitArrayBuilder.Insert(0, "1");
                                            _infraredState++;
                                        }
                                        else if (timeElapsed.IsWithinTolerance(TIME_SPACE))
                                        {
                                            BitArrayBuilder.Insert(0, "0");
                                            _infraredState++;
                                        }
                                        else
                                        {
                                            ResetState(false);
                                        }
                                    }

                                    break;
                                }

                                case 1:
                                case 3:
                                case 5:
                                case 7:
                                case 9:
                                case 11:
                                case 13:
                                {
                                    if (isPulse)
                                    {
                                        ResetState(false);
                                    }
                                    else if (_infraredState != 13 && !timeElapsed.IsWithinTolerance(TIME_SPACE))
                                    {
                                        ResetState(false);
                                    }
                                    else if (_infraredState == 13)
                                    {
                                        var irCommand = Convert.ToInt32(BitArrayBuilder.ToString(), 2);
                                        Remote?.Invoke((CommandButtons)irCommand);
                                        _lastCommandTime = DateTime.Now;
                                        ResetState(false);
                                        return;
                                    }

                                    _infraredState++;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine(packet.ToString());
            }
        }

        private static void UsbDevice_UsbErrorEvent(object sender, UsbError e)
        {
            throw new Exception(e.Description);
        }

        #endregion

        #region Extension Methods

        private static bool IsWithinTolerance(this int number, int compare, int tolerance = TIME_TOLERANCE)
        {
            return !(number < compare - tolerance || number > compare + tolerance);
        }

        public static void Deconstruct<T1, T2>(this Tuple<T1, T2> tuple, out T1 key, out T2 value)
        {
            key = tuple.Item1;
            value = tuple.Item2;
        }

        #endregion
    }
}