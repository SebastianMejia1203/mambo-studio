using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScreenStudio.Models;
using ScreenStudio.UI;
using CaptureMode = ScreenStudio.Models.CaptureMode;
namespace ScreenStudio;

// Vista previa en vivo del origen de captura (estilo OBS): captura el escritorio
// compuesto por DWM con GDI (~5 fps, sin procesos externos) y permite definir
// la región arrastrando sobre la imagen. Al usar el escritorio compuesto las
// ventanas con aceleración GPU se ven correctamente.
public partial class MainWindow
{
    private DispatcherTimer? previewTimer;
    private Canvas? previewOverlay;
    private Rectangle? regionBand;
    private WriteableBitmap? previewFrame;
    private IntPtr previewMemDc, previewDib, previewOldObj, previewBits;
    private int previewW, previewH;
    private bool previewDragging;
    private Point previewDragStart;
    private CaptureBounds previewDragView;

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint SizeImage; public int XPels; public int YPels; public uint ClrUsed; public uint ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct PreviewRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PreviewPoint { public int X,Y; }
    private const uint SRCCOPY = 0x00CC0020;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfoHeader header,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint op);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out PreviewRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd,out PreviewRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd,ref PreviewPoint point);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    private void StartSourcePreview()
    {
        if (previewTimer != null || previewImage == null) return;
        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        previewTimer.Tick += (_,_) => TickPreview();
        previewTimer.Start();
    }
    private void StopSourcePreview()
    {
        previewTimer?.Stop(); previewTimer = null; previewDragging = false; FreePreviewGdi();
    }
    private void TickPreview()
    {
        if (previewImage is not { } image || previewOverlay == null || regionBand == null) { StopSourcePreview(); return; }
        if (!image.IsVisible) { StopSourcePreview(); return; }
        if (!image.IsLoaded) return;
        try
        {
            if (!TryPreviewView(out var view,out var band,out var note)) { ShowPreviewNote(note,true); return; }
            image.Source = CaptureScreen(view);
            DrawBand(band,view);
            ShowPreviewNote(previewDragging ? "Suelta para fijar la región." : note,false);
        }
        catch (Exception e) { ShowPreviewNote(e.Message,true); }
    }
    private bool TryPreviewView(out CaptureBounds view,out CaptureBounds? band,out string note)
    {
        view = default; band = null; note = "";
        var mode = (CaptureMode)(captureModes?.SelectedIndex ?? 0);
        switch (mode)
        {
            case CaptureMode.Screen or CaptureMode.TwoScreens:
            {
                var monitor = CaptureTargets.Monitors().FirstOrDefault(m => m.Device == (captureFirst?.SelectedItem as CaptureMonitor)?.Device)
                    ?? CaptureTargets.Monitors().FirstOrDefault(m => m.Primary) ?? CaptureTargets.Monitors().FirstOrDefault();
                if (monitor == null) { note = "No hay pantallas disponibles."; return false; }
                view = monitor.Bounds; note = monitor.Name + (mode == CaptureMode.TwoScreens ? " · 1 de 2" : ""); return true;
            }
            case CaptureMode.Region:
            {
                TryParseRegion(out var rx,out var ry,out _,out _);
                var monitor = CaptureTargets.Monitors().FirstOrDefault(m => rx >= m.Bounds.X && rx < m.Bounds.X + m.Bounds.Width && ry >= m.Bounds.Y && ry < m.Bounds.Y + m.Bounds.Height)
                    ?? CaptureTargets.Monitors().FirstOrDefault(m => m.Primary) ?? CaptureTargets.Monitors().FirstOrDefault();
                if (monitor == null) { note = "No hay pantallas disponibles."; return false; }
                view = monitor.Bounds; note = monitor.Name;
                if (TryParseRegion(out var x,out var y,out var w,out var h) && w > 0 && h > 0) band = new(x,y,w,h);
                return true;
            }
            default:
            {
                var selected = captureWindow?.SelectedItem as CaptureWindow;
                var handle = (IntPtr)(selected?.Handle ?? 0);
                if (handle == IntPtr.Zero || !IsWindowVisible(handle)) { note = "Selecciona una ventana visible."; return false; }
                if (IsIconic(handle)) { note = "La ventana está minimizada."; return false; }
                if (!GetWindowRect(handle,out var rect) || rect.Right - rect.Left < 2 || rect.Bottom - rect.Top < 2) { note = "No se puede leer la ventana."; return false; }
                view = new(rect.Left,rect.Top,rect.Right - rect.Left,rect.Bottom - rect.Top);
                note = string.IsNullOrWhiteSpace(selected?.Title) ? "Ventana" : selected!.Title;
                if (mode == CaptureMode.WindowRegion && ClientOrigin(handle) is { } origin && TryParseRegion(out var x,out var y,out var w,out var h) && w > 0 && h > 0)
                    band = new(origin.X + x,origin.Y + y,w,h);
                return true;
            }
        }
    }
    private bool TryParseRegion(out int x,out int y,out int w,out int h)
    {
        x = y = w = h = 0;
        try
        {
            if (captureX == null || captureY == null || captureWidth == null || captureHeight == null) return false;
            x = checked((int)StudioDialogs.Number(captureX)); y = checked((int)StudioDialogs.Number(captureY));
            w = checked((int)StudioDialogs.Number(captureWidth)); h = checked((int)StudioDialogs.Number(captureHeight));
            return true;
        }
        catch { return false; }
    }
    private static PreviewPoint? ClientOrigin(IntPtr handle)
    {
        try
        {
            if (!GetClientRect(handle,out _)) return null;
            var point = new PreviewPoint(); if (!ClientToScreen(handle,ref point)) return null;
            return point;
        }
        catch { return null; }
    }
    private WriteableBitmap CaptureScreen(CaptureBounds view)
    {
        if (view.Width < 2 || view.Height < 2 || view.Width > 16384 || view.Height > 16384) throw new InvalidOperationException("Vista previa no disponible para este tamaño.");
        EnsurePreviewGdi(view.Width,view.Height);
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (screen == IntPtr.Zero || !BitBlt(previewMemDc,0,0,view.Width,view.Height,screen,view.X,view.Y,SRCCOPY))
                throw new InvalidOperationException("No se pudo capturar la vista previa.");
        }
        finally { if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen); }
        previewFrame!.WritePixels(new Int32Rect(0,0,view.Width,view.Height),previewBits,view.Height * view.Width * 4,view.Width * 4);
        return previewFrame;
    }
    private void EnsurePreviewGdi(int width,int height)
    {
        if (previewMemDc == IntPtr.Zero) previewMemDc = CreateCompatibleDC(IntPtr.Zero);
        if (previewMemDc == IntPtr.Zero) throw new InvalidOperationException("No se pudo iniciar la vista previa.");
        if (previewDib != IntPtr.Zero && (width != previewW || height != previewH)) FreePreviewDib();
        if (previewDib == IntPtr.Zero)
        {
            var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            previewDib = CreateDIBSection(previewMemDc,ref header,0,out previewBits,IntPtr.Zero,0);
            if (previewDib == IntPtr.Zero || previewBits == IntPtr.Zero) throw new InvalidOperationException("No se pudo iniciar la vista previa.");
            previewOldObj = SelectObject(previewMemDc,previewDib);
            previewW = width; previewH = height;
            previewFrame = new WriteableBitmap(width,height,96,96,PixelFormats.Bgr32,null);
            if (previewImage != null) previewImage.Source = previewFrame;
        }
    }
    private void FreePreviewDib()
    {
        if (previewMemDc != IntPtr.Zero && previewDib != IntPtr.Zero) SelectObject(previewMemDc,previewOldObj);
        if (previewDib != IntPtr.Zero) DeleteObject(previewDib);
        previewDib = previewBits = previewOldObj = IntPtr.Zero; previewW = previewH = 0;
    }
    private void FreePreviewGdi()
    {
        FreePreviewDib();
        if (previewMemDc != IntPtr.Zero) DeleteDC(previewMemDc);
        previewMemDc = IntPtr.Zero;
    }
    private void FitPreview(CaptureBounds view,out double scale,out double ox,out double oy)
    {
        var w = Math.Max(1,previewImage?.ActualWidth ?? 1); var h = Math.Max(1,previewImage?.ActualHeight ?? 1);
        scale = Math.Min(w / Math.Max(1,(double)view.Width),h / Math.Max(1,(double)view.Height));
        ox = (w - view.Width * scale) / 2; oy = (h - view.Height * scale) / 2;
    }
    private void DrawBand(CaptureBounds? band,CaptureBounds view)
    {
        if (previewOverlay == null || regionBand == null || previewImage == null) return;
        if (band == null) { regionBand.Visibility = Visibility.Collapsed; return; }
        var b = band.Value;
        FitPreview(view,out var scale,out var ox,out var oy);
        Canvas.SetLeft(regionBand,ox + (b.X - view.X) * scale);
        Canvas.SetTop(regionBand,oy + (b.Y - view.Y) * scale);
        regionBand.Width = Math.Max(2,b.Width * scale); regionBand.Height = Math.Max(2,b.Height * scale);
        regionBand.Visibility = Visibility.Visible;
    }
    private void ShowPreviewNote(string text,bool error)
    {
        if (previewStatus is not { IsLoaded: true } status) return;
        status.Text = text; status.SetResourceReference(TextBlock.ForegroundProperty,error ? "Danger" : "Muted"); status.Visibility = Visibility.Visible;
    }
    private void PreviewDragDown(object sender,MouseButtonEventArgs e)
    {
        if (previewImage == null || previewOverlay == null || !TryPreviewView(out var view,out _,out _)) return;
        previewDragging = true; previewDragStart = e.GetPosition(previewImage); previewDragView = view;
        previewOverlay.CaptureMouse(); e.Handled = true;
    }
    private void PreviewDragMove(object sender,MouseEventArgs e)
    {
        if (!previewDragging || previewImage == null) return;
        DrawBand(DragRect(e.GetPosition(previewImage)),previewDragView);
        if (previewStatus is { IsLoaded: true } status) { status.Text = "Suelta para fijar la región."; status.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); status.Visibility = Visibility.Visible; }
        e.Handled = true;
    }
    private void PreviewDragUp(object sender,MouseButtonEventArgs e)
    {
        if (!previewDragging) return;
        previewDragging = false;
        previewOverlay?.ReleaseMouseCapture();
        e.Handled = true;
        if (previewImage == null) return;
        var rect = DragRect(e.GetPosition(previewImage));
        if (rect == null) { TickPreview(); return; }
        var r = rect.Value;
        var mode = (CaptureMode)(captureModes?.SelectedIndex ?? 0);
        int target = mode switch { CaptureMode.Window or CaptureMode.WindowRegion => 3, _ => 1 };
        if (target == 3)
        {
            var selected = captureWindow?.SelectedItem as CaptureWindow;
            if (selected == null || ClientOrigin((IntPtr)selected.Handle) is not { } origin) { ShowNotice("Selecciona una ventana visible."); return; }
            r = new(r.X - origin.X,r.Y - origin.Y,r.Width,r.Height);
        }
        if (r.Width < 32 || r.Height < 32) { ShowNotice("La región debe tener al menos 32 × 32 píxeles."); TickPreview(); return; }
        if (captureX != null) captureX.Text = r.X.ToString();
        if (captureY != null) captureY.Text = r.Y.ToString();
        if (captureWidth != null) captureWidth.Text = r.Width.ToString();
        if (captureHeight != null) captureHeight.Text = r.Height.ToString();
        if (captureModes != null && captureModes.SelectedIndex != target) captureModes.SelectedIndex = target;
        else SaveCaptureOptions();
        TickPreview();
    }
    private CaptureBounds? DragRect(Point current)
    {
        FitPreview(previewDragView,out var scale,out var ox,out var oy);
        if (scale <= 0) return null;
        var ax = previewDragView.X + (previewDragStart.X - ox) / scale;
        var ay = previewDragView.Y + (previewDragStart.Y - oy) / scale;
        var bx = previewDragView.X + (current.X - ox) / scale;
        var by = previewDragView.Y + (current.Y - oy) / scale;
        var x = (int)Math.Round(Math.Min(ax,bx)); var y = (int)Math.Round(Math.Min(ay,by));
        var w = (int)Math.Round(Math.Abs(bx - ax)); var h = (int)Math.Round(Math.Abs(by - ay));
        if (w < 4 || h < 4) return null;
        return new(x,y,w,h);
    }
}
