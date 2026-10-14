using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ScreenStudio;
using ScreenStudio.Models;
internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out NativeRect rect,int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd,IntPtr dc,uint flags);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,int operation);
    private static async Task ChromeChecks()
    {
        var window = new MainWindow(new StudioPreferences(),Path.Combine(directory,"chrome-preferences.json"));
        window.Width=1280; window.Height=800; window.Left=40; window.Top=40; window.WindowStartupLocation=WindowStartupLocation.Manual;
        window.Show(); window.Activate(); await Task.Delay(400); window.UpdateLayout();
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var chrome = WindowChrome.GetWindowChrome(window);
            var result = DwmGetWindowAttribute(handle,5,out var buttons,Marshal.SizeOf<NativeRect>());
            Check(chrome.UseAeroCaptionButtons && chrome.GlassFrameThickness.Top >= 32 && result == 0 && buttons.Right > buttons.Left,"Windows supplies real native caption buttons");
            var title = (TextBlock)window.FindName("ProjectCaption"); var root = (FrameworkElement)window.Content;
            var point = title.TranslatePoint(new Point(title.ActualWidth/2,0),root);
            Check(Math.Abs(point.X-root.ActualWidth/2)<2,"project caption is centered in the full window");
            GetWindowRect(handle,out var rect); var dc=GetWindowDC(handle); var memory=CreateCompatibleDC(dc); var bitmap=CreateCompatibleBitmap(dc,rect.Right-rect.Left,rect.Bottom-rect.Top); var previous=SelectObject(memory,bitmap);
            try
            {
                if (PrintWindow(handle,memory,2))
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var output=File.Create(Path.Combine(directory,"studio-native-window.png")); encoder.Save(output);
                }
                var screen=GetWindowDC(IntPtr.Zero);
                try { if (BitBlt(memory,0,0,rect.Right-rect.Left,rect.Bottom-rect.Top,screen,rect.Left,rect.Top,0x00CC0020))
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var output=File.Create(Path.Combine(directory,"studio-native-screen.png")); encoder.Save(output);
                }
            }
                finally { ReleaseDC(IntPtr.Zero,screen); }
            }
            finally { SelectObject(memory,previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(handle,dc); }
            window.Width=980; window.Height=680; await Task.Delay(100); window.UpdateLayout();
            var toolbar = (ToolBar)window.FindName("ToolBar");
            toolbar.Width=400; window.UpdateLayout();
            var overflow = (ToggleButton)toolbar.Template.FindName("Overflow",toolbar);
            Check(toolbar.HasOverflowItems && overflow.IsVisible && overflow.ActualWidth >= 30,"narrow toolbar keeps extra editing tools accessible");
            toolbar.IsOverflowOpen=true; await Task.Delay(100); window.UpdateLayout();
            var extra = (ToolBarOverflowPanel)toolbar.Template.FindName("PART_ToolBarOverflowPanel",toolbar);
            Check(extra.Children.OfType<Button>().Any(),"overflow menu displays the remaining editing actions");
            var tools = toolbar.Items.OfType<Button>().ToArray();
            Check(tools.All(b => b.Content is ScreenStudio.UI.LucideIcon && b.ToolTip is string && b.Width == b.Height),"toolbar uses square icon-only buttons with name tooltips");
            toolbar.IsOverflowOpen=false;
        }
        finally { window.Close(); }
    }
}

