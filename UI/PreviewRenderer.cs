using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenStudio.Models;
using ScreenStudio.Services;
namespace ScreenStudio.UI;

public sealed class PreviewRenderer(Canvas stage, Panel audioHost, VideoEngine engine) : IDisposable
{
    private sealed class Layer(TimelineClip clip,FrameworkElement host,MediaPlayer? player,MotionPlan motion)
    {
        public TimelineClip Clip = clip;
        public FrameworkElement Host = host;
        public MediaPlayer? Player = player;
        public MotionPlan Motion = motion;
        public bool Active, WantPlay;
        public double Desired;
        public TransitionSpan? Transition;
    }
    private readonly List<Layer> layers = [];
    private int frameRate=30;
    private StudioProject? built;
    public HashSet<Guid> Held { get; } = [];
    public Dictionary<string,ImageSource> Thumbnails { get; } = [];
    public event EventHandler<string>? PlaybackError;
    public async Task<List<(string Path, string Error)>> WarmLibraryAsync(IEnumerable<MediaAsset> library)
    {
        var failed = new List<(string, string)>();
        foreach (var asset in library.Where(a => a.Kind != ClipKind.Audio && !Thumbnails.ContainsKey(a.Path)))
        {
            try
            {
                var (bytes, error, _) = await engine.ReadPosterAsync(asset.Path,0);
                if (bytes.Length > 0) Thumbnails[asset.Path] = TextArtwork.Decode(bytes);
                else if (TextArtwork.PosterFromFile(asset.Path) is { } fallback) Thumbnails[asset.Path] = fallback;
                else failed.Add((asset.Path, error));
            }
            catch (Exception e) { failed.Add((asset.Path, e.Message)); }
        }
        return failed;
    }
    public async Task<List<(string Path, string Error)>> BuildAsync(StudioProject project)
    {
        Clear(); built = project; stage.Width = project.Width; stage.Height = project.Height; stage.Background = FramePainter.Background(project); frameRate=project.Output.FrameRate;
        var transitions=TransitionPlan.Create(project);
        var failed = await WarmLibraryAsync(project.Library);
        foreach (var track in project.Tracks.Where(t => t.Enabled).Reverse())
        foreach (var clip in track.Clips.OrderBy(c=>c.Start))
        {
            var asset = project.Library.FirstOrDefault(a => a.Path == clip.SourcePath); ImageSource? poster = null;
            var key = clip.Kind == ClipKind.Text ? clip.Id.ToString() : clip.SourcePath;
            if (clip.Kind == ClipKind.Text) { poster = TextArtwork.Decode(await TextArtwork.RenderAsync(clip,project.Width,project.Height)); Thumbnails[key] = poster; }
            else if (clip.Kind != ClipKind.Audio && !Thumbnails.TryGetValue(key,out poster))
            {
                var (bytes, _, _) = await engine.ReadPosterAsync(clip.SourcePath,0);
                if (bytes.Length > 0) { poster = TextArtwork.Decode(bytes); Thumbnails[key] = poster; }
                else if (TextArtwork.PosterFromFile(clip.SourcePath) is { } fallback) { poster = fallback; Thumbnails[key] = poster; }
            }
            MediaPlayer? player = clip.Kind is ClipKind.Video or ClipKind.Audio ? new MediaPlayer { Volume = clip.Kind == ClipKind.Audio ? clip.Volume : 0, ScrubbingEnabled = true, SpeedRatio = clip.Speed <= 0 ? 1 : clip.Speed } : null;
            FrameworkElement host;
            if (clip.Kind == ClipKind.Audio) { host = new Grid { Width = 1, Height = 1 }; audioHost.Children.Add(host); }
            else
            {
                var surface = new ClipSurface { Poster=poster,Player=player,Effects=clip.Effects,Opacity=clip.Opacity };
                Place(project,clip,surface,clip.Scale,clip.X,clip.Y); host=surface; stage.Children.Add(host);
            }
            host.Visibility = Visibility.Hidden;
            var layer = new Layer(clip,host,player,new MotionPlan(clip,clip.Kind == ClipKind.Video ? asset?.Cursor : null,project.Output.FrameRate)); layers.Add(layer);
            layer.Transition=transitions.GetValueOrDefault(clip.Id);
            if (player != null)
            {
                player.MediaOpened += (_,_) => { if (!layers.Contains(layer)) return; if (host is ClipSurface surface) { surface.Ready = true; surface.InvalidateVisual(); } player.Position = TimeSpan.FromSeconds(layer.Desired); if (layer.WantPlay) player.Play(); else player.Pause(); };
                player.MediaFailed += (_,error) => { if (layers.Contains(layer)) PlaybackError?.Invoke(this,"Windows no puede reproducir " + clip.Name + ": " + error.ErrorException.Message); };
                player.Open(new Uri(clip.SourcePath)); player.Play(); player.Pause();
            }
        }
        return failed;
    }
    public void Update(double time,bool playing,bool seek = false)
    {
        foreach (var layer in layers)
        {
            var active = time >= (layer.Transition?.Start ?? layer.Clip.Start) && time < (layer.Transition?.End ?? layer.Clip.End); layer.Host.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            layer.WantPlay = active && playing && time>=layer.Clip.Start && time<layer.Clip.End;
            if (layer.Host is ClipSurface surface)
            {
                var frame=layer.Motion.At(time-layer.Clip.Start); surface.Frame = layer.Transition?.Decorate(frame,time) ?? frame; surface.InvalidateVisual();
                if (built != null && layer.Clip.HasTransformKeys && !Held.Contains(layer.Clip.Id)) Place(built,layer.Clip,surface,frame.Scale,frame.PositionX,frame.PositionY);
            }
            if (layer.Player != null)
            {
                var speed = layer.Clip.Speed <= 0 ? 1 : layer.Clip.Speed;
                layer.Desired = layer.Clip.SourceOffset+Math.Clamp(time-layer.Clip.Start,0,Math.Max(0,layer.Clip.Duration-1d/frameRate)) / speed;
                if (layer.Clip.Kind == ClipKind.Audio) layer.Player.Volume = MotionPlan.AudioEnvelope(layer.Clip,Math.Clamp(time-layer.Clip.Start,0,layer.Clip.Duration)) * KeyframeTrack.Evaluate(layer.Clip.VolumeKeys,Math.Clamp(time-layer.Clip.Start,0,layer.Clip.Duration),layer.Clip.Volume);
                if (active && (!layer.Active || seek)) layer.Player.Position = TimeSpan.FromSeconds(layer.Desired);
                if (layer.WantPlay) layer.Player.Play(); else layer.Player.Pause();
            }
            layer.Active = active;
        }
    }
    public void RefreshEffects(StudioProject project,Guid clipId,double time,bool playing)
    {
        var transitions=TransitionPlan.Create(project); foreach(var layer in layers) layer.Transition=transitions.GetValueOrDefault(layer.Clip.Id);
        foreach(var layer in layers.Where(l=>l.Clip.Id==clipId))
        {
            var clip=project.Clips.First(c=>c.Id==clipId); layer.Clip=clip;
            layer.Motion=new MotionPlan(clip,project.Library.FirstOrDefault(a=>a.Path==clip.SourcePath)?.Cursor,project.Output.FrameRate);
            if(layer.Host is ClipSurface surface) surface.Effects=clip.Effects;
            if(layer.Player != null) layer.Player.SpeedRatio = clip.Speed <= 0 ? 1 : clip.Speed;
        }
        Update(time,playing);
    }
    private static void Place(StudioProject project,TimelineClip clip,ClipSurface surface,double scale,double x,double y)
    {
        var asset=project.Library.FirstOrDefault(a=>a.Path==clip.SourcePath);
        var sourceW=clip.Kind==ClipKind.Text?project.Width:asset?.Width ?? surface.Poster?.Width ?? project.Width;
        var sourceH=clip.Kind==ClipKind.Text?project.Height:asset?.Height ?? surface.Poster?.Height ?? project.Height;
        var ratio=Math.Min((project.Width-2d*project.Padding)*scale/Math.Max(1,sourceW),(project.Height-2d*project.Padding)*scale/Math.Max(1,sourceH));
        var w=Math.Max(2,Math.Floor(sourceW*ratio/2)*2); var h=Math.Max(2,Math.Floor(sourceH*ratio/2)*2); var bleed=FramePainter.Bleed(clip.Effects);
        surface.ContentWidth=w; surface.ContentHeight=h; surface.Width=w+2*bleed; surface.Height=h+2*bleed;
        Canvas.SetLeft(surface,project.Width*x-surface.Width/2); Canvas.SetTop(surface,project.Height*y-surface.Height/2); surface.InvalidateVisual();
    }
    public Rect? VisualBounds(Guid clipId)
    {
        var layer=layers.FirstOrDefault(l=>l.Clip.Id==clipId && l.Host.Visibility==Visibility.Visible);
        if(layer?.Host is not ClipSurface surface) return null;
        var bleed=FramePainter.Bleed(surface.Effects);
        return new Rect(Canvas.GetLeft(surface)+bleed,Canvas.GetTop(surface)+bleed,surface.ContentWidth,surface.ContentHeight);
    }
    public Guid? HitVisual(Point point)=>layers.AsEnumerable().Reverse().FirstOrDefault(l=>l.Clip.Opacity>0 && VisualBounds(l.Clip.Id)?.Contains(point)==true)?.Clip.Id;
    public void SetVisualTransform(StudioProject project,Guid clipId,double scale,double x,double y)
    {
        var layer=layers.FirstOrDefault(l=>l.Clip.Id==clipId);
        if(layer?.Host is not ClipSurface surface) return;
        var clip=project.Clips.FirstOrDefault(c=>c.Id==clipId) ?? layer.Clip;
        layer.Clip=clip; Place(project,clip,surface,scale,x,y);
    }
    private void Clear() { Held.Clear(); var old = layers.ToArray(); layers.Clear(); foreach (var layer in old) layer.Player?.Close(); stage.Children.Clear(); audioHost.Children.Clear(); }
    public void Dispose() => Clear();
}
