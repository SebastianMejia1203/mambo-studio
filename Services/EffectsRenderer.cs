using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio.Models;
using ScreenStudio.UI;
namespace ScreenStudio.Services;

// The same WPF drawing path is used in the preview and the lossless export intermediate.
public sealed class EffectsRenderer(VideoEngine engine)
{
    public Task RenderAsync(TimelineClip clip, MediaAsset? asset,int canvasW,int canvasH,int width,int height,int fps,string output,IProgress<double> progress,CancellationToken cancellation,TransitionSpan? transition=null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { Render(clip,asset,canvasW,canvasH,width,height,fps,output,progress,cancellation,transition); completion.SetResult(); }
            catch (OperationCanceledException) { completion.SetCanceled(cancellation); }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); return completion.Task;
    }
    private void Render(TimelineClip clip,MediaAsset? asset,int canvasW,int canvasH,int width,int height,int fps,string output,IProgress<double> progress,CancellationToken cancellation,TransitionSpan? transition)
    {
        string N(double v) => v.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture);
        int bleed = FramePainter.Bleed(clip.Effects), outW = width+2*bleed, outH = height+2*bleed;
        int decodeW = Math.Max(width,Math.Min(3840,asset?.Width ?? width));
        int decodeH = Math.Max(2,(int)Math.Round(decodeW * height/(double)width / 2)*2);
        var speed = clip.Speed <= 0 ? 1 : clip.Speed;
        var sourceLen = clip.Duration / speed;
        var midRate = Math.Max(fps,(int)Math.Ceiling(fps * speed));
        var decoderArgs = new List<string> { "-v","error" };
        var duration=transition?.Duration ?? clip.Duration;
        var filter=$"trim=duration={N(sourceLen)},setpts=PTS-STARTPTS,fps={midRate},scale={decodeW}:{decodeH}";
        if (speed != 1) filter += $",setpts=PTS/{N(speed)},fps={fps}";
        if(transition is { } span && (span.LeftPadding>0 || span.RightPadding>0)) filter+=$",tpad=start_duration={N(span.LeftPadding)}:start_mode=clone:stop_duration={N(span.RightPadding)}:stop_mode=clone";
        if (clip.Kind is ClipKind.Image or ClipKind.Text) decoderArgs.AddRange(["-loop","1"]);
        decoderArgs.AddRange(["-ss",N(clip.Kind == ClipKind.Video ? clip.SourceOffset : 0),"-i",clip.SourcePath,"-t",N(duration),"-frames:v",Math.Ceiling(duration*fps).ToString(System.Globalization.CultureInfo.InvariantCulture),"-an","-vf",filter,"-pix_fmt","bgra","-f","rawvideo","pipe:1"]);
        using var decoder = VideoEngine.CreateProcess(engine.FfmpegPath,decoderArgs);
        using var encoder = VideoEngine.CreateProcess(engine.FfmpegPath,["-v","error","-y","-f","rawvideo","-pixel_format","bgra","-video_size",$"{outW}x{outH}","-framerate",fps.ToString(),"-i","pipe:0","-an","-c:v","ffv1","-level","3","-pix_fmt","bgra",output],true);
        cancellation.ThrowIfCancellationRequested(); decoder.Start(); encoder.Start();
        var decodeError = decoder.StandardError.ReadToEndAsync(); var encodeError = encoder.StandardError.ReadToEndAsync(); var encodeOutput = encoder.StandardOutput.ReadToEndAsync();
        using var registration = cancellation.Register(() => { try { if (!decoder.HasExited) decoder.Kill(true); } catch { } try { if (!encoder.HasExited) encoder.Kill(true); } catch { } });
        try
        {
            var plan = new MotionPlan(clip,asset?.Cursor,fps); var data = new byte[decodeW*decodeH*4]; var pixels = new byte[outW*outH*4];
            var bitmap = new WriteableBitmap(decodeW,decodeH,96,96,PixelFormats.Bgra32,null); var target = new RenderTargetBitmap(outW,outH,96,96,PixelFormats.Pbgra32); var visual = new DrawingVisual();
            var total = Math.Max(1,(int)Math.Ceiling(duration*fps));
            for (int frame = 0; frame < total; frame++)
            {
                cancellation.ThrowIfCancellationRequested(); var read = 0;
                while (read < data.Length) { var n = decoder.StandardOutput.BaseStream.Read(data,read,data.Length-read); if (n == 0) break; read += n; }
                if (read == 0) break;
                if (read != data.Length) throw new InvalidOperationException("Fotograma incompleto al procesar efectos.");
                if (clip.Effects.ChromaEnabled) ChromaKey.Apply(data,decodeW,decodeH,ChromaKey.Parse(clip.Effects.ChromaColor),clip.Effects.ChromaSimilarity,clip.Effects.ChromaBlend);
                bitmap.WritePixels(new Int32Rect(0,0,decodeW,decodeH),data,decodeW*4,0);
                var time=(transition?.Start ?? clip.Start)+frame/(double)fps;
                var motion=plan.At(time-clip.Start); if(transition!=null) motion=transition.Decorate(motion,time);
                if (clip.HasTransformKeys)
                {
                    using var transformed = visual.RenderOpen();
                    FramePainter.DrawTransformed(transformed,canvasW,canvasH,width,height,decodeW,decodeH,motion,clip.Effects,(context,rect) => context.DrawImage(bitmap,rect));
                }
                else using (var dc = visual.RenderOpen()) FramePainter.Draw(dc,width,height,motion,clip.Effects,(context,rect) => context.DrawImage(bitmap,rect));
                target.Clear(); target.Render(visual); target.CopyPixels(pixels,outW*4,0);
                // WPF returns premultiplied alpha; FFmpeg expects straight BGRA.
                for (var i = 0; i < pixels.Length; i += 4) if (pixels[i+3] is > 0 and < 255) { int alpha = pixels[i+3]; pixels[i] = (byte)Math.Min(255,pixels[i]*255/alpha); pixels[i+1] = (byte)Math.Min(255,pixels[i+1]*255/alpha); pixels[i+2] = (byte)Math.Min(255,pixels[i+2]*255/alpha); }
                encoder.StandardInput.BaseStream.Write(pixels); progress.Report((frame+1d)/total);
            }
            encoder.StandardInput.Close();
            decoder.WaitForExit(); encoder.WaitForExit(); cancellation.ThrowIfCancellationRequested();
            if (decoder.ExitCode != 0 || encoder.ExitCode != 0) throw new InvalidOperationException("No se pudieron procesar los efectos. " + decodeError.GetAwaiter().GetResult() + encodeError.GetAwaiter().GetResult());
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
        finally
        {
            if (!decoder.HasExited) decoder.Kill(true); if (!encoder.HasExited) encoder.Kill(true);
            decoder.WaitForExit(); encoder.WaitForExit(); decodeError.GetAwaiter().GetResult(); encodeError.GetAwaiter().GetResult(); encodeOutput.GetAwaiter().GetResult();
        }
    }
}
