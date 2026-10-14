using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenStudio;
using ScreenStudio.Controls;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;

internal static partial class Program
{
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { Check(true,label); return; } throw new Exception(label); }
    private static async Task<string> Run(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var p = Process.Start(start)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); if (p.ExitCode != 0) throw new Exception(await error); return await output;
    }
    private sealed class Progress : IProgress<double> { public double Value; public void Report(double value) => Value = value; }
    private static string directory = "";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            directory = Path.GetFullPath("artifacts/checks"); Directory.CreateDirectory(directory);
            Check(typeof(MainWindow).Assembly.GetName().Name == "MamboStudio", "application assembly uses Mambo Studio branding");
            ModelChecks(); MotionChecks(); var project = args.Contains("--ui-only") ? StudioProject.LoadAsync(Path.Combine(directory,"sample.studio")).GetAwaiter().GetResult() : MediaChecks().GetAwaiter().GetResult();
            if (!args.Contains("--ui-only")) EffectChecks().GetAwaiter().GetResult();
            var app = new App { OpenMainWindowOnStartup = false }; app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if(args.Contains("--screens-transitions")) { Pump(ScreenTransitionChecks); Pump(ParallelCaptureChecks); Console.WriteLine("Artifacts: "+directory); return 0; }
            if (args.Contains("--capture-fixture")) Pump(CaptureFixtureChecks);
            if (!args.Contains("--chrome-only"))
            {
                if(!args.Contains("--preview-only"))
                {
                    Pump(() => WindowChecks(project));
                    Pump(NewPanelChecks);
                    Pump(EffectInteractionChecks);
                }
                Pump(PreviewTransformChecks);
            }
            if(!args.Contains("--preview-only")) Pump(ChromeChecks);
            Console.WriteLine("Artifacts: " + directory); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void ModelChecks()
    {
        var p = new StudioProject(); var image = new TimelineClip { Kind = ClipKind.Image, Duration = 2 };
        p.Tracks[0].Clips.Add(image); var session = new EditingSession(p); session.AddTrack(TrackKind.Visual);
        var destination = session.Project.Tracks[0]; session.Move(image.Id,destination.Id,3);
        Check(session.Owner(image.Id)!.Id == destination.Id && session.Clip(image.Id)!.Start == 3,"move image across visual tracks and in time");
        var audio = session.Project.Tracks.First(t => t.Kind == TrackKind.Audio);
        Reject(() => session.Move(image.Id,audio.Id,0),"reject image on audio track");
        Reject(() => session.RemoveTrack(destination.Id),"reject removal of occupied track");
        session.Split(image.Id,4); Check(destination.Clips.Count == 2 && destination.Clips.All(c => c.Duration == 1),"split preserves timing");
        var right = destination.Clips.Single(c => c.Id != image.Id);
        Reject(() => session.Move(right.Id,destination.Id,3.5),"reject same-track overlaps");
        session.Undo(); Check(session.Owner(image.Id)!.Clips.Count == 1,"undo split"); session.Redo(); Check(session.Owner(image.Id)!.Clips.Count == 2,"redo split");
        var empty = session.Project.Tracks.First(t => t.Kind == TrackKind.Visual && t.Clips.Count == 0); session.RemoveTrack(empty.Id); Check(session.Track(empty.Id) == null,"delete empty track");
        var audioClip = new TimelineClip { Kind = ClipKind.Audio, SourceDuration = 4, Duration = 3 };
        session.Change(p => p.Tracks.First(t => t.Kind == TrackKind.Audio).Clips.Add(audioClip));
        Reject(() => session.Move(audioClip.Id,destination.Id,8),"audio remains in its own category");
        session.Trim(audioClip.Id,1,2,1); Check(session.Clip(audioClip.Id)!.SourceOffset == 1,"trim changes source in point");
        Reject(() => session.Trim(audioClip.Id,1,4,1),"reject trim past source duration");
        Check(session.Clip(audioClip.Id)!.Duration == 2,"failed edit rolls back atomically");
        var speedy = new TimelineClip { Kind = ClipKind.Video, SourceDuration = 4, Duration = 2, Speed = 10 };
        Reject(() => session.Change(p => p.Tracks[0].Clips.Add(speedy)),"reject out-of-range clip speed");
        var fast = new TimelineClip { Kind = ClipKind.Video, Start = 6, Duration = 2, SourceDuration = 4, Speed = 2 };
        session.Change(p => p.Tracks[0].Clips.Add(fast));
        Reject(() => session.Change(p => fast.Duration = 9),"reject speed playback past source duration");
        session.Split(fast.Id,7);
        var fastRight = session.Track(session.Owner(fast.Id)!.Id)!.Clips.First(c => c.Start == 7);
        Check(Math.Abs(fastRight.SourceOffset - .5) < .0001 && fastRight.Duration == 1 && session.Clip(fast.Id)!.Duration == 1,"split advances source offset by timeline time over speed");
        session.Change(p => { var c = session.Clip(audioClip.Id)!; c.FadeIn = .2; c.FadeOut = .3; });
        var faded = session.Clip(audioClip.Id)!;
        Check(faded.FadeIn == .2 && MotionPlan.AudioEnvelope(faded,0) == 0 && Math.Abs(MotionPlan.AudioEnvelope(faded,.1) - .5) < .001 && MotionPlan.AudioEnvelope(faded,1) == 1,"audio fade envelope matches export curve");
        Reject(() => session.Change(p => session.Clip(audioClip.Id)!.FadeIn = -1),"reject negative audio fade");
        Reject(() => session.Change(p => p.Tracks[0].Clips.Add(new TimelineClip { Kind = ClipKind.Image, Duration = 1, Effects = new ClipEffects { BlurAmount = 99 } })),"reject out-of-range blur amount");
        Reject(() => session.Change(p => p.Tracks[0].Clips.Add(new TimelineClip { Kind = ClipKind.Image, Duration = 1, Effects = new ClipEffects { ChromaColor = "red", ChromaSimilarity = .5 } })),"reject malformed chroma color");
        Check(MultitrackEngine.Tempo(2) == "atempo=2,atempo=1" && MultitrackEngine.Tempo(.25) == "atempo=0.5,atempo=0.5" && MultitrackEngine.Tempo(1.5) == "atempo=1.5","atempo chains cover the full speed range");
        Check(KeyframeTrack.Evaluate(null,1,.7) == .7 && KeyframeTrack.Evaluate([new Keyframe(1,0)],0,5) == 0 && Math.Abs(KeyframeTrack.Evaluate([new Keyframe(0,0),new Keyframe(2,1)],1,9) - .5) < .001 && KeyframeTrack.Evaluate([new Keyframe(2,.3)],9,9) == .3,"keyframe tracks interpolate, hold and fall back to base");
        Reject(() => KeyframeTrack.Validate([new Keyframe(2,0),new Keyframe(1,0)],"x"),"reject unordered keyframes");
        Reject(() => session.Change(p => { var c = session.Clip(audioClip.Id)!; c.VolumeKeys.Add(new Keyframe(0,2)); c.VolumeKeys.Add(new Keyframe(0,9)); }),"reject out-of-range volume keyframes");
        var pair = new StudioProject { Width = 640, Height = 360, Tracks = [new StudioTrack { Name = "A", Kind = TrackKind.Audio, Clips = [new TimelineClip { Kind = ClipKind.Audio, Duration = 2, SourceDuration = 4 },new TimelineClip { Kind = ClipKind.Audio, Start = 2, Duration = 2, SourceDuration = 4 }] }] };
        var (left, next) = AudioCrossfade.Apply(pair,pair.Clips.First().Id);
        Check(left.FadeOut == .15 && next.FadeIn == .15,"crossfade pairs fades on adjacent audio clips");
        Reject(() => AudioCrossfade.Apply(pair,next.Id),"reject crossfade without a following clip");
        var gapped = new StudioProject { Width = 640, Height = 360, Tracks = [new StudioTrack { Name = "A", Kind = TrackKind.Audio, Clips = [new TimelineClip { Kind = ClipKind.Audio, Duration = 2, SourceDuration = 4 },new TimelineClip { Kind = ClipKind.Audio, Start = 3, Duration = 2, SourceDuration = 4 }] }] };
        Reject(() => AudioCrossfade.Apply(gapped,gapped.Clips.First().Id),"reject crossfade across a gap");
        Reject(() => session.Change(p => p.Tracks[0].Clips.Add(new TimelineClip { Kind = ClipKind.Video, Duration = 1, ScaleKeys = [new Keyframe(0,5)] })),"reject out-of-range scale keyframes");
        Reject(() => session.Change(p => p.Tracks[0].Clips.Add(new TimelineClip { Kind = ClipKind.Video, Duration = 1, XKeys = [new Keyframe(0,2)] })),"reject out-of-range position keyframes");
        var queue = new ExportQueue();
        var queued = new StudioProject { Name = "Q", Width = 640, Height = 360 };
        var job = queue.Enqueue(queued,"q.mp4"); queued.Name = "Changed";
        Check(job.Project.Name == "Q" && queue.Jobs.Count == 1,"queue snapshots the project on enqueue");
        Check(queue.Remove(Guid.NewGuid()) == false && queue.Remove(job.Id) && queue.Jobs.Count == 0,"queue removes jobs by id");
        var sliced = new StudioProject { Width = 640, Height = 360, Tracks = [new StudioTrack { Name = "V", Clips = [new TimelineClip { Kind = ClipKind.Video, Start = 2, Duration = 4, SourceOffset = 1, SourceDuration = 10, OpacityKeys = [new Keyframe(.5,0),new Keyframe(1,.5)] },new TimelineClip { Kind = ClipKind.Video, Start = 8, Duration = 2, SourceOffset = 0, SourceDuration = 5 }] }] };
        var cut = sliced.Slice(3,9);
        Check(cut.Tracks[0].Clips.Count == 2 && cut.Tracks[0].Clips[0].Start == 0 && cut.Tracks[0].Clips[0].Duration == 3 && Math.Abs(cut.Tracks[0].Clips[0].SourceOffset - 2) < .0001 && cut.Tracks[0].Clips[1].Start == 5 && cut.Tracks[0].Clips[1].Duration == 1 && cut.Tracks[0].Clips[0].OpacityKeys.SequenceEqual([new Keyframe(0,.5)]),"export range slices edge clips and shifts keyframes");
        Reject(() => sliced.Slice(3,3),"reject empty export ranges");
        Reject(() => session.Change(p => { p.ExportIn = 2; p.ExportOut = 1; }),"reject export range ending before it starts");
        Check(Subtitles.Render([(1.2,2.5,"Hola"),(65.5,67.25,"Línea1\nLínea2")]) == "1\r\n00:00:01,200 --> 00:00:02,500\r\nHola\r\n\r\n2\r\n00:01:05,500 --> 00:01:07,250\r\nLínea1\nLínea2\r\n","subtitles render numbered cues with timestamps");
        var auto = MainWindow.AutosavePath(); var backup = auto + ".test-bak"; var hadAutosave = File.Exists(auto);
        if (hadAutosave) File.Move(auto,backup,true);
        try { File.WriteAllText(auto,"x"); MainWindow.ClearAutosave(); Check(!File.Exists(auto) && auto.EndsWith("autosave.studio"),"manual save clears the autosave file"); }
        finally { if (hadAutosave) File.Move(backup,auto,true); }
        var keyed = new byte[] { 0,255,0,255, 255,0,0,255 };
        ChromaKey.Apply(keyed,2,1,ChromaKey.Parse("#00FF00"),.5,0);
        Check(keyed[3] == 0 && keyed[7] == 255,"chroma key removes the key color and keeps the rest");
        var slice = Waveform.Slice([0f,.5f,1f,.5f],0,4,4,4);
        Check(slice.SequenceEqual([0f,.5f,1f,.5f]),"waveform slice maps source time to columns");
        Reject(() => new OutputSettings { Format = "webm" }.Validate(),"reject incompatible container and codecs");
        var prefsPath = Path.Combine(directory,"preferences-test.json");
        new StudioPreferences { Dark = false, LeftCollapsed = true, RightTabs = ["Archivos","Lienzo"], Defaults = new OutputSettings { FrameRate = 24 } }.Save(prefsPath);
        var prefs = StudioPreferences.Load(prefsPath);
        Check(!prefs.Dark && prefs.LeftCollapsed && prefs.RightTabs.Contains("Archivos") && prefs.Defaults.FrameRate == 24,"persist theme, panel layout and defaults");
    }
    private static async Task<StudioProject> MediaChecks()
    {
        var engine = new VideoEngine(); var compositor = new MultitrackEngine(engine);
        var video = Path.Combine(directory,"blue source.mp4"); var image = Path.Combine(directory,"red image.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=blue:s=640x360:r=30","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","2","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac",video);
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=red:s=640x360","-frames:v","1",image);
        var asset = await compositor.InspectAsync(video); var photo = await compositor.InspectAsync(image);
        Check(asset.Kind == ClipKind.Video && asset.HasAudio && asset.Duration == 2 && photo.Kind == ClipKind.Image,"probe video, embedded audio and image");
        var opaquePng = Path.Combine(directory,"opaque.png"); var alphaPng = Path.Combine(directory,"alpha.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=red:s=64x64","-frames:v","1",opaquePng);
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=red:s=64x64,format=rgba,colorchannelmixer=aa=0.5","-frames:v","1",alphaPng);
        Check(!(await compositor.InspectAsync(opaquePng)).HasAlpha && (await compositor.InspectAsync(alphaPng)).HasAlpha,"alpha channel detection on import");
        var fallback = TextArtwork.PosterFromFile(opaquePng);
        var fallbackOk = false;
        if (fallback != null && fallback.PixelWidth > 0)
        {
            var converted = new FormatConvertedBitmap(fallback,PixelFormats.Bgra32,null,0);
            var px = new byte[4]; converted.CopyPixels(new Int32Rect(converted.PixelWidth/2,converted.PixelHeight/2,1,1),px,4,0);
            fallbackOk = px[2] > 200 && px[0] < 80 && px[1] < 80;
        }
        Check(fallbackOk,"wpf fallback decodes stills directly");
        var jfif = Path.Combine(directory,"photo.jfif"); File.Copy(opaquePng,jfif,true);
        var jfifAsset = await compositor.InspectAsync(jfif);
        Check(jfifAsset.Kind == ClipKind.Image && jfifAsset.Duration == 5,"jpeg saved as jfif imports as image");
        var fullJpg = Path.Combine(directory,"full.jpg"); var cutJpg = Path.Combine(directory,"cut.jpg");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=red:s=1280x720","-frames:v","1",fullJpg);
        var fullBytes = await File.ReadAllBytesAsync(fullJpg); await File.WriteAllBytesAsync(cutJpg,fullBytes[..(fullBytes.Length * 3 / 10)]);
        Check((await compositor.InspectAsync(fullJpg)).Kind == ClipKind.Image,"valid jpeg imports as image");
        try { await compositor.InspectAsync(cutJpg); throw new Exception("Damaged image accepted"); }
        catch (InvalidOperationException) { Check(true,"damaged image is rejected with guidance"); }
        var p = new StudioProject { Name = "Edición multipista", Width = 640, Height = 360, Library = [asset,photo], Tracks = [] };
        p.Tracks.Add(new StudioTrack { Name = "Títulos", Clips = [new TimelineClip { Kind = ClipKind.Text, Name = "STUDIO", Text = "STUDIO", Start = 1, Duration = .5, FontSize = 70 }] });
        p.Tracks.Add(new StudioTrack { Name = "Imágenes", Clips = [new TimelineClip { Kind = ClipKind.Image, SourcePath = image, Name = "Imagen superpuesta", Start = .5, Duration = .75, Scale = .25, X = .25, Y = .25 }] });
        p.Tracks.Add(new StudioTrack { Name = "Vídeo", Clips = [new TimelineClip { Kind = ClipKind.Video, SourcePath = video, Name = "Vídeo principal", Duration = 2, SourceDuration = 2 }] });
        p.Tracks.Add(new StudioTrack { Name = "Audio", Kind = TrackKind.Audio, Clips = [new TimelineClip { Kind = ClipKind.Audio, SourcePath = video, Name = "Audio separado", Start = .5, Duration = 1, SourceDuration = 2 }] });
        var output = Path.Combine(directory,"multitrack.mp4"); var progress = new Progress();
        await compositor.ExportAsync(p,output,progress,CancellationToken.None);
        var info = await engine.ProbeAsync(output);
        Check(Math.Abs(info.Duration - 2) < .1 && info.Width == 640 && info.Height == 360 && progress.Value == 1,"export all tracks to MP4 with correct duration and dimensions");
        for (var i = 0; i < 3; i++)
        {
            var time = new[] { "0.25","0.75","1.5" }[i]; var frame = Path.Combine(directory,$"frame-{i}.png");
            await Run(engine.FfmpegPath,"-v","error","-y","-ss",time,"-i",output,"-frames:v","1",frame);
            var bitmap = TextArtwork.Decode(await File.ReadAllBytesAsync(frame)); var converted = new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0); var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(160,90,1,1),pixel,4,0);
            Check(i == 1 ? pixel[2] > 220 && pixel[0] < 30 : pixel[0] > 220 && pixel[2] < 30,"image compositing respects clip timing, frame " + i);
        }
        var textFrame = Path.Combine(directory,"text-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","1.15","-i",output,"-frames:v","1",textFrame);
        var textBitmap = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(textFrame)),PixelFormats.Bgra32,null,0); var pixels = new byte[640*360*4]; textBitmap.CopyPixels(pixels,640*4,0);
        Check(Enumerable.Range(0,640*360).Count(i => pixels[i*4] > 220 && pixels[i*4+1] > 220 && pixels[i*4+2] > 220) > 500,"text overlay renders in export");
        var pcm = Path.Combine(directory,"audio.raw"); await Run(engine.FfmpegPath,"-v","error","-y","-i",output,"-vn","-ac","1","-ar","48000","-f","s16le",pcm); var samples = await File.ReadAllBytesAsync(pcm);
        double Rms(double start,double end) { double sum = 0; int count = 0; for (int i = (int)(start*48000); i < end*48000 && i*2+1 < samples.Length; i++) { var v = BitConverter.ToInt16(samples,i*2) / 32768d; sum += v*v; count++; } return Math.Sqrt(sum / count); }
        Check(Rms(.1,.3) < .001 && Rms(.7,1.2) > .02 && Rms(1.7,1.9) < .001,"audio offset and end produce silence outside clip");
        var peaks = await Waveform.PeaksAsync(engine.FfmpegPath,video,64);
        Check(peaks.Length == 64 && peaks.Max() > .01,"waveform peaks follow source loudness");
        var fadeProject = new StudioProject { Name = "Fundidos", Width = 640, Height = 360, Library = [asset], Tracks = [] };
        fadeProject.Tracks.Add(new StudioTrack { Name = "Audio", Kind = TrackKind.Audio, Clips = [new TimelineClip { Kind = ClipKind.Audio, SourcePath = video, Duration = 2, SourceDuration = 2, FadeIn = .4, FadeOut = .4 }] });
        var faded = Path.Combine(directory,"faded.mp4"); await compositor.ExportAsync(fadeProject,faded,new Progress(),CancellationToken.None);
        var fadedPcm = Path.Combine(directory,"faded.raw"); await Run(engine.FfmpegPath,"-v","error","-y","-i",faded,"-vn","-ac","1","-ar","48000","-f","s16le",fadedPcm); var fadedSamples = await File.ReadAllBytesAsync(fadedPcm);
        double FadedRms(double start,double end) { double sum = 0; int count = 0; for (int i = (int)(start*48000); i < end*48000 && i*2+1 < fadedSamples.Length; i++) { var v = BitConverter.ToInt16(fadedSamples,i*2) / 32768d; sum += v*v; count++; } return Math.Sqrt(sum / count); }
        Check(FadedRms(0,.1) < FadedRms(.8,1.2) * .5 && FadedRms(1.9,2) < FadedRms(.8,1.2) * .5,"audio fades quieten clip edges in export");
        var fastProject = new StudioProject { Name = "Velocidad", Width = 640, Height = 360, Library = [asset], Tracks = [new StudioTrack { Name = "Vídeo", Clips = [new TimelineClip { Kind = ClipKind.Video, SourcePath = video, Duration = 1, SourceDuration = 2, Speed = 2 }] }] };
        var fast = Path.Combine(directory,"fast.mp4"); await compositor.ExportAsync(fastProject,fast,new Progress(),CancellationToken.None);
        Check(Math.Abs((await engine.ProbeAsync(fast)).Duration - 1) < .15,"double speed halves exported duration");
        var blurProject = new StudioProject { Name = "Desenfoque", Width = 640, Height = 360, Library = [photo], Tracks = [new StudioTrack { Name = "Imagen", Clips = [new TimelineClip { Kind = ClipKind.Image, SourcePath = image, Duration = .5, Effects = new ClipEffects { BlurX = .25, BlurY = .25, BlurW = .5, BlurH = .5, BlurAmount = 20 } }] }] };
        var blurred = Path.Combine(directory,"blurred.mp4"); await compositor.ExportAsync(blurProject,blurred,new Progress(),CancellationToken.None);
        Check(Math.Abs((await engine.ProbeAsync(blurred)).Duration - .5) < .15,"region blur exports through the effects pipeline");
        var greenImage = Path.Combine(directory,"green image.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=0x00FF00:s=640x360","-frames:v","1",greenImage);
        var chromaProject = new StudioProject { Name = "Croma", Width = 640, Height = 360, Library = [photo], Tracks = [new StudioTrack { Name = "Imagen", Clips = [new TimelineClip { Kind = ClipKind.Image, SourcePath = greenImage, Duration = .5, Effects = new ClipEffects { ChromaColor = "#00FF00", ChromaSimilarity = .4 } }] }] };
        var keyed = Path.Combine(directory,"keyed.mp4"); await compositor.ExportAsync(chromaProject,keyed,new Progress(),CancellationToken.None);
        var keyFrame = Path.Combine(directory,"key-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","0.25","-i",keyed,"-frames:v","1",keyFrame);
        var keyBitmap = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(keyFrame)),PixelFormats.Bgra32,null,0); var keyPixel = new byte[4]; keyBitmap.CopyPixels(new Int32Rect(320,180,1,1),keyPixel,4,0);
        Check(keyPixel[1] < 100 && keyPixel[2] < 100,"chroma key removes green in export");
        var rangeProject = new StudioProject { Name = "Rango", Width = 640, Height = 360, Library = [asset], Tracks = [new StudioTrack { Name = "Vídeo", Clips = [new TimelineClip { Kind = ClipKind.Video, SourcePath = video, Duration = 2, SourceDuration = 2 }] }], ExportIn = .5, ExportOut = 1.5 };
        var ranged = Path.Combine(directory,"ranged.mp4"); await compositor.ExportAsync(rangeProject,ranged,new Progress(),CancellationToken.None);
        Check(Math.Abs((await engine.ProbeAsync(ranged)).Duration - 1) < .15,"export range trims the timeline");
        var subsProject = new StudioProject { Name = "Subtítulos", Width = 640, Height = 360, Library = [], Tracks = [new StudioTrack { Name = "Títulos", Clips = [new TimelineClip { Kind = ClipKind.Text, Name = "Hola", Text = "Hola mundo", Start = .5, Duration = 1 }] }], Output = new OutputSettings { Subtitles = true } };
        var subtitled = Path.Combine(directory,"subtitled.mp4"); await compositor.ExportAsync(subsProject,subtitled,new Progress(),CancellationToken.None);
        var srt = Path.Combine(directory,"subtitled.srt");
        Check(File.Exists(srt) && (await File.ReadAllTextAsync(srt)).Contains("Hola mundo"),"text clips export as srt sidecar");
        var swellProject = new StudioProject { Name = "Volumen", Width = 640, Height = 360, Library = [asset], Tracks = [new StudioTrack { Name = "Audio", Kind = TrackKind.Audio, Clips = [new TimelineClip { Kind = ClipKind.Audio, SourcePath = video, Duration = 2, SourceDuration = 2, VolumeKeys = [new Keyframe(0,0),new Keyframe(2,1)] }] }] };
        var swelled = Path.Combine(directory,"swelled.mp4"); await compositor.ExportAsync(swellProject,swelled,new Progress(),CancellationToken.None);
        var swellPcm = Path.Combine(directory,"swell.raw"); await Run(engine.FfmpegPath,"-v","error","-y","-i",swelled,"-vn","-ac","1","-ar","48000","-f","s16le",swellPcm); var swellSamples = await File.ReadAllBytesAsync(swellPcm);
        double SwellRms(double start,double end) { double sum = 0; int count = 0; for (int i = (int)(start*48000); i < end*48000 && i*2+1 < swellSamples.Length; i++) { var v = BitConverter.ToInt16(swellSamples,i*2) / 32768d; sum += v*v; count++; } return Math.Sqrt(sum / count); }
        Check(SwellRms(0,.2) < SwellRms(1.5,1.9) * .4,"volume keyframes swell audio in export");
        var ghostProject = new StudioProject { Name = "Aparición", Width = 640, Height = 360, Library = [asset], Tracks = [new StudioTrack { Name = "Vídeo", Clips = [new TimelineClip { Kind = ClipKind.Video, SourcePath = video, Duration = 2, SourceDuration = 2, OpacityKeys = [new Keyframe(0,0),new Keyframe(2,1)] }] }] };
        var ghost = Path.Combine(directory,"ghost.mp4"); await compositor.ExportAsync(ghostProject,ghost,new Progress(),CancellationToken.None);
        var earlyFrame = Path.Combine(directory,"ghost-early.png"); var lateFrame = Path.Combine(directory,"ghost-late.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-ss","0.2","-i",ghost,"-frames:v","1",earlyFrame);
        await Run(engine.FfmpegPath,"-v","error","-y","-ss","1.8","-i",ghost,"-frames:v","1",lateFrame);
        var earlyBlue = await BlueLevel(earlyFrame); var lateBlue = await BlueLevel(lateFrame);
        Check(lateBlue - earlyBlue > 80,"opacity keyframes fade video in export");
        var batch = new ExportQueue();
        batch.Enqueue(new StudioProject { Name = "A", Width = 640, Height = 360 },"a.mp4");
        batch.Enqueue(new StudioProject { Name = "B", Width = 640, Height = 360 },"b.mp4");
        var order = new List<string>();
        var errors = await batch.ProcessAsync(async (p,d,report,token) => { order.Add(d); if (d == "b.mp4") throw new InvalidOperationException("boom"); await Task.Delay(1,token); },null,CancellationToken.None);
        Check(order.SequenceEqual(["a.mp4","b.mp4"]) && errors.Count == 2 && errors.Values.Count(e => e == null) == 1 && errors.Values.Contains("boom"),"queue exports in order and records failures without stopping");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var batch2 = new ExportQueue(); batch2.Enqueue(new StudioProject { Name = "C", Width = 640, Height = 360 },"c.mp4");
        try { await batch2.ProcessAsync((p,d,report,token) => Task.CompletedTask,null,cancelled.Token); throw new Exception("Queue ignored cancellation"); }
        catch (OperationCanceledException) { Check(true,"queue stops between jobs on cancel"); }
        var slideProject = new StudioProject { Name = "Deslizar", Width = 640, Height = 360, Library = [photo], Tracks = [new StudioTrack { Name = "Imagen", Clips = [new TimelineClip { Kind = ClipKind.Image, SourcePath = image, Duration = 2, XKeys = [new Keyframe(0,0),new Keyframe(2,1)] }] }] };
        var slide = Path.Combine(directory,"slide.mp4"); await compositor.ExportAsync(slideProject,slide,new Progress(),CancellationToken.None);
        var slideEarly = Path.Combine(directory,"slide-early.png"); var slideLate = Path.Combine(directory,"slide-late.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-ss","0.2","-i",slide,"-frames:v","1",slideEarly);
        await Run(engine.FfmpegPath,"-v","error","-y","-ss","1.8","-i",slide,"-frames:v","1",slideLate);
        static async Task<int> RedAt(string frame,int x)
        {
            var bitmap = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(frame)),PixelFormats.Bgra32,null,0); var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x,180,1,1),pixel,4,0); return pixel[2];
        }
        var leftEarly = await RedAt(slideEarly,60); var rightEarly = await RedAt(slideEarly,580);
        var leftLate = await RedAt(slideLate,60); var rightLate = await RedAt(slideLate,580);
        Check(leftEarly > 200 && rightEarly < 100 && leftLate < 100 && rightLate > 200,"position keyframes slide the clip across export");
        var stabHelp = "";
        try { stabHelp = await Run(engine.FfmpegPath,"-hide_banner","-h","filter=vidstabdetect"); } catch { }
        if (stabHelp.Contains("shakiness"))
        {
            var stabProject = new StudioProject { Name = "Estable", Width = 640, Height = 360, Library = [asset], Tracks = [new StudioTrack { Name = "Vídeo", Clips = [new TimelineClip { Kind = ClipKind.Video, SourcePath = video, Duration = 2, SourceDuration = 2 }] }], Output = new OutputSettings { Stabilize = true } };
            var steadied = Path.Combine(directory,"steadied.mp4"); await compositor.ExportAsync(stabProject,steadied,new Progress(),CancellationToken.None);
            Check(Math.Abs((await engine.ProbeAsync(steadied)).Duration - 2) < .15,"stabilization passes through without breaking export");
        }
        else Check(true,"vidstab not compiled into this ffmpeg build");
        static async Task<int> BlueLevel(string frame)
        {
            var bitmap = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(frame)),PixelFormats.Bgra32,null,0); var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(320,180,1,1),pixel,4,0); return pixel[0] - pixel[2];
        }
        var saved = Path.Combine(directory,"sample.studio"); await p.SaveAsync(saved); var restored = await StudioProject.LoadAsync(saved); restored.Validate();
        Check(restored.Tracks.Count == 4 && restored.Clips.Select(c => c.Id).SequenceEqual(p.Clips.Select(c => c.Id)) && restored.Output.FrameRate == 30,"save/load preserves tracks, clips and output settings");
        var legacy = Path.Combine(directory,"legacy.screenstudio"); await File.WriteAllTextAsync(legacy,JsonSerializer.Serialize(new { Version = 1, SourcePath = video, StartSeconds = .25, EndSeconds = 1.5, Width = 640, Height = 360 })); var migrated = await StudioProject.LoadAsync(legacy); migrated.Validate();
        Check(migrated.Version == 2 && migrated.Clips.Single().SourceOffset == .25,"migrate previous single-clip projects");
        var original = await File.ReadAllBytesAsync(output); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await compositor.ExportAsync(p,output,progress,cancel.Token); throw new Exception("Cancellation failed"); } catch (OperationCanceledException) { }
        var preserved = await File.ReadAllBytesAsync(output); Check(original.SequenceEqual(preserved) && !Directory.GetDirectories(directory,".studio-*").Any(),"cancel preserves existing export and cleans temporary files");
        try { await compositor.ExportAsync(p,video,progress,CancellationToken.None); throw new Exception("Source overwrite allowed"); } catch (InvalidOperationException) { Check(true,"export cannot overwrite source"); }
        var webm = p.Clone(); webm.Output = new OutputSettings { Format = "webm",VideoEncoder = "libvpx-vp9",AudioEncoder = "libopus",FrameRate = 24 };
        var webmPath = Path.Combine(directory,"multitrack.webm"); await compositor.ExportAsync(webm,webmPath,progress,CancellationToken.None);
        var metadata = await Run(engine.FfprobePath,"-v","error","-show_entries","stream=codec_name,r_frame_rate","-of","json",webmPath);
        Check(metadata.Contains("vp9") && metadata.Contains("opus") && metadata.Contains("24/1"),"configured VP9, Opus, WebM and 24 FPS are used");
        return p;
    }
    private static void Pump(Func<Task> run)
    {
        var frame = new DispatcherFrame(); Exception? error = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () => { try { await run(); } catch (Exception e) { error = e; } finally { frame.Continue = false; } });
        Dispatcher.PushFrame(frame); if (error != null) throw error;
    }
    private static async Task WindowChecks(StudioProject project)
    {
        var preferencesPath = Path.Combine(directory,"ui-preferences.json");
        var window = new MainWindow(new StudioPreferences(),preferencesPath);
        window.Editing.Change(p => { p.Name = project.Name; p.Width = project.Width; p.Height = project.Height; p.Library = project.Library; p.Tracks = project.Tracks; });
        await window.RefreshAsync(); window.Seek(1.15);
        var timeline = (TimelineControl)window.FindName("Timeline"); timeline.PixelsPerSecond = 280;
        timeline.SelectedTrackId = project.Tracks[1].Id; timeline.SelectedClipId = project.Tracks[1].Clips[0].Id; await window.RefreshAsync();
        Check(window.Title.Contains(project.Name),"project name is the window title");
        await window.ExecuteAsync("copy"); window.Seek(1.4); await window.ExecuteAsync("paste");
        Check(window.Editing.Project.Tracks[1].Clips.Count == 2,"copy and paste command creates a second clip");
        await window.ExecuteAsync("undo"); Check(window.Editing.Project.Tracks[1].Clips.Count == 1,"UI undo command restores timeline");
        await window.ExecuteAsync("redo"); Check(window.Editing.Project.Tracks[1].Clips.Count == 2,"UI redo command restores pasted clip");
        await window.ExecuteAsync("undo"); window.Seek(1.15);
        timeline.SelectedClipId = window.Editing.Project.Tracks[1].Clips[0].Id; await window.RefreshAsync();
        Render(window,1440,930,"studio-dark.png");
        var allButtons = Descendants((DependencyObject)window.Content).OfType<Button>().ToArray();
        Check(allButtons.Length > 25 && allButtons.All(b => b.ToolTip != null),"every action button has a name tooltip");
        Check(((Border)window.FindName("LeftHost")).Visibility == Visibility.Visible && ((Border)window.FindName("RightHost")).Visibility == Visibility.Visible,"two sidebars can be visible together");
        await window.ExecuteAsync("light"); Render(window,1440,930,"studio-light.png");
        Render(window,980,680,"studio-small.png");
        Check(((StackPanel)window.FindName("MediaBar")).ActualWidth + 85 < ((ScrollViewer)window.FindName("PreviewScroll")).ActualWidth,"media controls and timecode fit at minimum width");
        Check(((ScrollViewer)window.FindName("PreviewScroll")).ComputedHorizontalScrollBarVisibility == Visibility.Collapsed && ((ScrollViewer)window.FindName("PreviewScroll")).ComputedVerticalScrollBarVisibility == Visibility.Collapsed,"fit preview does not consume space with scrollbars");
        Check(((TextBlock)window.FindName("NoticeText")).Text.Length == 0,"no preview or media errors while editing: " + ((TextBlock)window.FindName("NoticeText")).Text);
        var menus = ((Menu)window.FindName("MenuBar")).Items.Cast<MenuItem>().Select(m => m.Header?.ToString()).ToArray();
        Check(menus.SequenceEqual(new[] { "Archivo","Editar","Pistas","Ver","Configuración" }),"native menu bar contains project, editing, layout and settings commands");
        await window.ExecuteAsync("leftvisible"); await window.ExecuteAsync("rightvisible");
        Check(((ColumnDefinition)window.FindName("LeftColumn")).Width.Value == 0 && ((ColumnDefinition)window.FindName("RightColumn")).Width.Value == 0,"hide both sidebars through menu commands");
        await window.ExecuteAsync("panelleft:Inspector");
        Check(window.Preferences.LeftVisible && window.Preferences.LeftTabs.Contains("Inspector"),"restore any sidebar tab on chosen side");
        Render(window,980,680,"studio-one-panel.png");
        var move = Descendants((Border)window.FindName("LeftHost")).OfType<Button>().Single(b => b.ToolTip?.ToString() == "Mover pestaña al otro lado");
        move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!window.Preferences.LeftTabs.Contains("Inspector") && window.Preferences.RightTabs.Contains("Inspector") && window.Preferences.RightVisible,"move active sidebar tab from left to right");
        Render(window,980,680,"studio-both-panels.png");
        var collapse = Descendants((Border)window.FindName("RightHost")).OfType<Button>().Single(b => b.ToolTip?.ToString() == "Contraer panel"); collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((ColumnDefinition)window.FindName("RightColumn")).Width.Value == 44,"collapse sidebar to icon rail");
        var saved = StudioPreferences.Load(preferencesPath);
        Check(!saved.Dark && saved.RightCollapsed && saved.RightVisible,"UI theme and layout changes survive restart");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root,i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Render(MainWindow window,int width,int height,string file)
    {
        var surface = (FrameworkElement)window.Content; surface.Measure(new Size(width,height)); surface.Arrange(new Rect(0,0,width,height)); surface.UpdateLayout();
        var image = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory,file)); encoder.Save(output);
    }
}





