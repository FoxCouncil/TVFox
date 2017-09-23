#region Header

//   !!  // TvFox - Structs.cs
// *.-". // Created: 2017-08-28 [8:11 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 6:46 PM

#endregion

#region Usings

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

#endregion

namespace TvFox
{
    public struct KeyStateInfo
    {
        public static KeyStateInfo Default => new KeyStateInfo(Keys.None, false, false);

        public KeyStateInfo(Keys key, bool ispressed, bool istoggled)
        {
            Key = key;
            IsPressed = ispressed;
            IsToggled = istoggled;
        }

        public Keys Key { get; }

        public bool IsPressed { get; }

        public bool IsToggled { get; }
    }

    internal struct UsbPacket
    {
        private const byte PULSE_MASK = 128;

        public UsbPacketDirection Direction;
        public UsbPacketType Type;
        public byte Data0;
        public byte Data1;
        public byte Data2;
        public byte Data3;

        public byte ReceiverBuffer;
        public byte[] ReceiverData;

        public byte[] GetByteArray()
        {
            return new byte[] { Infrared.PACKET_ACK, Infrared.PACKET_ACK, (byte) Direction, (byte) Type, Data0, Data1, Data2, Data3 };
        }

        public List<Tuple<bool, byte>> GetPulses()
        {
            var ret = new List<Tuple<bool, byte>>();

            foreach (var b in ReceiverData)
            {
                if (b == PULSE_MASK)
                {
                    continue;
                }

                var isPulse = (b & PULSE_MASK) == 0;
                var value = b;

                if (!isPulse)
                {
                    value -= PULSE_MASK;
                }

                ret.Add(new Tuple<bool, byte>(isPulse, value));
            }

            return ret;
        }

        public override string ToString()
        {
            if (Type == 0)
            {
                Debugger.Break();
            }

            return $"UsbPacket Type:({Type.ToString()}) Direction:({Direction.ToString()})";
        }

        public static UsbPacket ParseByteArray(byte[] packetToParse, int size)
        {
            if (size > 8)
            {
                throw new Exception("Incorrect Packet Length");
            }

            if (packetToParse[0] != 0x00 || packetToParse[1] != 0x00)
            {
                var bufferSize = packetToParse[7];
                Array.Resize(ref packetToParse, 7);
                return new UsbPacket
                {
                    Direction = UsbPacketDirection.TransceiverToHost,
                    Type = UsbPacketType.RecieverData,
                    ReceiverBuffer = bufferSize,
                    ReceiverData = packetToParse
                };
            }

            return new UsbPacket
            {
                Direction = (UsbPacketDirection) packetToParse[2],
                Type = (UsbPacketType) packetToParse[3],
                Data0 = packetToParse[4],
                Data1 = packetToParse[5],
                Data2 = packetToParse[6],
                Data3 = packetToParse[7]
            };
        }

        public static UsbPacket GeneratePacket(UsbPacketDirection direction, UsbPacketType type, byte data0 = Infrared.PACKET_ACK, byte data1 = Infrared.PACKET_ACK, byte data2 = Infrared.PACKET_ACK, byte data3 = Infrared.PACKET_ACK)
        {
            return new UsbPacket
            {
                Direction = direction,
                Type = type,
                Data0 = data0,
                Data1 = data1,
                Data2 = data2,
                Data3 = data3
            };
        }
    }
}