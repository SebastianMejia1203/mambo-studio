using System.Runtime.InteropServices;
using ScreenStudio.Models;
namespace ScreenStudio.Services;
public sealed class CursorRecorder : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int Size; public int Flags; public IntPtr Handle; public CaptureTargets.POINT Position; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<CursorSample> samples = [];
    private readonly Task sampling;
    public CursorRecorder(CaptureOptions options,Func<double> clock)
    {
        sampling = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16)); bool left = false,right = false;
            try
            {
                do
                {
                    var cursor = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
                    if (!GetCursorInfo(ref cursor)) continue;
                    CaptureBounds bounds; try { bounds = CaptureTargets.Bounds(options); } catch { continue; }
                    var l = (GetAsyncKeyState(1)&0x8000) != 0; var r = (GetAsyncKeyState(2)&0x8000) != 0;
                    var x = (cursor.Position.X-bounds.X)/(double)bounds.Width; var y = (cursor.Position.Y-bounds.Y)/(double)bounds.Height;
                    var visible = cursor.Flags == 1 && x >= 0 && x <= 1 && y >= 0 && y <= 1;
                    samples.Add(new CursorSample { Time = clock(), X = Math.Clamp(x,0,1), Y = Math.Clamp(y,0,1), Visible = visible, Click = visible ? l && !left ? 1 : r && !right ? 2 : 0 : 0 }); left = l; right = r;
                } while (await timer.WaitForNextTickAsync(cancellation.Token));
            }
            catch (OperationCanceledException) { }
        });
    }
    public async Task<CursorTrace> StopAsync(double lead,double duration,CaptureBounds bounds,bool show)
    {
        cancellation.Cancel(); await sampling;
        var result = new CursorTrace { Width = bounds.Width, Height = bounds.Height, Duration = duration, ShowCursor = show };
        foreach (var s in samples.Where(s => s.Time >= lead && s.Time <= lead+duration)) result.Samples.Add(new CursorSample { Time = Math.Max(0,s.Time-lead),X=s.X,Y=s.Y,Visible=s.Visible,Click=s.Click });
        if (result.Samples.Count > 0 && result.Samples[0].Time > 0) { var first = result.Samples[0]; result.Samples.Insert(0,new CursorSample { X=first.X,Y=first.Y,Visible=first.Visible }); }
        return result;
    }
    public void Dispose() { cancellation.Cancel(); sampling.GetAwaiter().GetResult(); cancellation.Dispose(); }
}
