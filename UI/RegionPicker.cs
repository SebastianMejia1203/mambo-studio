using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenStudio.Models;
namespace ScreenStudio.UI;
public sealed class RegionPicker : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
    public CaptureBounds? Selection { get; private set; }
    public RegionPicker(CaptureBounds bounds)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(70,0,0,0)); Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = bounds.X; Top = bounds.Y; Width = bounds.Width; Height = bounds.Height;
        var canvas = new Canvas(); Content = canvas;
        var hint = StudioUi.Label("Arrastra para seleccionar · Esc para cancelar"); hint.Background = Brushes.Black; hint.Foreground = Brushes.White; hint.Padding = new Thickness(12); Canvas.SetLeft(hint,20); Canvas.SetTop(hint,20); canvas.Children.Add(hint);
        var rectangle = new System.Windows.Shapes.Rectangle { Stroke = Brushes.LawnGreen, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35,255,255,255)) }; canvas.Children.Add(rectangle);
        CaptureTargets.POINT origin = default;
        Loaded += (_,_) => SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0040);
        MouseLeftButtonDown += (_,_) => { CaptureTargets.GetCursorPos(out origin); CaptureMouse(); };
        MouseMove += (_,_) => { if (!IsMouseCaptured) return; CaptureTargets.GetCursorPos(out var p); var a = PointFromScreen(new Point(origin.X,origin.Y)); var b = PointFromScreen(new Point(p.X,p.Y)); Canvas.SetLeft(rectangle,Math.Min(a.X,b.X)); Canvas.SetTop(rectangle,Math.Min(a.Y,b.Y)); rectangle.Width = Math.Abs(a.X-b.X); rectangle.Height = Math.Abs(a.Y-b.Y); };
        MouseLeftButtonUp += (_,_) => { if (!IsMouseCaptured) return; ReleaseMouseCapture(); CaptureTargets.GetCursorPos(out var p); var x = Math.Clamp(Math.Min(origin.X,p.X),bounds.X,bounds.X+bounds.Width); var y = Math.Clamp(Math.Min(origin.Y,p.Y),bounds.Y,bounds.Y+bounds.Height); var w = Math.Min(Math.Abs(origin.X-p.X),bounds.X+bounds.Width-x); var h = Math.Min(Math.Abs(origin.Y-p.Y),bounds.Y+bounds.Height-y); if (w >= 32 && h >= 32) { Selection = new(x,y,w,h); DialogResult = true; } };
        KeyDown += (_,e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }
}
