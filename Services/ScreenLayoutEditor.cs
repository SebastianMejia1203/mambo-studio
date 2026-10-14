using ScreenStudio.Models;
namespace ScreenStudio.Services;
public static class ScreenLayoutEditor
{
    public static void Apply(StudioProject project,Guid group,ScreenLayout layout,bool reverse)
    {
        foreach(var clip in project.Clips.Where(c=>c.CaptureGroupId==group))
        {
            project.Tracks.First(t=>t.Clips.Contains(clip)).Enabled=true;
            clip.CaptureLayout=layout; clip.CaptureReversed=reverse;
            var slot=reverse?1-clip.ScreenIndex:clip.ScreenIndex;
            clip.Opacity=layout==ScreenLayout.Separate && slot!=0?0:1;
            clip.Scale=1; clip.X=clip.Y=.5;
            if(layout==ScreenLayout.Separate) continue;
            var asset=project.Library.First(a=>a.Path==clip.SourcePath);
            var width=project.Width-2d*project.Padding; var height=project.Height-2d*project.Padding;
            var full=Math.Min(width/asset.Width,height/asset.Height);
            var fit=Math.Min((layout==ScreenLayout.SideBySide?width/2:width)/asset.Width,(layout==ScreenLayout.Stacked?height/2:height)/asset.Height);
            clip.Scale=Math.Clamp(fit/full,.05,2);
            if(layout==ScreenLayout.SideBySide) clip.X=(project.Padding+width*(slot==0?.25:.75))/project.Width;
            else clip.Y=(project.Padding+height*(slot==0?.25:.75))/project.Height;
        }
    }
    public static Guid? SwitchAt(StudioProject project,Guid group,int screen,double time)
    {
        if(screen is <0 or >1) throw new InvalidOperationException("Pantalla inválida.");
        var selected=project.Clips.FirstOrDefault(c=>c.CaptureGroupId==group && c.ScreenIndex==screen && time>=c.Start && time<c.End);
        if(selected==null) throw new InvalidOperationException("Sitúa el cabezal dentro de la captura de esa pantalla.");
        foreach(var track in project.Tracks)
        foreach(var clip in track.Clips.Where(c=>c.CaptureGroupId==group).ToArray())
        {
            if(clip.End<=time) continue;
            var target=clip;
            if(clip.Start<time)
            {
                if(time-clip.Start<.04 || clip.End-time<.04) throw new InvalidOperationException("Deja al menos un fotograma a cada lado del cambio de pantalla.");
                target=clip.Clone(true); target.SourceOffset+=time-clip.Start; target.Duration=clip.End-time; target.Start=time; target.Effects.TransitionIn=new(); clip.Effects.TransitionOut=new(); clip.Duration=time-clip.Start; track.Clips.Add(target);
            }
            target.Scale=1; target.X=target.Y=.5; target.Opacity=target.ScreenIndex==screen?1:0; target.CaptureLayout=ScreenLayout.Separate; target.CaptureReversed=false; track.Enabled=true;
        }
        return project.Clips.First(c=>c.CaptureGroupId==group && c.ScreenIndex==screen && time>=c.Start && time<c.End).Id;
    }
}
