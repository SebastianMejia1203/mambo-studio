using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ScreenStudio;
using ScreenStudio.Controls;
using ScreenStudio.Models;
internal static partial class Program
{
    private static async Task EffectInteractionChecks()
    {
        var project=await StudioProject.LoadAsync(Path.Combine(directory,"motion.studio"));
        var clip=project.Clips.First(); clip.Effects.Follow=FollowMode.Clicks; clip.Effects.TransitionSeconds=.1; clip.Effects.HoldSeconds=.2;
        var sourceBefore=await File.ReadAllBytesAsync(clip.SourcePath);
        var window=new MainWindow(new StudioPreferences(),Path.Combine(directory,"effect-interaction-preferences.json"));
        window.Editing.Change(p=> { p.Name="Prueba de efectos"; p.Width=project.Width; p.Height=project.Height; p.Library=project.Library; p.Tracks=project.Tracks; });
        await window.RefreshAsync(); window.Show(); await Task.Delay(100);
        var timeline=(TimelineControl)window.FindName("Timeline"); timeline.PixelsPerSecond=240; timeline.Refresh(); window.UpdateLayout();
        var trace=window.Editing.Project.Library[0].Cursor;
        var clickId=EffectCues.Clicks(clip,trace).Single().Id; var zoomId=EffectCues.Zooms(clip,trace).Single().Id;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Call(string method,params object[] args)=>typeof(TimelineControl).GetMethod(method,flags)!.Invoke(timeline,args);
        void Begin(Point point,int count=1)
        {
            typeof(TimelineControl).GetField("down",flags)!.SetValue(timeline,point);
            typeof(TimelineControl).GetField("moved",flags)!.SetValue(timeline,false);
            Call("BeginEffect",point,count);
        }
        void Drag(EffectSelection selection,double seconds,bool edge=false)
        {
            window.UpdateLayout(); var rect=timeline.EffectBounds(selection)!.Value;
            var point=new Point(edge?rect.Left+2:rect.Left+rect.Width/2,rect.Top+rect.Height/2);
            Begin(point); Check(timeline.IsMouseCaptured,"effect drag captures the pointer in a real WPF window");
            Call("MoveEffect",new Point(point.X+seconds*timeline.PixelsPerSecond,point.Y)); Call("EndEffect");
            timeline.Refresh(); window.UpdateLayout();
        }
        try
        {
            var zoom=new EffectSelection(clip.Id,"zoom",zoomId);
            Drag(zoom,.2);
            Check(Math.Abs(clip.Effects.ZoomCues!.Single().Start-1.1)<.001,"dragging zoom changes its timeline position");
            Drag(zoom,.1,edge:true);
            Check(Math.Abs(clip.Effects.ZoomCues!.Single().Start-1.2)<.001 && Math.Abs(clip.Effects.ZoomCues!.Single().Duration-.3)<.001,"dragging zoom edge trims its interval");
            Drag(new(clip.Id,"click",clickId),-.2);
            Check(Math.Abs(clip.Effects.ClickCues!.Single().Time-.8)<.001 && trace!.Samples.Single(s=>s.Click!=0).Time==1 && Math.Abs(clip.Effects.ZoomCues!.Single().Start-1.2)<.001,"moving a highlight preserves recorded mouse data and separate zoom timing");
            Begin(new Point(TimelineControl.HeaderWidth+1.7*timeline.PixelsPerSecond,timeline.TrackTop(0)+TimelineControl.TrackHeight+2*TimelineControl.EffectHeight+14),2);
            Check(clip.Effects.ZoomCues!.Count==2 && timeline.SelectedClipId==clip.Id && timeline.SelectedEffect?.Kind=="zoom","double-clicking the zoom lane adds and selects an interval");
            window.UpdateLayout();
            var right=(Border)window.FindName("RightHost");
            var enabled=Descendants(right).OfType<CheckBox>().Single(c=>c.Content?.ToString()=="Evento activo");
            enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!clip.Effects.ZoomCues.Last().Enabled,"a selected interval can be disabled independently");
            var saved=Path.Combine(directory,"edited-effects.studio"); await window.Editing.Project.SaveAsync(saved); var restored=await StudioProject.LoadAsync(saved);
            Check(restored.Clips.First().Effects.ZoomCues?.Count==2 && restored.Clips.First().Effects.ClickCues?.Single().Time==.8 && !restored.Clips.First().Effects.ZoomCues!.Last().Enabled,"save and reopen preserve edited click and zoom events");
            var sourceAfter=await File.ReadAllBytesAsync(clip.SourcePath);
            Check(clip.Start==0 && clip.SourceOffset==0 && clip.Duration==2 && sourceBefore.SequenceEqual(sourceAfter),"timeline effect edits preserve source video and media timing");
        }
        finally
        {
            typeof(MainWindow).GetField("dirty",flags)!.SetValue(window,false);
            window.Close();
        }
    }
}
