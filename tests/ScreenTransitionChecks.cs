using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio;
using ScreenStudio.Controls;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
internal static partial class Program
{
    private static async Task ScreenTransitionChecks()
    {
        var monitors=CaptureTargets.Monitors();
        Check(monitors.Count>0 && monitors.Any(m=>m.Primary),"enumerate Windows monitors with their physical bounds");
        foreach(var monitor in monitors)
        {
            var options=new CaptureOptions { Mode=CaptureMode.Screen,MonitorDevice=monitor.Device };
            var args=CaptureTargets.InputArguments(options,30);
            Check(CaptureTargets.Bounds(options)==monitor.Bounds && args[args.IndexOf("-offset_x")+1]==monitor.Bounds.X.ToString(),"selected monitor supplies its own capture coordinates");
        }
        Reject(()=>CaptureTargets.Bounds(new CaptureOptions { Mode=CaptureMode.TwoScreens,MonitorDevice=monitors[0].Device,SecondMonitorDevice=monitors[0].Device }),"reject recording the same monitor twice");
        var engine=new VideoEngine(); var compositor=new MultitrackEngine(engine);
        var red=Path.Combine(directory,"transition-red.mp4"); var blue=Path.Combine(directory,"transition-blue.mp4");
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=red:s=640x360:r=30","-t","2","-c:v","libx264","-pix_fmt","yuv420p",red);
        await Run(engine.FfmpegPath,"-v","error","-y","-f","lavfi","-i","color=c=blue:s=640x360:r=30","-t","2","-c:v","libx264","-pix_fmt","yuv420p",blue);
        var a=await compositor.InspectAsync(red); var b=await compositor.InspectAsync(blue);
        var first=new TimelineClip { Name="Código",Kind=ClipKind.Video,SourcePath=red,Duration=1,SourceDuration=2 };
        var second=new TimelineClip { Name="Resultado",Kind=ClipKind.Video,SourcePath=blue,Start=1,Duration=1,SourceDuration=2 };
        second.Effects.TransitionIn=new ClipTransition { Kind=TransitionKind.Dissolve,Duration=.4 };
        var project=new StudioProject { Name="Transiciones",Width=640,Height=360,Background="#00FF00",Library=[a,b],Tracks=[new StudioTrack { Clips=[first,second] }] };
        var spans=TransitionPlan.Create(project); var frame=new MotionFrame(.5,.5,false,100,1,.5,.5,1);
        Check(Math.Abs(spans[first.Id].End-1.2)<.001 && Math.Abs(spans[second.Id].Start-.8)<.001 && Math.Abs(spans[second.Id].Decorate(frame,1).Opacity-.5)<.001 && project.Duration==2,"crossfade spans the cut without changing clip timing");
        async Task<byte[]> Pixel(string file,double time,int x=320)
        {
            var png=Path.Combine(directory,"transition-pixel.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss",time.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",file,"-frames:v","1",png);
            var bitmap=new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(png)),PixelFormats.Bgra32,null,0); var pixel=new byte[4]; bitmap.CopyPixels(new Int32Rect(x,180,1,1),pixel,4,0); return pixel;
        }
        foreach(var kind in new[]{TransitionKind.Dissolve,TransitionKind.FadeBlack,TransitionKind.WipeLeft,TransitionKind.WipeRight})
        {
            second.Effects.TransitionIn.Kind=kind;
            var output=Path.Combine(directory,"transition-"+kind+".mp4"); await compositor.ExportAsync(project,output,new Progress(),CancellationToken.None);
            var middle=await Pixel(output,1); var left=await Pixel(output,1,100); var right=await Pixel(output,1,500);
            Check(kind switch
            {
                TransitionKind.Dissolve=>middle[0]>60 && middle[2]>60 && middle[1]<30,
                TransitionKind.FadeBlack=>middle.Take(3).All(v=>v<30),
                TransitionKind.WipeLeft=>left[0]>200 && right[2]>200,
                _=>left[2]>200 && right[0]>200
            },"native export renders "+kind+" at the cut");
            var before=await Pixel(output,.4); var after=await Pixel(output,1.6);
            Check(before[2]>200 && after[0]>200 && Math.Abs((await engine.ProbeAsync(output)).Duration-2)<.1,"transition retains surrounding footage and total duration");
        }
        second.Effects.TransitionIn.Kind=TransitionKind.Dissolve; second.Effects.TransitionIn.Enabled=false;
        var plain=Path.Combine(directory,"transition-disabled.mp4"); await compositor.ExportAsync(project,plain,new Progress(),CancellationToken.None);
        Check((await Pixel(plain,.9))[2]>200 && (await Pixel(plain,1))[0]>200,"disabled transition returns to a plain cut in export");
        var window=new MainWindow(new StudioPreferences(),Path.Combine(directory,"transition-ui-preferences.json"));
        window.Editing.Change(p=> { p.Name=project.Name; p.Width=project.Width; p.Height=project.Height; p.Background=project.Background; p.Library=project.Library; p.Tracks=project.Tracks; });
        var timeline=(TimelineControl)window.FindName("Timeline"); timeline.SelectedClipId=second.Id; timeline.SelectedTrackId=project.Tracks[0].Id;
        await window.RefreshAsync(); window.Seek(1); await window.ExecuteAsync("panelright:Transiciones"); Render(window,1440,930,"studio-transitions.png");
        var rightHost=(Border)window.FindName("RightHost"); var incoming=Descendants(rightHost).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Entrada activa");
        incoming.IsChecked=true;
        var surface=((Canvas)window.FindName("Stage")).Children.OfType<ClipSurface>().Last();
        Check(Math.Abs(surface.Frame.Opacity-.5)<.01,"transition checkbox immediately updates the preview");
        Render(window,1440,930,"studio-transitions.png");
        Check(timeline.EffectBounds(new(second.Id,"transitionin","in"))!=null,"transition is visible and selectable in its timeline lane");
        var group=Guid.NewGuid(); first.CaptureGroupId=second.CaptureGroupId=group; first.ScreenIndex=0; second.ScreenIndex=1; first.Start=second.Start=0; first.Duration=second.Duration=2; second.Effects.TransitionIn.Kind=TransitionKind.Cut;
        project.Tracks=[new StudioTrack { Name="Código",Clips=[first] },new StudioTrack { Name="Resultado",Clips=[second] }];
        foreach(var layout in new[]{ScreenLayout.SideBySide,ScreenLayout.Stacked})
        {
            ScreenLayoutEditor.Apply(project,group,layout,false);
            Check(layout==ScreenLayout.SideBySide?first.X==.25 && second.X==.75:first.Y==.25 && second.Y==.75,"screen layout positions two independent clips: "+layout);
        }
        ScreenLayoutEditor.Apply(project,group,ScreenLayout.SideBySide,true); Check(first.X==.75 && second.X==.25,"screen ordering can be reversed");
        ScreenLayoutEditor.Apply(project,group,ScreenLayout.Separate,false);
        var editing=new EditingSession(project); editing.Change(p=>ScreenLayoutEditor.SwitchAt(p,group,1,1));
        Check(project.Clips.Count()==4 && project.Clips.Single(c=>c.ScreenIndex==1 && c.Start==1).Opacity==1 && project.Clips.Single(c=>c.ScreenIndex==0 && c.Start==1).Opacity==0,"switching screens at the playhead creates editable source-aligned cuts");
        var saved=Path.Combine(directory,"screen-montage.studio"); await project.SaveAsync(saved); var reopened=await StudioProject.LoadAsync(saved);
        Check(reopened.Clips.All(c=>c.CaptureGroupId==group) && reopened.Clips.Count()==4,"screen group and switching edits survive project reopen");
        var alternating=Path.Combine(directory,"screens-alternating.mp4"); await compositor.ExportAsync(project,alternating,new Progress(),CancellationToken.None);
        Check((await Pixel(alternating,.5))[2]>200 && (await Pixel(alternating,1.5))[0]>200,"alternating screen edits select the correct source in export");
        editing.Undo(); Check(editing.Project.Clips.Count()==2,"screen switch is a single undoable operation");
        await window.ExecuteAsync("panelright:Captura"); Render(window,1440,930,"studio-monitor-selection.png");
        Check(Descendants(rightHost).OfType<ComboBox>().Any(c=>c.Items.OfType<CaptureMonitor>().Any()),"capture panel exposes monitor selection");
        var origin=Descendants(rightHost).OfType<ComboBox>().Single(c=>c.Items.OfType<string>().Contains("Dos pantallas")); origin.SelectedIndex=4;
        Render(window,1440,930,"studio-dual-monitor-selection.png");
        if(monitors.Count>1)
        {
            var savedOptions=StudioPreferences.Load(Path.Combine(directory,"transition-ui-preferences.json")).Capture;
            Check(savedOptions.Mode==CaptureMode.TwoScreens && savedOptions.MonitorDevice!=savedOptions.SecondMonitorDevice,"dual-monitor selection persists automatically without Apply");
        }
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    private static async Task ParallelCaptureChecks()
    {
        var first=new Window { Title="Captura de prueba A",Width=350,Height=260,Left=20,Top=20,WindowStartupLocation=WindowStartupLocation.Manual,Content=new Border { Background=Brushes.Red } };
        var second=new Window { Title="Captura de prueba B",Width=350,Height=260,Left=400,Top=20,WindowStartupLocation=WindowStartupLocation.Manual,Content=new Border { Background=Brushes.Blue } };
        first.Topmost=second.Topmost=true; first.Show(); second.Show();
        var monitors=CaptureTargets.Monitors();
        for(int i=0;i<2;i++)
        {
            var target=monitors[Math.Min(i,monitors.Count-1)].Bounds; var fixture=i==0?first:second;
            SetWindowPos(new WindowInteropHelper(fixture).Handle,IntPtr.Zero,target.X+(i==1 && monitors.Count==1?400:20),target.Y+20,350,260,0x14);
        }
        await Task.Delay(150);
        using var session=new ScreenCaptureSession(new VideoEngine()); var paths=new List<string>();
        try
        {
            CaptureOptions Options(Window window)
            {
                var bounds=CaptureTargets.Bounds(new CaptureOptions { Mode=CaptureMode.Window,WindowHandle=new WindowInteropHelper(window).Handle.ToInt64() });
                if(bounds.Width<320 || bounds.Height<180) throw new Exception("Test window is smaller than its capture region.");
                return new() { Mode=CaptureMode.Region,X=bounds.X,Y=bounds.Y,Width=320,Height=180 };
            }
            await session.StartSourcesAsync([new(Options(first),"A"),new(Options(second),"B")],true,directory,frameRate:24);
            Check(session.IsRecording,"two native capture sources record simultaneously"); await Task.Delay(800); await session.StopAsync(); paths=session.Screens.Select(s=>s.Path).ToList();
            Check(paths.Count==2 && paths.Distinct().Count()==2 && session.Screens.All(s=>s.Start>=0 && s.Start<1),"simultaneous capture produces separate files with shared-clock offsets");
            var engine=new VideoEngine();
            foreach(var screen in session.Screens)
            {
                var trace=await CursorTrace.LoadAsync(screen.Path); var info=await engine.ProbeAsync(screen.Path);
                Check(trace?.Samples.Count>5 && info.Width==320 && info.Height==180 && info.Duration>.5,"each simultaneous source keeps independent editable cursor metadata");
                await Run(engine.FfmpegPath,"-v","error","-i",screen.Path,"-f","null","-");
                var png=Path.Combine(directory,"parallel-capture-frame.png"); await Run(engine.FfmpegPath,"-v","error","-y","-ss","0.3","-i",screen.Path,"-frames:v","1",png);
                var bitmap=new FormatConvertedBitmap(TextArtwork.Decode(await File.ReadAllBytesAsync(png)),PixelFormats.Bgra32,null,0); var pixel=new byte[4]; bitmap.CopyPixels(new Int32Rect(160,90,1,1),pixel,4,0);
                Check(screen.Index==0?pixel[2]>200 && pixel[0]<40:pixel[0]>200 && pixel[2]<40,"simultaneous files contain distinct captured sources");
            }
        }
        finally { first.Close(); second.Close(); foreach(var path in paths) { File.Delete(path); File.Delete(path+".cursor.json"); } }
    }
}
