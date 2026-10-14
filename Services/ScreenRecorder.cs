using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ScreenStudio.Models;

namespace ScreenStudio.Services;

public sealed class ScreenRecorder(VideoEngine engine) : IDisposable
{
    private Process? process;
    private Task<string>? errors;
    private Task? output;
    private bool processStarted;
    private long encodedMicroseconds;
    private readonly Stopwatch captureClock = new();
    private CursorRecorder? cursor;
    private CaptureBounds captureBounds;
    private double originLead;
    private int captureFps;
    private bool showCursor;
    private long startedTimestamp;
    public long OriginTimestamp=>startedTimestamp+(long)(originLead*Stopwatch.Frequency);
    public double SessionTime => Math.Max(0,captureClock.Elapsed.TotalSeconds-originLead);
    public string? OutputPath { get; private set; }
    public bool IsRecording => process is { HasExited: false };
    public TimeSpan EncodedDuration => TimeSpan.FromMicroseconds(Math.Max(0, Interlocked.Read(ref encodedMicroseconds)));

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    public async Task StartAsync(bool includeCursor, string? outputDirectory = null, CancellationToken cancellation = default, int frameRate = 30, CaptureOptions? options = null)
    {
        if (process != null) throw new InvalidOperationException("Ya hay una grabación activa.");
        cancellation.ThrowIfCancellationRequested();
        if (frameRate is not (24 or 25 or 30 or 50 or 60)) throw new InvalidOperationException("Fotogramas no compatibles.");
        Interlocked.Exchange(ref encodedMicroseconds, 0);
        var directory = outputDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenStudio", "Recordings");
        Directory.CreateDirectory(directory);
        OutputPath = Path.Combine(directory, $"Captura-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.mp4");
        // Physical pixels of the primary display; per-monitor DPI awareness is declared in the manifest.
        options ??= new(); captureBounds = CaptureTargets.Bounds(options); captureFps = frameRate; showCursor = includeCursor; originLead = 0;
        var args = new List<string> { "-hide_banner","-y" }; args.AddRange(CaptureTargets.InputArguments(options,frameRate));
        args.AddRange(["-an", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-progress", "pipe:1", "-stats_period", "0.2", "-nostats", OutputPath]);
        process = VideoEngine.CreateProcess(engine.FfmpegPath,args,true);
        try
        {
            startedTimestamp=Stopwatch.GetTimestamp(); captureClock.Restart(); cursor = new CursorRecorder(options,() => captureClock.Elapsed.TotalSeconds);
            process.Start();
            processStarted = true;
            errors = process.StandardError.ReadToEndAsync();
            var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output = ReadProgressAsync(process, firstFrame);
            var exit = process.WaitForExitAsync();
            var first = await Task.WhenAny(firstFrame.Task, exit).WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            if (first == exit || process.HasExited) throw new InvalidOperationException("No se pudo iniciar la grabación. " + await errors);
            await firstFrame.Task;
            cancellation.ThrowIfCancellationRequested();
        }
        catch
        {
            if (processStarted && process != null)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
            }
            if (output != null) await output;
            if (errors != null) await errors;
            Dispose();
            // Failed/cancelled startup has not produced a usable recording; the path is unique to this attempt.
            if (OutputPath != null && File.Exists(OutputPath)) File.Delete(OutputPath);
            throw;
        }
    }

    private async Task ReadProgressAsync(Process active, TaskCompletionSource firstFrame)
    {
        var reader = active.StandardOutput;
        var calibrated = false; long frames = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("frame=") && long.TryParse(line.AsSpan(6), out var frame) && frame > 0)
            { frames = frame; }
            if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var micros))
                Interlocked.Exchange(ref encodedMicroseconds, Math.Max(0, micros));
            if (!calibrated && frames > 0 && line.StartsWith("progress=")) { originLead = Math.Max(0,captureClock.Elapsed.TotalSeconds-Math.Max(EncodedDuration.TotalSeconds,frames/(double)captureFps)); calibrated = true; firstFrame.TrySetResult(); }
        }
    }

    public async Task<string> StopAsync()
    {
        var active = process ?? throw new InvalidOperationException("No hay una grabación activa.");
        try
        {
            if (!active.HasExited)
            {
                await active.StandardInput.WriteLineAsync("q");
                await active.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await active.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    active.Kill(true);
                    await active.WaitForExitAsync();
                    throw new InvalidOperationException("La grabación no pudo finalizar a tiempo. El archivo puede estar incompleto.");
                }
            }
            var diagnostic = errors == null ? "" : await errors;
            if (output != null) await output;
            if (active.ExitCode != 0) throw new InvalidOperationException("La grabación falló. " + diagnostic);
            if (OutputPath == null || !File.Exists(OutputPath)) throw new InvalidOperationException("No se generó ningún vídeo.");
            if (cursor != null)
            {
                var info = await engine.ProbeAsync(OutputPath);
                var trace = await cursor.StopAsync(originLead,info.Duration,captureBounds,showCursor); await trace.SaveAsync(OutputPath); cursor.Dispose(); cursor = null;
            }
            return OutputPath;
        }
        finally { cursor?.Dispose(); cursor = null; captureClock.Stop(); active.Dispose(); process = null; processStarted = false; }
    }

    public void Dispose()
    {
        cursor?.Dispose(); cursor = null; captureClock.Stop();
        if (process != null)
        {
            if (processStarted && !process.HasExited) process.Kill(true);
            process.Dispose(); process = null; processStarted = false;
        }
    }
}


