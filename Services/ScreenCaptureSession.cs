using System.Diagnostics;
using System.Text.Json;
using ScreenStudio.Models;
namespace ScreenStudio.Services;

public sealed record CapturedScreen(string Path,double Start,int Index,string Name);
public sealed record ScreenCaptureSource(CaptureOptions Options,string Name);
public sealed class ScreenCaptureSession(VideoEngine engine) : IDisposable
{
    private readonly List<(ScreenRecorder Recorder,string Name)> recorders=[];
    private long origin;
    public List<CapturedScreen> Screens { get; private set; }=[];
    public string? OutputPath=>recorders.FirstOrDefault().Recorder?.OutputPath;
    public bool IsRecording=>recorders.Count>0 && recorders.All(r=>r.Recorder.IsRecording);
    public double SessionTime=>origin==0?0:Math.Max(0,(Stopwatch.GetTimestamp()-origin)/(double)Stopwatch.Frequency);
    public TimeSpan EncodedDuration=>recorders.FirstOrDefault().Recorder?.EncodedDuration ?? TimeSpan.Zero;
    public async Task StartAsync(bool includeCursor,string? outputDirectory=null,CancellationToken cancellation=default,int frameRate=30,CaptureOptions? options=null)
    {
        if(recorders.Count>0) throw new InvalidOperationException("Ya hay una sesión de captura activa.");
        options ??= new(); CaptureTargets.Bounds(options); Screens=[];
        var sources=new List<ScreenCaptureSource>();
        var first=JsonSerializer.Deserialize<CaptureOptions>(JsonSerializer.Serialize(options))!;
        if(options.Mode==CaptureMode.TwoScreens) first.Mode=CaptureMode.Screen;
        sources.Add(new(first,options.Mode is CaptureMode.Screen or CaptureMode.TwoScreens ? CaptureTargets.Monitor(options.MonitorDevice).Name : "Captura"));
        if(options.Mode==CaptureMode.TwoScreens)
        {
            var second=JsonSerializer.Deserialize<CaptureOptions>(JsonSerializer.Serialize(first))!; second.MonitorDevice=options.SecondMonitorDevice;
            sources.Add(new(second,CaptureTargets.Monitor(second.MonitorDevice).Name));
        }
        await StartSourcesAsync(sources,includeCursor,outputDirectory,cancellation,frameRate);
    }
    public async Task StartSourcesAsync(IReadOnlyList<ScreenCaptureSource> sources,bool includeCursor,string? outputDirectory=null,CancellationToken cancellation=default,int frameRate=30)
    {
        if(recorders.Count>0) throw new InvalidOperationException("Ya hay una sesión de captura activa.");
        if(sources.Count is <1 or >2) throw new InvalidOperationException("Selecciona una o dos fuentes.");
        foreach(var source in sources) CaptureTargets.Bounds(source.Options);
        Screens=[];
        foreach(var source in sources) recorders.Add((new ScreenRecorder(engine),source.Name));
        try
        {
            await Task.WhenAll(sources.Select((s,i)=>recorders[i].Recorder.StartAsync(includeCursor,outputDirectory,cancellation,frameRate,s.Options)));
            origin=recorders.Min(r=>r.Recorder.OriginTimestamp);
        }
        catch { Dispose(); throw; }
    }
    public async Task<string> StopAsync()
    {
        if(recorders.Count==0) throw new InvalidOperationException("No hay una captura activa.");
        try
        {
            var paths=await Task.WhenAll(recorders.Select(r=>r.Recorder.StopAsync()));
            Screens=paths.Select((path,i)=>new CapturedScreen(path,Math.Max(0,(recorders[i].Recorder.OriginTimestamp-origin)/(double)Stopwatch.Frequency),i,recorders[i].Name)).ToList();
            return paths[0];
        }
        finally { foreach(var entry in recorders) entry.Recorder.Dispose(); recorders.Clear(); }
    }
    public void Dispose() { foreach(var entry in recorders) entry.Recorder.Dispose(); recorders.Clear(); }
}
