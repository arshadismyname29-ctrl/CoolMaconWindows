$ErrorActionPreference = 'Stop'
if (-not ('CoolMacTaskbarRepair' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class CoolMacTaskbarRepair {
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct DATA { public uint Size; public IntPtr Window; public uint Callback, Edge; public RECT Rectangle; public IntPtr Parameter; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder value, int capacity);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message, ref DATA data);
    public static string Restore() {
        IntPtr primary = FindWindow("Shell_TrayWnd",null);
        if (primary == IntPtr.Zero) throw new InvalidOperationException("The Windows taskbar is unavailable.");
        DATA data = new DATA { Size=(uint)Marshal.SizeOf(typeof(DATA)), Window=primary };
        uint state=(uint)SHAppBarMessage(4,ref data).ToUInt64(); data.Parameter=new IntPtr(state & ~1u);
        SHAppBarMessage(10,ref data);
        EnumWindows(delegate(IntPtr window, IntPtr unused) {
            var name=new StringBuilder(128); GetClassName(window,name,name.Capacity);
            if (name.ToString()=="Shell_TrayWnd" || name.ToString()=="Shell_SecondaryTrayWnd") ShowWindow(window,8);
            return true;
        },IntPtr.Zero);
        return "Windows taskbar restored; auto-hide disabled.";
    }
}
'@
}
[CoolMacTaskbarRepair]::Restore()
