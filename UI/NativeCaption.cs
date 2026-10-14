using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace ScreenStudio.UI;

public static class NativeCaption
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static void Apply(Window window,bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var enabled = dark ? 1 : 0; DwmSetWindowAttribute(handle,20,ref enabled,4);
        var background = dark ? 0x1D1917 : 0xEFEFEC; DwmSetWindowAttribute(handle,35,ref background,4);
        var foreground = dark ? 0xF0EAE8 : 0x393025; DwmSetWindowAttribute(handle,36,ref foreground,4);
        // Unsupported DWM color attributes on older Windows are safely ignored.
    }
}
