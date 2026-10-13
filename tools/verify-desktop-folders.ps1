param([string]$OutputFolder = (Join-Path $PSScriptRoot '..\artifacts\desktop-folders'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -OutputFolder $OutputFolder
    exit $LASTEXITCODE
}
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CoolMacShellPreview {
    [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    public static bool Save(string folder, string output) {
        Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); IImageFactory factory;
        SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out factory);
        IntPtr handle = IntPtr.Zero;
        try {
            Marshal.ThrowExceptionForHR(factory.GetImage(new Size { Width=128, Height=128 }, 0, out handle));
            using (Bitmap bitmap = Image.FromHbitmap(handle)) {
                bitmap.Save(output, ImageFormat.Png);
                long blue=0, red=0, count=0;
                for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) {
                    Color pixel=bitmap.GetPixel(x,y);
                    if(pixel.B+pixel.G+pixel.R > 40) { blue+=pixel.B; red+=pixel.R; count++; }
                }
                return count>0 && blue>red+count*25;
            }
        } finally { if(handle != IntPtr.Zero) DeleteObject(handle); Marshal.ReleaseComObject(factory); }
    }
}
'@ -ReferencedAssemblies System.Drawing
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
foreach ($folder in Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('DesktopDirectory')) -Directory) {
    $resultPath = Join-Path $OutputFolder ($folder.Name + '.png')
    $blue = [CoolMacShellPreview]::Save($folder.FullName, $resultPath)
    if (-not $blue) { throw ('Windows still resolves a non-blue preview for ' + $folder.FullName) }
    Write-Output ('PASS Windows folder image (thumbnails enabled): ' + $folder.Name)
}
