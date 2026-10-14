using System.Globalization;
using System.IO;
using System.Text.Json;
using ScreenStudio.Models;
namespace ScreenStudio.Services;

public sealed class MultitrackEngine(VideoEngine engine)
{
    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    public async Task<MediaAsset> InspectAsync(string path)
    {
        path = Path.GetFullPath(path);
        var isImage = new[] { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".webp", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant());
        using var process = VideoEngine.CreateProcess(engine.FfprobePath, ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,pix_fmt:stream_disposition=attached_pic", "-of", "json", path]);
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException("No se pudo importar el archivo. " + await errors);
        using var doc = JsonDocument.Parse(await output);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video" && (!s.TryGetProperty("disposition",out var d) || !d.TryGetProperty("attached_pic",out var pic) || pic.GetInt32() == 0));
        var hasAudio = streams.Any(s => s.GetProperty("codec_type").GetString() == "audio");
        var duration = isImage ? 5 : double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration < .04 || (video.ValueKind == JsonValueKind.Undefined && !hasAudio)) throw new InvalidOperationException("El archivo no contiene medios compatibles.");
        if (isImage) await engine.CheckImageAsync(path);
        return new MediaAsset { Path = path, Kind = isImage ? ClipKind.Image : video.ValueKind == JsonValueKind.Undefined ? ClipKind.Audio : ClipKind.Video, Duration = duration, HasAudio = hasAudio, HasAlpha = video.ValueKind != JsonValueKind.Undefined && video.TryGetProperty("pix_fmt",out var pix) && pix.GetString() is { } format && HasAlphaChannel(format), Cursor = await CursorTrace.LoadAsync(path),
            Width = video.ValueKind == JsonValueKind.Undefined ? 0 : video.GetProperty("width").GetInt32(), Height = video.ValueKind == JsonValueKind.Undefined ? 0 : video.GetProperty("height").GetInt32() };
    }

