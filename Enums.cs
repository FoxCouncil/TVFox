#region Header

//   !!  // TvFox - Enums.cs
// *.-". // Created: 2017-01-03 [10:17 PM]
//  | |  // Copyright 2017 The Fox Council 
// Modified by: Fox Diller on 2017-09-22 @ 6:58 PM

#endregion

namespace TvFox
{
    internal enum AppState
    {
        FirstStart,
        NoSignal,
        Signal
    }

    internal enum WindowState
    {
        FirstStart,
        Hidden,
        Shown,
        Fullscreen
    }

    internal enum CommandButtons
    {
        None = 0,
        VolumeUp = 18,
        VolumeDown = 19,
        Mute = 20,
        Power = 21
    }

    internal enum UsbPacketType : byte
    {
        Version = 0x01,

        RecieverEnable = 0x12,
        RecieverDisable = 0x14,

        RecieverData = 0x30,
        BufferOverflowReciever = 0x31,
        BufferOverflowTransmitter = 0x32
    }

    internal enum UsbPacketDirection : byte
    {
        TransceiverToHost = 0xDC,
        HostToTransceiver = 0xCD
    }
}