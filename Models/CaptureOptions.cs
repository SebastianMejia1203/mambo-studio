using System.Runtime.InteropServices;
using System.Text;
namespace ScreenStudio.Models;
public enum CaptureMode { Screen, Region, Window, WindowRegion, TwoScreens }
public enum ScreenLayout { SideBySide, Stacked, Separate }
public sealed class CaptureOptions
{
    public CaptureMode Mode { get; set; }
    public string? MonitorDevice { get; set; }
    public string? SecondMonitorDevice { get; set; }
    public ScreenLayout ScreenLayout { get; set; }
    public bool ReverseScreens { get; set; }
    public long WindowHandle { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Microphone { get; set; }
    public string? MicrophoneId { get; set; }
    public bool SystemAudio { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? CameraDevice { get; set; }
    public bool TrackCursor { get; set; } = true;
    public bool AutoZoom { get; set; } = true;
}
public readonly record struct CaptureBounds(int X,int Y,int Width,int Height);
public sealed record CaptureWindow(long Handle,string Title);
public sealed record CaptureMonitor(string Device,string Name,CaptureBounds Bounds,bool Primary);
public static class CaptureTargets
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr data);
    private delegate bool EnumProc(IntPtr handle,IntPtr data);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MONITORINFOEX
    {
        public int Size; public RECT Monitor,Work; public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device;
    }
    private delegate bool MonitorProc(IntPtr monitor,IntPtr dc,ref RECT bounds,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorProc callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle,out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr handle,ref POINT point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle,StringBuilder text,int max);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    public static List<CaptureWindow> Windows()
    {
        var result = new List<CaptureWindow>(); EnumWindows((h,_) => { if (!IsWindowVisible(h) || IsIconic(h)) return true; var title = new StringBuilder(512); GetWindowText(h,title,512); if (title.Length > 0) result.Add(new(h.ToInt64(),title.ToString())); return true; },IntPtr.Zero); return result;
    }
    public static List<CaptureMonitor> Monitors()
    {
        var result=new List<CaptureMonitor>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr handle,IntPtr dc,ref RECT rect,IntPtr data)=>
        {
            var info=new MONITORINFOEX { Size=Marshal.SizeOf<MONITORINFOEX>(),Device="" };
            if(GetMonitorInfo(handle,ref info)) result.Add(new(info.Device,"",new(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top),(info.Flags&1)!=0));
            return true;
        },IntPtr.Zero);
        return result.OrderByDescending(m=>m.Primary).ThenBy(m=>m.Device).Select((m,i)=>m with { Name=$"Pantalla {i+1} · {m.Bounds.Width} × {m.Bounds.Height}"+(m.Primary?" · principal":"") }).ToList();
    }
    public static CaptureMonitor Monitor(string? device)
    {
        var monitors=Monitors();
        return string.IsNullOrEmpty(device)?monitors.FirstOrDefault(m=>m.Primary) ?? throw new InvalidOperationException("No hay pantallas disponibles."):
            monitors.FirstOrDefault(m=>m.Device==device) ?? throw new InvalidOperationException("La pantalla seleccionada ya no está conectada. Actualiza las fuentes de captura.");
    }
    public static CaptureBounds Bounds(CaptureOptions options)
    {
        if(!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.ScreenLayout)) throw new InvalidOperationException("Origen de captura inválido.");
        if (options.Mode is CaptureMode.Screen or CaptureMode.TwoScreens)
        {
            var first=Monitor(options.MonitorDevice);
            if(options.Mode==CaptureMode.TwoScreens)
            {
                if(string.IsNullOrEmpty(options.SecondMonitorDevice)) throw new InvalidOperationException("Selecciona la segunda pantalla.");
                if(Monitor(options.SecondMonitorDevice).Device==first.Device) throw new InvalidOperationException("Selecciona dos pantallas diferentes.");
            }
            return first.Bounds;
        }
        if (options.Mode is CaptureMode.Window or CaptureMode.WindowRegion)
        {
            var h = (IntPtr)options.WindowHandle;
            if (!IsWindowVisible(h) || IsIconic(h) || !GetClientRect(h,out var rect)) throw new InvalidOperationException("Selecciona una ventana visible y sin minimizar.");
            var point = new POINT(); ClientToScreen(h,ref point); var width = rect.Right-rect.Left; var height = rect.Bottom-rect.Top;
            if (options.Mode == CaptureMode.Window) return new(point.X,point.Y,width,height);
            if (options.X < 0 || options.Y < 0 || options.X+options.Width > width || options.Y+options.Height > height) throw new InvalidOperationException("La región debe quedar dentro del contenido de la ventana.");
            ValidateSize(options.Width,options.Height); return new(point.X+options.X,point.Y+options.Y,options.Width,options.Height);
        }
        ValidateSize(options.Width,options.Height);
        var virtualX = GetSystemMetrics(76); var virtualY = GetSystemMetrics(77);
        if (options.X < virtualX || options.Y < virtualY || options.X+options.Width > virtualX+GetSystemMetrics(78) || options.Y+options.Height > virtualY+GetSystemMetrics(79)) throw new InvalidOperationException("La región está fuera del escritorio.");
        return new(options.X,options.Y,options.Width,options.Height);
    }
    private static void ValidateSize(int width,int height) { if (width < 32 || height < 32 || width > 16384 || height > 16384) throw new InvalidOperationException("La región debe tener al menos 32 × 32 píxeles."); }
    public static List<string> InputArguments(CaptureOptions options,int fps)
    {
        var bounds = Bounds(options); var window = options.Mode is CaptureMode.Window or CaptureMode.WindowRegion;
        var x = window ? options.Mode == CaptureMode.WindowRegion ? options.X : 0 : bounds.X;
        var y = window ? options.Mode == CaptureMode.WindowRegion ? options.Y : 0 : bounds.Y;
        return ["-f","gdigrab","-framerate",fps.ToString(),"-draw_mouse","0","-offset_x",x.ToString(),"-offset_y",y.ToString(),"-video_size",$"{bounds.Width}x{bounds.Height}","-i",window ? "hwnd="+options.WindowHandle : "desktop"];
    }
}

