using ScreenStudio.Models;
namespace ScreenStudio.Services;

public sealed class TransitionSpan(TimelineClip clip)
{
    public TimelineClip Clip { get; }=clip;
    public double Start { get; set; }=clip.Start;
    public double End { get; set; }=clip.End;
    public double LeftPadding=>Clip.Start-Start;
    public double RightPadding=>End-Clip.End;
    public double Duration=>End-Start;
    public double IncomingStart,IncomingEnd,IncomingBoundary;
    public bool BetweenClips;
    public TransitionKind NextKind;
    public double NextStart,NextBoundary;
    private static double Smooth(double value) { value=Math.Clamp(value,0,1); return value*value*(3-2*value); }
    public MotionFrame Decorate(MotionFrame frame,double time)
    {
        double opacity=1,reveal=1,blackout=0; int direction=0;
        var incoming=Clip.Effects.TransitionIn;
        if(incoming.Active)
        {
            var progress=Smooth((time-IncomingStart)/(IncomingEnd-IncomingStart));
            if(incoming.Kind==TransitionKind.FadeBlack)
            {
                if(BetweenClips) { opacity=time<IncomingBoundary?0:1; progress=time<IncomingBoundary?0:Smooth((time-IncomingBoundary)/(IncomingEnd-IncomingBoundary)); }
                blackout=1-progress;
            }
            else if(incoming.Kind is TransitionKind.WipeLeft or TransitionKind.WipeRight) { reveal=progress; direction=incoming.Kind==TransitionKind.WipeLeft?-1:1; }
            else opacity*=progress;
        }
        if(NextKind==TransitionKind.FadeBlack) blackout=Math.Max(blackout,Smooth((time-NextStart)/(NextBoundary-NextStart)));
        var outgoing=Clip.Effects.TransitionOut;
        if(outgoing.Active)
        {
            var duration=Math.Min(outgoing.Duration,Clip.Duration/2); var progress=Smooth((time-(Clip.End-duration))/duration);
            if(outgoing.Kind==TransitionKind.FadeBlack) blackout=Math.Max(blackout,progress);
            else if(outgoing.Kind is TransitionKind.WipeLeft or TransitionKind.WipeRight) { reveal=Math.Min(reveal,1-progress); direction=outgoing.Kind==TransitionKind.WipeLeft?-1:1; }
            else opacity*=1-progress;
        }
        return frame with { Opacity=frame.Opacity*opacity,Reveal=reveal,RevealDirection=direction,Blackout=blackout,ClickAge=frame.ClickAge+Math.Max(0,time-Clip.End) };
    }
}
public static class TransitionPlan
{
    public static Dictionary<Guid,TransitionSpan> Create(StudioProject project)
    {
        var result=new Dictionary<Guid,TransitionSpan>();
        foreach(var track in project.Tracks.Where(t=>t.Enabled && t.Kind==TrackKind.Visual))
        {
            var clips=track.Clips.OrderBy(c=>c.Start).ToArray();
            foreach(var clip in clips) result[clip.Id]=new(clip);
            for(int i=0;i<clips.Length;i++)
            {
                var clip=clips[i]; var span=result[clip.Id]; var transition=clip.Effects.TransitionIn;
                if(!transition.Active) continue;
                var previous=i>0?clips[i-1]:null;
                if(previous!=null && Math.Abs(previous.End-clip.Start)<.0001)
                {
                    var half=Math.Min(transition.Duration,Math.Min(previous.Duration,clip.Duration))/2;
                    span.BetweenClips=true; span.Start=Math.Max(0,clip.Start-half); span.IncomingStart=span.Start; span.IncomingEnd=clip.Start+half; span.IncomingBoundary=clip.Start;
                    var before=result[previous.Id]; before.End=clip.Start+half; before.NextKind=transition.Kind; before.NextStart=span.Start; before.NextBoundary=clip.Start;
                }
                else { span.IncomingStart=clip.Start; span.IncomingEnd=clip.Start+Math.Min(transition.Duration,clip.Duration/2); span.IncomingBoundary=clip.Start; }
            }
        }
        return result;
    }
}