    public async Task ExportAsync(StudioProject project, string destination, IProgress<double> progress, CancellationToken cancellation)
    {
        project.Validate();
        var rangeEnd = project.ExportOut > 0 ? Math.Min(project.ExportOut,project.Duration) : project.Duration;
        if (project.ExportOut > 0 || project.ExportIn > 0) project = project.Slice(project.ExportIn,rangeEnd);
        if (project.Duration < .04) throw new InvalidOperationException("Añade clips antes de exportar.");
        destination = Path.GetFullPath(destination);
        if (project.Clips.Any(c => c.Kind != ClipKind.Text && string.Equals(Path.GetFullPath(c.SourcePath), destination, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("La exportación no puede reemplazar un original.");
        var scratch = Path.Combine(Path.GetDirectoryName(destination)!, ".studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var temporary = Path.Combine(scratch, "export." + project.Output.Format);
        try
        {
            var args = new List<string> { "-hide_banner", "-y", "-filter_complex_threads", "2" };
            if (project.GradientBackground)
            {
                var background = Path.Combine(scratch,"background.png"); await File.WriteAllBytesAsync(background,await TextArtwork.BackgroundAsync(project),cancellation);
                args.AddRange(["-loop","1","-framerate",project.Output.FrameRate.ToString(),"-i",background]);
            }
            else args.AddRange(["-f", "lavfi", "-i", $"color=c=0x{project.Background[1..]}:s={project.Width}x{project.Height}:r={project.Output.FrameRate}:d={N(project.Duration)}"]);
            var graph = new List<string>();
            var inputs = 1; var baseLabel = "0:v"; var audioLabels = new List<string>();
            var transitions=TransitionPlan.Create(project);
            bool HasEffects(TimelineClip c) => c.Effects.HasVisualEffects || c.OpacityKeys.Count > 0 || c.HasTransformKeys || transitions.GetValueOrDefault(c.Id) is { } span && (span.LeftPadding>0 || span.RightPadding>0) || c.Kind == ClipKind.Video && project.Library.Any(a => a.Path == c.SourcePath && a.Cursor != null);
            var effectCount = project.Tracks.Where(t => t.Enabled && t.Kind == TrackKind.Visual).SelectMany(t => t.Clips).Count(HasEffects); var effectIndex = 0;
            foreach (var track in project.Tracks.Where(t => t.Enabled && t.Kind == TrackKind.Visual).Reverse())
            foreach (var clip in track.Clips.OrderBy(c => c.Start))
            {
                cancellation.ThrowIfCancellationRequested();
                var source = clip.SourcePath;
                var timing=transitions.GetValueOrDefault(clip.Id); var start=timing?.Start ?? clip.Start; var end=timing?.End ?? clip.End; var duration=end-start;
                var speed = clip.Speed <= 0 ? 1 : clip.Speed;
                var sourceLen = duration / speed;
                if (clip.Kind == ClipKind.Text)
                {
                    source = Path.Combine(scratch, clip.Id.ToString("N") + ".png");
                    await File.WriteAllBytesAsync(source, await TextArtwork.RenderAsync(clip, project.Width, project.Height), cancellation);
                }
                var w = Math.Max(2, (int)((project.Width - 2 * project.Padding) * clip.Scale / 2) * 2);
                var h = Math.Max(2, (int)((project.Height - 2 * project.Padding) * clip.Scale / 2) * 2);
                var offset = clip.Kind == ClipKind.Video ? clip.SourceOffset : 0;
                var prepared = HasEffects(clip);
                if (prepared)
                {
                    var asset = project.Library.FirstOrDefault(a => a.Path == clip.SourcePath);
                    double sw = clip.Kind == ClipKind.Text ? project.Width : asset?.Width ?? w, sh = clip.Kind == ClipKind.Text ? project.Height : asset?.Height ?? h;
                    if (clip.HasTransformKeys) { w = Math.Max(2,(project.Width - 2 * project.Padding) / 2 * 2); h = Math.Max(2,(project.Height - 2 * project.Padding) / 2 * 2); }
                    else { var ratio = Math.Min(w/sw,h/sh); w = Math.Max(2,(int)(sw*ratio/2)*2); h = Math.Max(2,(int)(sh*ratio/2)*2); }
                    var renderClip = clip.Clone(); renderClip.SourcePath = source; if (clip.Kind == ClipKind.Text) renderClip.Kind = ClipKind.Image;
                    var processed = Path.Combine(scratch,"effects-"+clip.Id+".mkv"); var current = effectIndex++;
                    await new EffectsRenderer(engine).RenderAsync(renderClip,asset,project.Width,project.Height,w,h,project.Output.FrameRate,processed,new CallbackProgress(v => progress.Report(.6*(current+v)/effectCount)),cancellation,timing);
                    source = processed; offset = 0;
                }
                else if (clip.Kind is ClipKind.Image or ClipKind.Text) args.AddRange(["-loop", "1", "-framerate", project.Output.FrameRate.ToString(CultureInfo.InvariantCulture)]);
                args.AddRange(["-i", source]); var index = inputs++;
                var scale = prepared ? "" : $"scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,";
                var trimLen = prepared ? duration : sourceLen;
                var retime = !prepared && speed != 1 ? $"setpts=(PTS-STARTPTS)/{N(speed)}," : "";
                var place = clip.HasTransformKeys ? "x='(W-overlay_w)/2':y='(H-overlay_h)/2'" : $"x='{N(project.Width * clip.X)}-overlay_w/2':y='{N(project.Height * clip.Y)}-overlay_h/2'";
                graph.Add($"[{index}:v]trim=start={N(offset)}:duration={N(trimLen)},setpts=PTS-STARTPTS,{retime}{scale}setsar=1,format=rgba,colorchannelmixer=aa={N(clip.Opacity)},setpts=PTS+{N(start)}/TB[v{index}]");
                graph.Add($"[{baseLabel}][v{index}]overlay={place}:eof_action=pass:repeatlast=0:enable='gte(t,{N(start)})*lt(t,{N(end)})'[b{index}]");
                baseLabel = "b" + index;
            }
            foreach (var clip in project.Tracks.Where(t => t.Enabled && t.Kind == TrackKind.Audio).SelectMany(t => t.Clips))
            {
                args.AddRange(["-i", clip.SourcePath]); var index = inputs++;
                var label = "a" + index;
                var speed = clip.Speed <= 0 ? 1 : clip.Speed;
                var fadeIn = Math.Min(clip.FadeIn,clip.Duration); var fadeOut = Math.Min(clip.FadeOut,clip.Duration);
                var fades = (fadeIn > 0 ? $",afade=t=in:st=0:d={N(fadeIn)}:curve=tri" : fadeIn == 0 && clip.Duration > .01 ? ",afade=t=in:st=0:d=0.005:curve=tri" : "") + (fadeOut > 0 ? $",afade=t=out:st={N(Math.Max(0,clip.Duration - fadeOut))}:d={N(fadeOut)}:curve=tri" : fadeOut == 0 && clip.Duration > .01 ? $",afade=t=out:st={N(Math.Max(0,clip.Duration - .005))}:d=0.005:curve=tri" : "");
                graph.Add($"[{index}:a]atrim=start={N(clip.SourceOffset)}:duration={N(clip.Duration / speed)},asetpts=PTS-STARTPTS{(speed == 1 ? "" : "," + Tempo(speed))},aresample=48000,aformat=channel_layouts=stereo,{VolumeFilter(clip)}{fades},adelay={Math.Round(clip.Start * 1000)}:all=1[{label}]");
                audioLabels.Add("[" + label + "]");
            }
            graph.Add($"[{baseLabel}]format=yuv420p[vout]");
            if (audioLabels.Count > 0) graph.Add(string.Concat(audioLabels) + $"amix=inputs={audioLabels.Count}:duration=longest:normalize=0,alimiter=limit=0.95:level=false,apad,atrim=duration={N(project.Duration)}[aout]");
            args.AddRange(["-filter_complex", string.Join(';', graph), "-map", "[vout]"]);
            if (audioLabels.Count > 0) args.AddRange(["-map", "[aout]", "-c:a", project.Output.AudioEncoder]);
            args.AddRange(["-c:v", project.Output.VideoEncoder]);
            if (project.Output.VideoEncoder is "libx264" or "libx265") args.AddRange(["-preset", "fast", "-crf", "20"]);
            else args.AddRange(["-crf", "30", "-b:v", "0", "-deadline", "good", "-cpu-used", "4"]);
            if (project.Output.Format is "mp4" or "mov") args.AddRange(["-movflags", "+faststart"]);
            args.AddRange(["-r", project.Output.FrameRate.ToString(CultureInfo.InvariantCulture), "-t", N(project.Duration), "-progress", "pipe:1", "-nostats", temporary]);
            using var process = VideoEngine.CreateProcess(engine.FfmpegPath, args);
            cancellation.ThrowIfCancellationRequested(); process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            var reading = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                    if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var value)) { var ratio = Math.Clamp(value / 1_000_000d / project.Duration, 0, 1); progress.Report(effectCount > 0 ? .6 + .4*ratio : ratio); }
            });
            try { await process.WaitForExitAsync(cancellation); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await reading; await errors; throw; }
            await reading;
            if (process.ExitCode != 0) throw new InvalidOperationException("No se pudo exportar. " + await errors);
            cancellation.ThrowIfCancellationRequested();
            if (project.Output.Stabilize) { progress.Report(.95); await StabilizeAsync(temporary,scratch,project.Output,cancellation); }
            if (project.Output.Subtitles)
            {
                var script = Subtitles.Render(Subtitles.FromProject(project));
                if (!string.IsNullOrWhiteSpace(script)) await File.WriteAllTextAsync(Path.ChangeExtension(destination,".srt"),script,cancellation);
            }
            File.Move(temporary, destination, true); progress.Report(1);
        }
        finally { Directory.Delete(scratch, true); }
    }
    private static bool HasAlphaChannel(string format) =>
        format.StartsWith("rgba") || format.StartsWith("bgra") || format.StartsWith("argb") || format.StartsWith("abgr") ||
        format.StartsWith("ya") || format.Contains("yuva");
    public static string Tempo(double speed)
    {
        if (speed >= 2) return $"atempo=2,atempo={N(speed / 2)}";
        if (speed >= 0.5) return $"atempo={N(speed)}";
        return $"atempo=0.5,atempo={N(speed / 0.5)}";
    }
    // Volumen con keyframes: expresión evaluada por fotograma (los keys definen el volumen absoluto).
    public static string VolumeFilter(TimelineClip clip)
    {
        if (clip.VolumeKeys.Count == 0) return $"volume={N(clip.Volume)}";
        var keys = clip.VolumeKeys.OrderBy(k => k.Time).ToArray();
        var expression = N(keys[^1].Value);
        for (var i = keys.Length - 1; i >= 1; i--)
        {
            var a = keys[i-1]; var b = keys[i];
            var span = b.Time - a.Time;
            var segment = span <= 0 ? N(b.Value) : $"{N(a.Value)}+({N(b.Value)}-{N(a.Value)})*(t-{N(a.Time)})/{N(span)}";
            expression = $"if(lt(t,{N(b.Time)}),{segment},{expression})";
        }
        expression = $"if(lt(t,{N(keys[0].Time)}),{N(keys[0].Value)},{expression})";
        return $"volume='{expression}':eval=frame";
    }
    // Estabilización en dos pasadas (vidstab) sobre la mezcla final.
    private async Task StabilizeAsync(string input,string scratch,OutputSettings output,CancellationToken cancellation)
    {
        static string FilterPath(string path) => "'" + path.Replace("\\","/").Replace(":","\\:") + "'";
        var transforms = FilterPath(Path.Combine(scratch,"stabilize.trf"));
        var steadied = Path.Combine(scratch,"stabilized." + output.Format);
        await RunFilterAsync(["-y","-i",input,"-vf",$"vidstabdetect=shakiness=5:accuracy=15:result={transforms}", "-f","null","-"],"Estabilizar requiere un FFmpeg con vidstab.",cancellation);
        var videoArgs = new List<string> { "-y","-i",input,"-vf","vidstabtransform=input=" + transforms + ":smoothing=30","-c:a","copy","-c:v",output.VideoEncoder };
        if (output.VideoEncoder is "libx264" or "libx265") videoArgs.AddRange(["-preset","fast","-crf","20"]);
        else videoArgs.AddRange(["-crf","30","-b:v","0","-deadline","good","-cpu-used","4"]);
        if (output.Format is "mp4" or "mov") videoArgs.AddRange(["-movflags","+faststart"]);
        videoArgs.Add(steadied);
        await RunFilterAsync(videoArgs,"No se pudo estabilizar.",cancellation);
        File.Move(steadied,input,true);
    }
    private async Task RunFilterAsync(IEnumerable<string> args,string missingFilter,CancellationToken cancellation)
    {
        using var process = VideoEngine.CreateProcess(engine.FfmpegPath,args);
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        var message = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException((message.Contains("No such filter") ? missingFilter + " " : "No se pudo estabilizar. ") + message);
    }
    private sealed class CallbackProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
