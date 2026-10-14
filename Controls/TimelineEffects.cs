using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenStudio.Models;
using ScreenStudio.Services;
namespace ScreenStudio.Controls;

public record EffectSelection(Guid ClipId,string Kind,string CueId);
public record TimelineEffectEdit(EffectSelection Selection,string Operation,double Start=0,double Duration=0,double Amount=0);
public sealed partial class TimelineControl
{
    public const double EffectHeight=28;
    public EffectSelection? SelectedEffect { get; set; }
    public event EventHandler<TimelineEffectEdit>? EffectEditRequested;
    public event EventHandler? EffectSelected;
    private sealed record EffectHit(EffectSelection Selection,Rect Rect,double Start,double Duration,bool Enabled);
    private readonly List<EffectHit> effectHits=[];
    private EffectHit? effectDrag;
    private double effectStart,effectDuration;
    private int effectEdge;
    private CursorTrace? Trace(TimelineClip clip)=>Session?.Project.Library.FirstOrDefault(a=>a.Path==clip.SourcePath)?.Cursor;
    private bool HasEffects(StudioTrack track)=>track.Clips.Any(c=>c.Kind==ClipKind.Video);
    private double RowHeight(StudioTrack track)=>TrackHeight+(HasEffects(track)?3*EffectHeight:0)+(track.Kind==TrackKind.Visual && track.Clips.Count>0?EffectHeight:0);
    private double TotalTrackHeight()=>Session?.Project.Tracks.Sum(RowHeight) ?? TrackHeight;
    public double TrackTop(int row)=>RulerHeight+(Session?.Project.Tracks.Take(row).Sum(RowHeight) ?? row*TrackHeight);
    private int RowAt(double y)
    {
        if(Session==null || y<RulerHeight) return -1;
        var top=RulerHeight;
        for(int i=0;i<Session.Project.Tracks.Count;i++) { top+=RowHeight(Session.Project.Tracks[i]); if(y<top) return i; }
        return Session.Project.Tracks.Count;
    }
    public Rect? EffectBounds(EffectSelection selection)=>effectHits.FirstOrDefault(h=>h.Selection==selection)?.Rect;
    private void DrawEffects(DrawingContext dc,StudioTrack track,int row)
    {
        if(track.Kind!=TrackKind.Visual || track.Clips.Count==0) return;
        var top=TrackTop(row)+TrackHeight;
        var motionLanes=HasEffects(track)?3:0;
        for(int lane=0;lane<motionLanes+1;lane++)
        {
            var y=top+lane*EffectHeight;
            dc.DrawRectangle(B("Surface"),null,new Rect(0,y,HeaderWidth-1,EffectHeight));
            Text(dc,lane==motionLanes?"Transiciones":new[]{"Cursor","Clics","Zoom"}[lane],new Point(26,y+7),"Muted",10,110);
            dc.DrawLine(new Pen(B("Border"),.5),new Point(0,y+EffectHeight),new Point(ActualWidth,y+EffectHeight));
        }
        var transitions=TransitionPlan.Create(Session!.Project);
        foreach(var clip in track.Clips)
        {
            var trace=Trace(clip); var e=clip.Effects;
            void Draw(string kind,string id,double sourceStart,double duration,int lane,bool enabled,string label,bool extended=false)
            {
                var selection=new EffectSelection(clip.Id,kind,id);
                if(effectDrag?.Selection==selection) { sourceStart=effectStart; duration=effectDuration; }
                var start=Math.Max(extended?0:clip.Start,clip.Start+sourceStart-clip.SourceOffset);
                var end=Math.Min(clip.End,clip.Start+sourceStart+duration-clip.SourceOffset);
                if(end<=start) return;
                var rect=new Rect(X(start),top+lane*EffectHeight+4,Math.Max(kind=="click"?10:4,(end-start)*PixelsPerSecond),EffectHeight-8);
                if(kind=="click") rect.X-=5;
                effectHits.Add(new(selection,rect,sourceStart,duration,enabled));
                dc.PushOpacity(track.Enabled && enabled ? 1:.35);
                dc.DrawRoundedRectangle(B(kind=="zoom"?"ClipText":kind=="click"?"ClipImage":"ClipVideo"),new Pen(B(SelectedEffect==selection?"Accent":"Border"),SelectedEffect==selection?2:1),rect,3,3);
                if(kind=="click") dc.DrawEllipse(B("Accent"),null,new Point(rect.Left+5,rect.Top+rect.Height/2),2.5,2.5);
                else Text(dc,label,new Point(rect.Left+6,rect.Top+3),"ClipTextInk",10,rect.Width-12);
                dc.Pop();
            }
            if(clip.Kind==ClipKind.Video && trace!=null)
            {
                Draw("cursor","cursor",clip.SourceOffset,clip.Duration,0,e.ShowCursor,e.ShowCursor?"Cursor":"Cursor · desactivado");
                foreach(var cue in EffectCues.Clicks(clip,trace)) Draw("click",cue.Id,cue.Time,.001,1,e.ShowClicks && cue.Enabled,"");
            }
            if(clip.Kind==ClipKind.Video) foreach(var cue in EffectCues.Zooms(clip,trace)) Draw("zoom",cue.Id,cue.Start,cue.Duration,2,e.Follow!=FollowMode.Off && cue.Enabled,$"{(cue.Amount==0?e.Zoom:cue.Amount):0.#}×");
            if(e.TransitionIn.Kind!=TransitionKind.Cut)
            {
                var timing=transitions.GetValueOrDefault(clip.Id); var start=e.TransitionIn.Active?timing!.IncomingStart:clip.Start;
                var duration=e.TransitionIn.Active?timing!.IncomingEnd-start:Math.Min(e.TransitionIn.Duration,clip.Duration/2);
                Draw("transitionin","in",clip.SourceOffset+start-clip.Start,duration,motionLanes,e.TransitionIn.Enabled,"Entrada",true);
            }
            if(e.TransitionOut.Kind!=TransitionKind.Cut) { var d=Math.Min(e.TransitionOut.Duration,clip.Duration/2); Draw("transitionout","out",clip.SourceOffset+clip.Duration-d,d,motionLanes,e.TransitionOut.Enabled,"Salida"); }
        }
    }
    private bool BeginEffect(Point point,int clickCount)
    {
        if(Session==null) return false;
        var row=RowAt(point.Y); if(row<0 || row>=Session.Project.Tracks.Count) return false;
        var track=Session.Project.Tracks[row]; var top=TrackTop(row)+TrackHeight;
        if(track.Kind!=TrackKind.Visual || track.Clips.Count==0 || point.Y<top) return false;
        var hit=effectHits.LastOrDefault(h=>h.Rect.Contains(point));
        if(hit==null)
        {
            if(point.X<HeaderWidth) return true;
            Position=TimeAt(point.X);
            if(clickCount==2 && HasEffects(track) && point.Y>=top+2*EffectHeight && point.Y<top+3*EffectHeight && track.Clips.FirstOrDefault(c=>c.Kind==ClipKind.Video && Position>=c.Start && Position<c.End) is {} clip)
                EffectEditRequested?.Invoke(this,new(new(clip.Id,"zoom",Guid.NewGuid().ToString("N")),"add",clip.SourceOffset+Position-clip.Start));
            return true;
        }
        SelectedEffect=hit.Selection; SelectedClipId=hit.Selection.ClipId; SelectedTrackId=track.Id;
        SelectionChanged?.Invoke(this,EventArgs.Empty); EffectSelected?.Invoke(this,EventArgs.Empty);
        Position=TimeAt(Math.Max(HeaderWidth,point.X)); InvalidateVisual();
        if(hit.Selection.Kind=="cursor" || hit.Selection.Kind.StartsWith("transition"))
        {
            if(clickCount==2) EffectEditRequested?.Invoke(this,new(hit.Selection,"toggle"));
            return true;
        }
        effectDrag=hit; effectStart=hit.Start; effectDuration=hit.Duration; effectEdge=0;
        if(hit.Selection.Kind=="zoom") effectEdge=point.X-hit.Rect.Left<6 ? -1 : hit.Rect.Right-point.X<6 ? 1 : 0;
        CaptureMouse(); return true;
    }
    private bool MoveEffect(Point point)
    {
        if(effectDrag==null || Session==null || !IsMouseCaptured) return false;
        var clip=Session.Clip(effectDrag.Selection.ClipId)!;
        moved|=Math.Abs(point.X-down.X)>3;
        var delta=Math.Round((point.X-down.X)/PixelsPerSecond*Session.Project.Output.FrameRate)/Session.Project.Output.FrameRate;
        var min=clip.SourceOffset; var max=min+clip.Duration;
        if(effectDrag.Selection.Kind=="click") effectStart=Math.Clamp(effectDrag.Start+delta,min,max-.001);
        else if(effectEdge<0)
        {
            effectStart=Math.Clamp(effectDrag.Start+delta,min,Math.Min(max-.04,effectDrag.Start+effectDrag.Duration-.04));
            effectDuration=Math.Min(max,effectDrag.Start+effectDrag.Duration)-effectStart;
        }
        else if(effectEdge>0) { effectStart=Math.Max(min,effectDrag.Start); effectDuration=Math.Clamp(effectDrag.Start+effectDrag.Duration+delta-effectStart,.04,max-effectStart); }
        else
        {
            effectDuration=Math.Min(effectDrag.Duration,clip.Duration);
            effectStart=Math.Clamp(effectDrag.Start+delta,min,max-effectDuration);
        }
        InvalidateVisual(); return true;
    }
    private bool EndEffect()
    {
        if(effectDrag==null) return false;
        var hit=effectDrag; effectDrag=null; ReleaseMouseCapture();
        if(moved) EffectEditRequested?.Invoke(this,new(hit.Selection,"move",effectStart,effectDuration));
        InvalidateVisual(); return true;
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var hit=effectHits.LastOrDefault(h=>h.Rect.Contains(e.GetPosition(this))); if(hit==null) return;
        SelectedEffect=hit.Selection; SelectedClipId=hit.Selection.ClipId; SelectedTrackId=Session?.Owner(hit.Selection.ClipId)?.Id;
        SelectionChanged?.Invoke(this,EventArgs.Empty); EffectSelected?.Invoke(this,EventArgs.Empty); InvalidateVisual();
        var menu=new ContextMenu();
        var toggle=new MenuItem { Header="Activar / desactivar efecto" }; toggle.Click+=(_,_)=>EffectEditRequested?.Invoke(this,new(hit.Selection,"toggle")); menu.Items.Add(toggle);
        if(hit.Selection.Kind!="cursor") { var remove=new MenuItem { Header="Eliminar efecto",InputGestureText="Supr" }; remove.Click+=(_,_)=>EffectEditRequested?.Invoke(this,new(hit.Selection,"delete")); menu.Items.Add(remove); }
        menu.IsOpen=true; e.Handled=true;
    }
}
