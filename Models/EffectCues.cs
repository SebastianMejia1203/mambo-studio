using System.Globalization;
namespace ScreenStudio.Models;

public sealed class ClickCue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Time { get; set; }
    public bool Enabled { get; set; } = true;
}
public sealed class ZoomCue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Start { get; set; }
    public double Duration { get; set; }
    public double Amount { get; set; } // 0 inherits the clip's zoom amount.
    public bool Enabled { get; set; } = true;
    public double End => Start+Duration;
}

// Cue times stay in source coordinates, so trimming, splitting and moving a clip
// preserve alignment without modifying the original recording or cursor trace.
public static class EffectCues
{
    public static List<ClickCue> Clicks(TimelineClip clip,CursorTrace? trace) => clip.Effects.ClickCues ??
        (trace?.Samples.Where(s=>s.Visible && s.Click!=0).Select((s,i)=>new ClickCue { Id="click:"+i.ToString(CultureInfo.InvariantCulture),Time=s.Time }).ToList() ?? []);
    public static List<ZoomCue> Zooms(TimelineClip clip,CursorTrace? trace)
    {
        var e=clip.Effects; if(e.ZoomCues != null) return e.ZoomCues;
        var mode=e.Follow == FollowMode.Off ? e.LastFollow : e.Follow;
        if(mode is FollowMode.Always or FollowMode.Manual) return [new ZoomCue { Id="continuous",Start=0,Duration=clip.SourceDuration }];
        // Zoom and click highlighting are independent. Deleting a highlight does not remove a zoom.
        return (trace?.Samples.Where(s=>s.Visible && s.Click!=0).Select((s,i)=>
        {
            var start=Math.Max(0,s.Time-e.TransitionSeconds);
            var end=Math.Min(clip.SourceDuration,s.Time+e.HoldSeconds+e.TransitionSeconds);
            return new ZoomCue { Id="zoom:"+i.ToString(CultureInfo.InvariantCulture),Start=start,Duration=end-start };
        }).Where(z=>z.Duration>=.04).ToList()) ?? [];
    }
    public static List<ClickCue> EditClicks(TimelineClip clip,CursorTrace? trace) => clip.Effects.ClickCues ??= Clicks(clip,trace);
    public static List<ZoomCue> EditZooms(TimelineClip clip,CursorTrace? trace) => clip.Effects.ZoomCues ??= Zooms(clip,trace);
}
