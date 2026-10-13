using System;
using System.Runtime.InteropServices;

namespace CoolMac
{
    // Windows Core Audio controls the current default playback device, including Bluetooth changes.
    internal sealed class AudioEndpoint : IDisposable
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevices
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.Interface)] out IVolume volume);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
        private IVolume volume;
        internal AudioEndpoint()
        {
            IDevices devices = (IDevices)new DeviceEnumerator(); IDevice device = null;
            try
            {
                Marshal.ThrowExceptionForHR(devices.GetDefaultAudioEndpoint(0, 1, out device));
                Guid iid = typeof(IVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
            }
            finally { if (device != null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(devices); }
        }
        internal double Level
        {
            get { float level; Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out level)); return level; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, value)), ref context)); }
        }
        internal bool Muted
        {
            get { bool mute; Marshal.ThrowExceptionForHR(volume.GetMute(out mute)); return mute; }
            set { Guid context = Guid.Empty; Marshal.ThrowExceptionForHR(volume.SetMute(value, ref context)); }
        }
        public void Dispose() { if (volume != null) { Marshal.ReleaseComObject(volume); volume = null; } }
    }
}
