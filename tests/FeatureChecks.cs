using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
internal static partial class Program
{
    private static CursorTrace Trace()
    {
        var trace = new CursorTrace { Width=640,Height=360,Duration=3 };
        for (int i=0;i<=180;i++) trace.Samples.Add(new CursorSample { Time=i/60d,X=i < 30 ? .2 : .8,Y=.5,Visible=true,Click=i == 60 ? 1 : 0 });
        return trace;
    }
    private static void MotionChecks()
    {
        var trace = Trace(); trace.Validate();
        var clip = new TimelineClip { Kind=ClipKind.Video,Duration=3,SourceDuration=3,Effects=new ClipEffects { Follow=FollowMode.Clicks,Zoom=2,HoldSeconds=.5,TransitionSeconds=.2,FollowSeconds=.1 } };
        var plan = new MotionPlan(clip,trace,60);
        Check(plan.At(.1).Zoom == 1 && plan.At(1).Zoom > 1.99 && plan.At(2).Zoom == 1,"click zoom animates in and returns to original framing");
        Check(plan.At(1).CenterX <= .75 && plan.At(1).CenterX > .7,"camera follows mouse and stays inside frame edges");
        clip.Effects.Follow = FollowMode.Always; clip.Effects.FollowSeconds = 2; var slow = new MotionPlan(clip,trace,60); clip.Effects.FollowSeconds = .02; var fast = new MotionPlan(clip,trace,60);
        Check(fast.At(.7).CenterX > slow.At(.7).CenterX+.1,"follow response speed changes camera motion");
        clip.Effects.CursorSmoothSeconds = 0; var raw = new MotionPlan(clip,trace,60); clip.Effects.CursorSmoothSeconds = .3; var smooth = new MotionPlan(clip,trace,60);
        Check(raw.At(.5).X > smooth.At(.5).X+.2,"cursor smoothing removes abrupt position changes");
        clip.Effects.Follow = FollowMode.Manual; clip.Effects.FocusX = .25; var manual = new MotionPlan(clip,null,60);
        Check(manual.At(1).Zoom == 2 && !manual.At(1).Visible,"manual zoom works on videos without cursor metadata");
        clip.Effects.FadeSeconds = .3; var fade = new MotionPlan(clip,trace,60);
        Check(fade.At(0).Opacity == 0 && fade.At(.3).Opacity == 1 && fade.At(2.9).Opacity < .5,"entry and exit opacity animations");
        var invalid = Trace(); invalid.Samples[1].X = double.NaN; Reject(invalid.Validate,"reject malformed cursor coordinates");
        var assets = new[] { new MediaAsset { Path="clip.mp4",Kind=ClipKind.Video,Duration=10 },new MediaAsset { Path="photo.png",Kind=ClipKind.Image,Duration=5 },new MediaAsset { Path="music.wav",Kind=ClipKind.Audio,Duration=30 } };
        Check(MediaLibraryView.Query(assets,new MediaLibraryState { Filter=3 }).Single().Name == "music.wav","library filters audio files");
        Check(MediaLibraryView.Query(assets,new MediaLibraryState { Search="CLIP" }).Single().Name == "clip.mp4","library search is case insensitive");
        Check(MediaLibraryView.Query(assets,new MediaLibraryState { Sort=1 }).First().Name == "music.wav","library duration ordering");
        Reject(() => CaptureTargets.Bounds(new CaptureOptions { Mode=CaptureMode.Region,Width=0,Height=100 }),"reject invalid capture regions before starting devices");
        var captureArgs = CaptureTargets.InputArguments(new CaptureOptions { TrackCursor=false },30);
        Check(captureArgs[captureArgs.IndexOf("-draw_mouse")+1] == "0","source recording always excludes baked cursor, even with legacy settings");
        var edited=new TimelineClip { Kind=ClipKind.Video,Duration=3,SourceDuration=3,Effects=new ClipEffects { Follow=FollowMode.Clicks,TransitionSeconds=.1,ZoomCues=[new ZoomCue { Start=1,Duration=1,Amount=3 }],ClickCues=[new ClickCue { Time=.5 }] } };
        var scheduled=new MotionPlan(edited,trace,60);
        Check(scheduled.At(.5).Zoom==1 && scheduled.At(1.5).Zoom==3 && scheduled.At(2.2).Zoom==1,"zoom rendering follows editable intervals only");
        Check(Math.Abs(scheduled.At(.6).ClickAge-.1)<.001,"click highlights follow edited marker times");
        edited.Effects.ZoomCues[0].Enabled=false;
        Check(new MotionPlan(edited,trace,60).At(1.5).Zoom==1,"disabled zoom interval has no rendering effect");
        edited.Effects.ZoomCues[0].Enabled=true; edited.SourceOffset=1.2; edited.Duration=1;
        Check(new MotionPlan(edited,trace,60).At(.3).Zoom==3,"trimmed clips keep source-aligned zoom cues");
        edited.Effects.Follow=FollowMode.Off;
        Check(new MotionPlan(edited,trace,60).At(.3).Zoom==1,"global no-zoom overrides every timeline interval");
        var stationary=Trace(); foreach(var sample in stationary.Samples) { sample.X=.5; sample.Click=sample.Time==2.5?1:0; }
        var idleClip=new TimelineClip { Duration=3,SourceDuration=3,Effects=new ClipEffects { HideIdleCursor=true } };
        var idleFrame=new MotionPlan(idleClip,stationary,60).At(2.6);
        Check(idleFrame.Visible && idleFrame.CursorHidden && idleFrame.ClickAge<.2,"hiding an idle cursor preserves independent click highlights");
    }
    private static async Task EffectChecks()
    {
        var engine = new VideoEngine(); var compositor = new MultitrackEngine(engine); var source = Path.Combine(directory,"motion-source.mp4");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=blue:s=640x360:r=30,drawbox=x=0:y=0:w=320:h=360:color=red:t=fill","-t","3","-c:v","libx264","-pix_fmt","yuv420p",source);
        var trace = Trace(); await trace.SaveAsync(source); var asset = await compositor.InspectAsync(source);
        Check(asset.Cursor?.Samples.Count == 181 && asset.Cursor.Samples.Count(s => s.Click != 0) == 1,"import cursor sidecar with positions and clicks");
        var clip = new TimelineClip { SourcePath=source,Name="Cursor editable",Kind=ClipKind.Video,Duration=2,SourceDuration=3,Effects=new ClipEffects { Follow=FollowMode.Always,Zoom=2,FollowSeconds=.1,CursorSize=70,CornerRadius=24,BorderWidth=2,Shadow=.5 } };
        var project = new StudioProject { Name="Cursor, zoom y apariencia",Width=640,Height=360,Padding=32,GradientBackground=true,Background="#12472E",BackgroundEnd="#092619",Library=[asset],Tracks=[new StudioTrack { Name="Captura",Clips=[clip] }] };
        var output = Path.Combine(directory,"motion-styled.mp4"); await compositor.ExportAsync(project,output,new Progress(),CancellationToken.None);
        var info = await engine.ProbeAsync(output); Check(info.Width == 640 && info.Height == 360 && Math.Abs(info.Duration-2) < .1,"export native cursor, camera and appearance effects");
        var frame = Path.Combine(directory,"motion-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","1.1","-i",output,"-frames:v","1",frame);
        var bitmap = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(frame)),PixelFormats.Bgra32,null,0); var pixels = new byte[640*360*4]; bitmap.CopyPixels(pixels,640*4,0);
        byte[] Pixel(int x,int y) => pixels.Skip((y*640+x)*4).Take(4).ToArray();
        var inside = Pixel(100,180); Check(inside[0] > 220 && inside[2] < 30,"zoom focuses on blue half of the source");
        var corner = Pixel(59,34); Check(corner[1] > corner[0] && corner[1] > corner[2],"rounded alpha reveals gradient background instead of black corners");
        Check(Enumerable.Range(140,100).Sum(y => Enumerable.Range(330,90).Count(x => { var q=Pixel(x,y); return q[0]>220 && q[1]>220 && q[2]>220; })) > 20,"editable cursor renders above video in export");
        var saved = Path.Combine(directory,"motion.studio"); await project.SaveAsync(saved); var loaded = await StudioProject.LoadAsync(saved);
        Check(loaded.Library[0].Cursor?.Samples.Count == 181 && loaded.Clips.First().Effects.Zoom == 2 && loaded.GradientBackground,"project persists cursor data, zoom, appearance and gradient");
        using var cts = new CancellationTokenSource(); var before = await File.ReadAllBytesAsync(output);
        try { await compositor.ExportAsync(project,output,new CancelEffects(cts),cts.Token); throw new Exception("Effects were not cancelled"); } catch (OperationCanceledException) { }
        var after = await File.ReadAllBytesAsync(output); Check(before.SequenceEqual(after) && !Directory.GetDirectories(directory,".studio-*").Any(),"cancelling native effect rendering preserves destination and cleans lossless intermediates");
        var originalSource = await File.ReadAllBytesAsync(source);
        var cursorPath = Path.Combine(directory,"custom-cursor.png");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=lime:s=32x32","-frames:v","1",cursorPath);
        var customProject = project.Clone(); customProject.Padding=0; customProject.GradientBackground=false;
        var customClip = customProject.Clips.First(); customClip.Duration=1.3;
        customClip.Effects=new ClipEffects { Follow=FollowMode.Off,ShowClicks=false,CursorSize=96,CursorImage=CursorArtwork.Import(cursorPath),CursorHotspotX=.5,CursorHotspotY=.5 };
        var customOutput = Path.Combine(directory,"cursor-replaced.mp4"); await compositor.ExportAsync(customProject,customOutput,new Progress(),CancellationToken.None);
        async Task<byte[]> ReadPixel(string video,int x,int y)
        {
            var png = Path.Combine(directory,"cursor-pixel.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","1","-i",video,"-frames:v","1",png);
            var b = new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(png)),PixelFormats.Bgra32,null,0); var p = new byte[4]; b.CopyPixels(new Int32Rect(x,y,1,1),p,4,0); return p;
        }
        var customPixel = await ReadPixel(customOutput,512,180); var unzoomed = await ReadPixel(customOutput,100,180);
        Check(customPixel[1]>180 && customPixel[0]<40 && unzoomed[2]>200,"custom cursor image exports at its hotspot with zoom disabled");
        var customSave = Path.Combine(directory,"custom-cursor.studio"); await customProject.SaveAsync(customSave); var customLoaded = await StudioProject.LoadAsync(customSave);
        Check(customLoaded.Clips.First().Effects.CursorImage == customClip.Effects.CursorImage && customLoaded.Clips.First().Effects.CursorHotspotX == .5,"custom cursor image is embedded in the project");
        customClip.Effects.ShowCursor=false; await compositor.ExportAsync(customProject,customOutput,new Progress(),CancellationToken.None);
        var hidden = await ReadPixel(customOutput,512,180);
        var sourceAfter = await File.ReadAllBytesAsync(source);
        Check(hidden[0]>200 && hidden[1]<40 && originalSource.SequenceEqual(sourceAfter),"cursor can be hidden after recording without modifying source pixels");
        var scheduledProject=project.Clone(); scheduledProject.Padding=0; scheduledProject.GradientBackground=false;
        scheduledProject.Clips.First().Effects=new ClipEffects { Follow=FollowMode.Clicks,ShowCursor=false,ShowClicks=false,TransitionSeconds=.1,FollowSeconds=.1,ZoomCues=[new ZoomCue { Start=.6,Duration=.6,Amount=2 }] };
        var scheduledOutput=Path.Combine(directory,"scheduled-zoom.mp4"); await compositor.ExportAsync(scheduledProject,scheduledOutput,new Progress(),CancellationToken.None);
        foreach(var moment in new[]{.2,.9,1.5})
        {
            var png=Path.Combine(directory,"scheduled-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss",moment.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",scheduledOutput,"-frames:v","1",png);
            var b=new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(png)),PixelFormats.Bgra32,null,0); var p=new byte[4]; b.CopyPixels(new Int32Rect(100,180,1,1),p,4,0);
            Check(moment==.9 ? p[0]>200 && p[2]<40 : p[2]>200 && p[0]<40,"export applies scheduled zoom only inside its editable interval at "+moment);
        }
    }
    private sealed class CancelEffects(CancellationTokenSource source) : IProgress<double> { public void Report(double value) { if (value > 0) source.Cancel(); } }
    private static async Task CaptureFixtureChecks()
    {
        var fixture = new Window { Title="Studio capture fixture",Width=480,Height=300,Left=30,Top=30,WindowStartupLocation=WindowStartupLocation.Manual,Content=new Border { Background=Brushes.Navy,Child=new TextBlock { Text="PRUEBA DE CAPTURA",Foreground=Brushes.White,FontSize=24,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } } };
        fixture.Show(); await Task.Delay(150);
        var engine = new VideoEngine(); using var recorder = new ScreenRecorder(engine);
        string? path = null;
        try
        {
            var options = new CaptureOptions { Mode=CaptureMode.WindowRegion,WindowHandle=new WindowInteropHelper(fixture).Handle.ToInt64(),X=10,Y=10,Width=320,Height=180,TrackCursor=false };
            await recorder.StartAsync(true,directory,frameRate:24,options:options); await Task.Delay(1200); path = await recorder.StopAsync();
            var info = await engine.ProbeAsync(path); var trace = await CursorTrace.LoadAsync(path);
            Check(info.Width == 320 && info.Height == 180 && info.Duration > 1 && trace?.Samples.Count > 10,"real native window-region recording saves synchronized cursor metadata");
            await Run(engine.FfmpegPath,"-v","error","-i",path,"-f","null","-"); Check(true,"all captured window-region frames decode");
        }
        finally { fixture.Close(); if (path != null) { File.Delete(path); File.Delete(path+".cursor.json"); } }
    }
}
