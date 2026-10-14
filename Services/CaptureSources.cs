using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenStudio.Models;
namespace ScreenStudio.Services;

public sealed record AudioDevice(string Id,string Name);
public sealed record CapturedMedia(string Path,double Start,string Name,bool Camera);
public sealed class CaptureSources(VideoEngine engine) : IDisposable
{
    private sealed class AudioSession : IDisposable
    {
        public required WasapiCapture Capture;
        public required WaveFileWriter Writer;
        public required MMDevice Device;
        public WasapiOut? Silence;
        public required CapturedMedia Media;
        public readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Error;
        public void Dispose() { Silence?.Stop(); Silence?.Dispose(); Capture.Dispose(); Writer.Dispose(); Device.Dispose(); }
    }
    private readonly List<AudioSession> audio = [];
    private Process? camera;
    private Task<string>? cameraError;
    private Task? cameraProgress;
    private CapturedMedia? cameraMedia;
    public static List<AudioDevice> AudioDevices(bool input)
    {
        using var enumerator = new MMDeviceEnumerator(); var result = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(input ? DataFlow.Capture : DataFlow.Render,DeviceState.Active))
        { using (device) result.Add(new(device.ID,device.FriendlyName)); }
        return result;
    }
    public async Task<List<string>> CamerasAsync()
    {
        using var p = VideoEngine.CreateProcess(engine.FfmpegPath,["-hide_banner","-list_devices","true","-f","dshow","-i","dummy"]);
        p.Start(); var error = p.StandardError.ReadToEndAsync(); var output = p.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await p.WaitForExitAsync(timeout.Token); } catch { if (!p.HasExited) p.Kill(true); await p.WaitForExitAsync(); throw; }
        await output;
        return Regex.Matches(await error,"\"([^\"]+)\" \\(video\\)").Select(m => m.Groups[1].Value).Distinct().ToList();
    }
    public async Task StartAsync(CaptureOptions options,string screenPath,Func<double> clock,int fps,CancellationToken cancellation)
    {
        try
        {
            if (options.Microphone) StartAudio(options.MicrophoneId,true,screenPath+".mic.wav","Micrófono",clock);
            if (options.SystemAudio) StartAudio(options.OutputDeviceId,false,screenPath+".system.wav","Audio del sistema",clock);
            if (!string.IsNullOrWhiteSpace(options.CameraDevice))
            {
                cancellation.ThrowIfCancellationRequested(); var path = screenPath+".camera.mp4";
                camera = VideoEngine.CreateProcess(engine.FfmpegPath,["-hide_banner","-y","-f","dshow","-rtbufsize","256M","-i","video="+options.CameraDevice,"-an","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2","-r",fps.ToString(),"-c:v","libx264","-preset","ultrafast","-crf","18","-pix_fmt","yuv420p","-movflags","+faststart","-progress","pipe:1","-stats_period","0.2","-nostats",path],true);
                camera.Start(); cameraError = camera.StandardError.ReadToEndAsync(); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var active = camera;
                cameraProgress = Task.Run(async () => { while (await active.StandardOutput.ReadLineAsync() is { } line) if (line.StartsWith("frame=") && long.TryParse(line.AsSpan(6),out var f) && f > 0) { cameraMedia ??= new CapturedMedia(path,Math.Max(0,clock()-f/(double)fps),"Cámara",true); ready.TrySetResult(); } });
                var exited = active.WaitForExitAsync(); var first = await Task.WhenAny(ready.Task,exited).WaitAsync(TimeSpan.FromSeconds(15),cancellation);
                if (first == exited) throw new InvalidOperationException("No se pudo iniciar la cámara: " + await cameraError);
            }
        }
        catch { await AbortAsync(); throw; }
    }
    private void StartAudio(string? id,bool microphone,string path,string name,Func<double> clock)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrEmpty(id) ? enumerator.GetDefaultAudioEndpoint(microphone ? DataFlow.Capture : DataFlow.Render,Role.Multimedia) : enumerator.GetDevice(id);
        WasapiCapture capture = microphone ? new WasapiCapture(device,true,30) : new WasapiLoopbackCapture(device);
        var session = new AudioSession { Device=device, Capture=capture, Writer=new WaveFileWriter(path,capture.WaveFormat), Media=new CapturedMedia(path,clock(),name,false) };
        audio.Add(session);
        capture.DataAvailable += (_,e) => { try { session.Writer.Write(e.Buffer,0,e.BytesRecorded); } catch (Exception error) { session.Error = error; capture.StopRecording(); } };
        capture.RecordingStopped += (_,e) => { session.Error ??= e.Exception; session.Stopped.TrySetResult(); };
        if (!microphone)
        {
            // Keep the render endpoint clock alive: loopback must retain silent intervals.
            session.Silence = new WasapiOut(device,AudioClientShareMode.Shared,false,30); session.Silence.Init(new SilenceProvider(capture.WaveFormat)); session.Silence.Play();
        }
        capture.StartRecording();
    }
    public async Task<List<CapturedMedia>> StopAsync()
    {
        var result = new List<CapturedMedia>(); var failures = new List<string>();
        foreach (var session in audio)
        {
            try { session.Capture.StopRecording(); await session.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); if (session.Error != null) failures.Add(session.Media.Name+": "+session.Error.Message); else result.Add(session.Media); }
            catch (Exception e) { failures.Add(e.Message); }
            finally { session.Dispose(); }
        }
        audio.Clear();
        if (camera != null)
        {
            try
            {
                if (!camera.HasExited) { await camera.StandardInput.WriteLineAsync("q"); await camera.StandardInput.FlushAsync(); await camera.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                if (cameraProgress != null) await cameraProgress;
                if (camera.ExitCode != 0) failures.Add("Cámara: "+(cameraError == null ? "Grabación interrumpida" : await cameraError));
                else if (cameraMedia != null) result.Add(cameraMedia);
            }
            catch (Exception e) { failures.Add(e.Message); }
            finally { if (!camera.HasExited) { camera.Kill(true); await camera.WaitForExitAsync(); } camera.Dispose(); camera = null; }
        }
        if (failures.Count > 0) LastError = string.Join("\n",failures);
        return result;
    }
    public string? LastError { get; private set; }
    private async Task AbortAsync() { await StopAsync(); }
    public void Dispose()
    {
        foreach (var s in audio) { s.Capture.StopRecording(); s.Stopped.Task.Wait(TimeSpan.FromSeconds(3)); s.Dispose(); } audio.Clear();
        if (camera != null) { if (!camera.HasExited) camera.Kill(true); camera.Dispose(); camera = null; }
    }
}
