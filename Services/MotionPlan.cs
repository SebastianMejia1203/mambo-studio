using ScreenStudio.Models;
namespace ScreenStudio.Services;

public readonly record struct MotionFrame(double X, double Y, bool Visible, double ClickAge, double Zoom, double CenterX, double CenterY, double Opacity)
{
    public bool CursorHidden { get; init; }
    public double Reveal { get; init; }=1;
    public int RevealDirection { get; init; }
    public double Blackout { get; init; }
    public double Scale { get; init; } = 1;
    public double PositionX { get; init; } = .5;
    public double PositionY { get; init; } = .5;
}
public sealed class MotionPlan
{
    private readonly MotionFrame[] frames;
    private readonly int fps;
    public MotionPlan(TimelineClip clip, CursorTrace? trace, int frameRate)
    {
        fps = frameRate; var count = Math.Max(1,(int)Math.Ceiling(clip.Duration * fps) + 1); frames = new MotionFrame[count];
        var e = clip.Effects; var samples = trace?.Samples ?? [];
        var clicks = EffectCues.Clicks(clip,trace).Where(c=>c.Enabled).Select(c=>c.Time).Order().ToArray();
        var zooms = EffectCues.Zooms(clip,trace).Where(z=>z.Enabled).OrderBy(z=>z.Start).ToArray();
        var continuous = e.ZoomCues == null && e.Follow is FollowMode.Always or FollowMode.Manual;
        var activeZooms = new List<ZoomCue>(); var nextZoom = 0;
        int index = 0; double sx = .5, sy = .5, cx = .5, cy = .5, lastMove = 0, lastClick = -100;
        // Simulate from the source's beginning so split and trimmed clips preserve the same cursor smoothing.
        // The simulation step is source time: output frame i shows source time offset + i/(fps·Speed).
        var speed = clip.Speed <= 0 ? 1 : clip.Speed;
        var step = 1 / (fps * speed);
        var end = clip.SourceOffset + clip.Duration / speed;
        var cursorStep = e.CursorSmoothSeconds == 0 ? 1 : 1-Math.Exp(-step/e.CursorSmoothSeconds);
        var followStep = 1-Math.Exp(-step/e.FollowSeconds);
        int output = 0, clickIndex = 0, n = 0;
        while (output < count)
        {
            var time = n * step; n++;
            if (time > end + step) break;
            while (index + 1 < samples.Count && samples[index + 1].Time <= time) index++;
            var a = samples.Count > 0 ? samples[index] : null; var b = index + 1 < samples.Count ? samples[index+1] : a;
            var mix = a == null || b == null || b.Time <= a.Time ? 0 : Math.Clamp((time-a.Time)/(b.Time-a.Time),0,1);
            var x = a == null ? .5 : a.X + ((b?.X ?? a.X)-a.X)*mix; var y = a == null ? .5 : a.Y + ((b?.Y ?? a.Y)-a.Y)*mix;
            if (a?.Visible == false) { x = sx; y = sy; }
            if (n == 1) { sx = x; sy = y; }
            if (Math.Abs(x-sx)+Math.Abs(y-sy) > .002) lastMove = time;
            sx += (x-sx)*cursorStep; sy += (y-sy)*cursorStep;
            while (clickIndex < clicks.Length && clicks[clickIndex] <= time) lastClick = clicks[clickIndex++];
            double zoom = 1;
            if (continuous) zoom=e.Zoom;
            else if (e.Follow != FollowMode.Off)
            {
                while(nextZoom<zooms.Length && zooms[nextZoom].Start<=time) activeZooms.Add(zooms[nextZoom++]);
                activeZooms.RemoveAll(z=>z.End<time);
                foreach(var cue in activeZooms)
                {
                    var transition=Math.Min(e.TransitionSeconds,cue.Duration/2);
                    var envelope=Math.Min(Smooth((time-cue.Start)/transition),Smooth((cue.End-time)/transition));
                    zoom=Math.Max(zoom,1+((cue.Amount==0 ? e.Zoom : cue.Amount)-1)*envelope);
                }
            }
            var targetX = e.Follow == FollowMode.Manual ? e.FocusX : sx; var targetY = e.Follow == FollowMode.Manual ? e.FocusY : sy;
            cx += (targetX-cx)*followStep; cy += (targetY-cy)*followStep;
            var centerX = Math.Clamp(cx,.5/zoom,1-.5/zoom); var centerY = Math.Clamp(cy,.5/zoom,1-.5/zoom);
            if (time + .000001 < clip.SourceOffset) continue;
            var local = (time - clip.SourceOffset) * speed;
            var fade = e.FadeSeconds == 0 ? 1 : Math.Min(Smooth(local/e.FadeSeconds),Smooth((clip.Duration-local)/e.FadeSeconds));
            var opacity = fade * KeyframeTrack.Evaluate(clip.OpacityKeys,local,1);
            if (output < count) frames[output++] = new MotionFrame(sx,sy,a?.Visible == true,time-lastClick,zoom,centerX,centerY,opacity) { CursorHidden=e.HideIdleCursor && time-lastMove>=2, Scale=KeyframeTrack.Evaluate(clip.ScaleKeys,local,clip.Scale), PositionX=KeyframeTrack.Evaluate(clip.XKeys,local,clip.X), PositionY=KeyframeTrack.Evaluate(clip.YKeys,local,clip.Y) };
        }
        if (output == 0) frames[0] = new(.5,.5,false,100,1,.5,.5,1);
        for (int i = Math.Max(1,output); i < frames.Length; i++) frames[i] = frames[i-1];
    }
    private static double Smooth(double t) { t = Math.Clamp(t,0,1); return t*t*(3-2*t); }
    public static double AudioEnvelope(TimelineClip clip,double local)
    {
        var level = 1d;
        var fadeIn = Math.Min(clip.FadeIn,clip.Duration); var fadeOut = Math.Min(clip.FadeOut,clip.Duration);
        if (fadeIn > 0) level = Math.Min(level,local / fadeIn);
        if (fadeOut > 0) level = Math.Min(level,(clip.Duration - local) / fadeOut);
        return Math.Clamp(level,0,1);
    }
    public MotionFrame At(double time) => frames[Math.Clamp((int)Math.Round(time*fps),0,frames.Length-1)];
}

