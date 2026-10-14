using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ScreenStudio.Models;

namespace ScreenStudio.Services;

public record VideoInfo(double Duration, int Width, int Height);

public sealed class VideoEngine
{
    public string FfmpegPath { get; }
    public string FfprobePath { get; }
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public VideoEngine()
    {
        FfmpegPath = FindTool("ffmpeg.exe");
        FfprobePath = FindTool("ffprobe.exe", Path.GetDirectoryName(FfmpegPath));
    }

    private static string FindTool(string name, string? preferredDirectory = null)
    {
        var directories = new[] { preferredDirectory, Path.Combine(AppContext.BaseDirectory, "tools"), AppContext.BaseDirectory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var path = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException($"Falta {name}. Instala FFmpeg o coloca ffmpeg.exe y ffprobe.exe en la carpeta tools junto a la aplicación.");
    }

    internal static Process CreateProcess(string executable, IEnumerable<string> arguments, bool input = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = input
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new Process { StartInfo = start };
    }

    public async Task<VideoInfo> ProbeAsync(string path)
    {
        using var process = CreateProcess(FfprobePath, ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "json", path]);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException("No se pudo abrir el vídeo. " + await errors);
        using var document = JsonDocument.Parse(await output);
        var root = document.RootElement;
        var stream = root.GetProperty("streams").EnumerateArray().FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
        if (stream.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("El archivo no contiene vídeo.");
        var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidOperationException("El vídeo no tiene una duración válida.");
        return new VideoInfo(duration, stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32());
    }

    public static string BuildFilter(StudioProject project)
    {
        var innerWidth = project.Width - project.Padding * 2;
        var innerHeight = project.Height - project.Padding * 2;
        return $"scale={innerWidth}:{innerHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2," +
               $"pad={project.Width}:{project.Height}:(ow-iw)/2:(oh-ih)/2:color=0x{project.Background[1..]},setsar=1";
    }

    public async Task<(byte[] Bytes, string Error, int Exit)> ReadPosterAsync(string path, double position)
    {
        using var process = CreateProcess(FfmpegPath, ["-v", "error", "-ss", Number(position), "-i", path,
            "-frames:v", "1", "-vf", "scale=1280:-2", "-f", "image2pipe", "-c:v", "png", "pipe:1"]);
        process.Start();
        using var bytes = new MemoryStream();
        var output = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); await output; await errors;
            return ([], "tarda demasiado en abrirse (¿red o disco lento?)", -1);
        }
        await output;
        var error = await errors;
        if (process.ExitCode == 0 && bytes.Length > 0) return (bytes.ToArray(), "", 0);
        var detail = error.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        if (detail.Length > 160) detail = detail[..160] + "…";
        if (string.IsNullOrWhiteSpace(detail)) detail = $"sin mensaje de error (código {process.ExitCode})";
        return ([], detail, process.ExitCode);
    }

    // Decodifica un fotograma para verificar que la imagen no esté dañada o incompleta.
    public async Task CheckImageAsync(string path)
    {
        using var process = CreateProcess(FfmpegPath,["-v","error","-i",path,"-frames:v","1","-f","null","-"]);
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw new InvalidOperationException("La imagen tarda demasiado en abrirse. Cópiala a tu equipo e inténtalo de nuevo."); }
        await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException("La imagen está dañada o incompleta y no se puede usar. Descárgala de nuevo.");
    }

    public async Task ExportAsync(StudioProject project, double duration, string destination, IProgress<double> progress, CancellationToken cancellation)
    {
        project.Validate(duration);
        if (string.Equals(Path.GetFullPath(project.SourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El destino debe ser diferente del vídeo original.");
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".screenstudio-{Guid.NewGuid():N}.mp4");
        var length = project.EndSeconds - project.StartSeconds;
        using var process = CreateProcess(FfmpegPath, ["-hide_banner", "-y", "-ss", Number(project.StartSeconds), "-i", project.SourcePath,
            "-t", Number(length), "-map", "0:v:0", "-map", "0:a?", "-vf", BuildFilter(project),
            "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
            "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", temporary]);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            var output = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                    if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var micros))
                        progress.Report(Math.Clamp(micros / 1_000_000d / length, 0, 1));
            });
            try { await process.WaitForExitAsync(cancellation); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
                await output; await errors;
                throw;
            }
            await output;
            if (process.ExitCode != 0) throw new InvalidOperationException("La exportación falló. " + await errors);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
            progress.Report(1);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
