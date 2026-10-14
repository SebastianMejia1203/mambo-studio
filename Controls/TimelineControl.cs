using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenStudio.Models;
using ScreenStudio.Services;
namespace ScreenStudio.Controls;

public record TimelineEdit(string Operation, Guid ClipId, Guid TrackId, double Start, double Duration = 0, double Offset = 0);
public record TimelineDrop(string[] Paths, Guid TrackId, double Time);

public sealed partial class TimelineControl : FrameworkElement
{
    public const double HeaderWidth = 144, RulerHeight = 30, TrackHeight = 62;
    public EditingSession? Session { get; set; }
    public Guid? SelectedClipId { get; set; }
    public Guid? SelectedTrackId { get; set; }
    public EditTool Tool { get; set; }
    public double PixelsPerSecond { get; set; } = 64;
    public double ViewportWidth { get; set; } = 900;
    public bool Snap { get; set; } = true;
    public Dictionary<string, ImageSource> Thumbnails { get; set; } = [];
    public Dictionary<Guid, (string Path, float[] Peaks)> WaveformCache { get; } = [];
    public Func<string, Task<float[]>>? WaveformLoader { get; set; }
    private readonly HashSet<Guid> waveformLoading = [];
    public event EventHandler? SelectionChanged;
    public event EventHandler? PositionChanged;
    public event EventHandler<TimelineEdit>? EditRequested;
    public event EventHandler<TimelineDrop>? FilesDropped;
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(TimelineControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, (o, _) => ((TimelineControl)o).PositionChanged?.Invoke(o, EventArgs.Empty)));
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, Math.Max(0, value)); }
    private TimelineClip? original, ghost;
    private Guid ghostTrack;
    private bool ghostValid, moved, seeking;
    private Point down;
    private int trimSide;
    private Brush B(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
    public TimelineControl()
    {
        Focusable = true; AllowDrop = true; ClipToBounds = true;
        IsEnabledChanged += (_, _) => InvalidateVisual();
    }
    public void Refresh()
    {
        if (Session != null) foreach (var id in WaveformCache.Keys.Where(id => Session.Clip(id) == null).ToArray()) WaveformCache.Remove(id);
        if(SelectedEffect is {} selected)
        {
            var clip=Session?.Clip(selected.ClipId);
            var trace=clip==null?null:Trace(clip);
            var valid=clip!=null && SelectedClipId==clip.Id && selected.Kind switch
            {
                "cursor"=>trace!=null,
                "click"=>EffectCues.Clicks(clip,trace).Any(c=>c.Id==selected.CueId),
                "zoom"=>EffectCues.Zooms(clip,trace).Any(c=>c.Id==selected.CueId),
                "transitionin"=>clip.Effects.TransitionIn.Kind!=TransitionKind.Cut,
                "transitionout"=>clip.Effects.TransitionOut.Kind!=TransitionKind.Cut,
                _=>false
            };
            if(!valid) SelectedEffect=null;
        }
        InvalidateMeasure(); InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(ViewportWidth, HeaderWidth + Math.Max(12, (Session?.Project.Duration ?? 0) + 3) * PixelsPerSecond + 30), RulerHeight + TotalTrackHeight() + 12);
    public double TimeAt(double x) => Math.Max(0, (x - HeaderWidth) / PixelsPerSecond);
    private double X(double time) => HeaderWidth + time * PixelsPerSecond;
    private Rect ClipRect(TimelineClip clip, int row) => new(X(clip.Start), TrackTop(row) + 7, Math.Max(3, clip.Duration * PixelsPerSecond), TrackHeight - 14);
    private void Text(DrawingContext dc, string value, Point point, string brush, double size = 11, double maxWidth = 300)
    {
        if (maxWidth < 1) return;
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, B(brush), VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, point);
    }
    protected override void OnRender(DrawingContext dc)
    {
        effectHits.Clear();
        dc.DrawRectangle(B("Background"), null, new Rect(RenderSize));
        if (Session == null) return;
        var project = Session.Project;
        dc.DrawRectangle(B("Surface"), null, new Rect(0, 0, ActualWidth, RulerHeight));
        var intervals = new double[] { .1, .25, .5, 1, 2, 5, 10, 30, 60, 120, 300, 600, 1800 };
        var interval = intervals.FirstOrDefault(s => s * PixelsPerSecond >= 75, 1800);
        for (var t = 0d; X(t) <= ActualWidth; t += interval)
        {
            var ts = TimeSpan.FromSeconds(t);
            Text(dc, $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}" + (interval < 1 ? $".{ts.Milliseconds / 100}" : ""), new Point(X(t) + 4, 7), "Muted", 10, 70);
            dc.DrawLine(new Pen(B("Border"), 1), new Point(X(t), 24), new Point(X(t), ActualHeight));
        }
        var rangeOut = project.ExportOut > 0 ? project.ExportOut : 0;
        if (rangeOut > project.ExportIn)
        {
            var rangeRect = new Rect(X(project.ExportIn), 2, Math.Max(3,(rangeOut - project.ExportIn) * PixelsPerSecond), RulerHeight - 4);
            dc.DrawRoundedRectangle(B("AccentSoft"), new Pen(B("Accent"), 1), rangeRect, 3, 3);
            Text(dc, "Rango de exportación · Alt+X limpia", new Point(rangeRect.Left + 6, 8), "Muted", 9, Math.Max(1, rangeRect.Width - 12));
        }
        for (var row = 0; row < project.Tracks.Count; row++)
        {
            var track = project.Tracks[row]; var top = TrackTop(row);
            dc.DrawRectangle(B(track.Id == SelectedTrackId ? "Raised" : "Surface"), null, new Rect(0, top, HeaderWidth - 1, TrackHeight));
            dc.DrawLine(new Pen(B("Border"), 1), new Point(0, top + TrackHeight), new Point(ActualWidth, top + TrackHeight));
            dc.DrawRoundedRectangle(B(track.Kind == TrackKind.Audio ? "ClipAudio" : "ClipVideo"), null, new Rect(12, top + 15, 4, 30), 2, 2);
            Text(dc, track.Name, new Point(24, top + 14), track.Enabled ? "Text" : "Muted", 11, 110);
            Text(dc, track.Kind == TrackKind.Audio ? "Audio" : "Vídeo · imagen · texto", new Point(24, top + 32), "Muted", 9, 110);
            foreach (var clip in track.Clips) DrawClip(dc, clip, row, clip.Id == SelectedClipId, false);
            DrawEffects(dc,track,row);
        }
        if (ghost != null && moved)
        {
            var row = project.Tracks.FindIndex(t => t.Id == ghostTrack);
            if (row >= 0) DrawClip(dc, ghost, row, true, true);
        }
        var playhead = X(Position);
        dc.DrawLine(new Pen(B("Accent"), 1.5), new Point(playhead, 0), new Point(playhead, ActualHeight));
        var marker = new StreamGeometry(); using (var ctx = marker.Open()) { ctx.BeginFigure(new Point(playhead - 5, 0), true, true); ctx.LineTo(new Point(playhead + 5, 0), true, false); ctx.LineTo(new Point(playhead + 5, 7), true, false); ctx.LineTo(new Point(playhead, 12), true, false); ctx.LineTo(new Point(playhead - 5, 7), true, false); }
        dc.DrawGeometry(B("Accent"), null, marker);
    }
    private void DrawWaveform(DrawingContext dc, TimelineClip clip, Rect rect)
    {
        EnsureWaveform(clip);
        if (!WaveformCache.TryGetValue(clip.Id, out var cached) || cached.Path != clip.SourcePath || cached.Peaks.Length == 0) return;
        var columns = Math.Clamp((int)rect.Width, 1, 600);
        var slice = Services.Waveform.Slice(cached.Peaks, clip.SourceOffset, clip.Duration, Math.Max(clip.SourceDuration, clip.SourceOffset + clip.Duration), columns);
        var midY = rect.Top + rect.Height / 2; var maxH = Math.Max(2, rect.Height / 2 - 4);
        var full = new Pen(B("ClipTextInk"), 1); full.Freeze();
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(80, 0xED, 0xF2, 0xFA)), 1); faint.Freeze();
        var fadeInX = rect.Left + Math.Min(1, clip.FadeIn / Math.Max(.001, clip.Duration)) * rect.Width;
        var fadeOutX = rect.Right - Math.Min(1, clip.FadeOut / Math.Max(.001, clip.Duration)) * rect.Width;
        for (var i = 0; i < columns; i++)
        {
            var h = Math.Max(1, slice[i] * maxH); var x = rect.Left + i;
            var pen = x < fadeInX || x > fadeOutX ? faint : full;
            dc.DrawLine(pen, new Point(x, midY - h), new Point(x, midY + h));
        }
    }
    private void EnsureWaveform(TimelineClip clip)
    {
        if (clip.Kind != ClipKind.Audio || WaveformLoader == null || waveformLoading.Contains(clip.Id)) return;
        if (WaveformCache.TryGetValue(clip.Id, out var cached) && cached.Path == clip.SourcePath) return;
        waveformLoading.Add(clip.Id);
        var path = clip.SourcePath; var id = clip.Id; var loader = WaveformLoader;
        _ = Task.Run(async () =>
        {
            float[]? peaks = null;
            try { peaks = await loader(path); } catch { }
            var done = peaks;
            _ = Dispatcher.BeginInvoke(() => { waveformLoading.Remove(id); if (done != null) WaveformCache[id] = (path, done); InvalidateVisual(); });
        });
    }
    private void DrawClip(DrawingContext dc, TimelineClip clip, int row, bool selected, bool isGhost)
    {
        var rect = ClipRect(clip, row); var color = "Clip" + clip.Kind;
        dc.PushOpacity(isGhost ? .65 : 1);
        dc.DrawRoundedRectangle(B(color), new Pen(B(isGhost && !ghostValid ? "Danger" : selected ? "Accent" : "Border"), selected ? 2 : 1), rect, 5, 5);
        dc.PushClip(new RectangleGeometry(rect));
        var textX = rect.Left + 10;
        var key = clip.Kind == ClipKind.Text ? clip.Id.ToString() : clip.SourcePath;
        if (clip.Kind != ClipKind.Audio && Thumbnails.TryGetValue(key, out var image) && rect.Width > 85)
        {
            var width = Math.Min(60, (rect.Height - 10) * image.Width / Math.Max(1, image.Height));
            dc.DrawImage(image, new Rect(rect.Left + 6, rect.Top + 5, width, rect.Height - 10)); textX += width + 5;
        }
        Text(dc, clip.Name, new Point(textX, rect.Top + 9), "ClipTextInk", 11, Math.Max(1, rect.Right - textX - 8));
        if (clip.Kind == ClipKind.Audio) DrawWaveform(dc, clip, rect);
        Text(dc, clip.Kind == ClipKind.Audio ? $"{clip.Duration:0.0} s · audio" : $"{clip.Duration:0.0} s", new Point(textX, rect.Top + 27), "ClipTextInk", 9, Math.Max(1, rect.Right - textX - 8));
        if (selected && Tool == EditTool.Trim)
            foreach (var x in new[] { rect.Left + 4, rect.Right - 4 }) dc.DrawLine(new Pen(B("Accent"), 3), new Point(x, rect.Top + 10), new Point(x, rect.Bottom - 10));
        dc.Pop(); dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled || Session == null) return;
        Focus(); down = e.GetPosition(this); moved = false; original = ghost = null; trimSide = 0;
        if (BeginEffect(down,e.ClickCount)) { e.Handled=true; return; }
        SelectedEffect=null;
        var row = RowAt(down.Y);
        if (down.Y < RulerHeight) { seeking = true; CaptureMouse(); Position = TimeAt(down.X); return; }
        if (row < 0 || row >= Session.Project.Tracks.Count) return;
        var track = Session.Project.Tracks[row]; SelectedTrackId = track.Id;
        var clip = track.Clips.FirstOrDefault(c => ClipRect(c, row).Contains(down));
        SelectedClipId = clip?.Id; SelectionChanged?.Invoke(this, EventArgs.Empty); InvalidateVisual();
        if (down.X < HeaderWidth) return;
        if (clip == null) { seeking = true; CaptureMouse(); Position = TimeAt(down.X); return; }
        if (Tool == EditTool.Split) { EditRequested?.Invoke(this, new("split", clip.Id, track.Id, TimeAt(down.X))); return; }
        original = clip.Clone(); ghost = clip.Clone(); ghostTrack = track.Id; ghostValid = true;
        if (Tool == EditTool.Trim) trimSide = Math.Abs(down.X - X(clip.Start)) < Math.Abs(down.X - X(clip.End)) ? -1 : 1;
        CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (MoveEffect(e.GetPosition(this))) { e.Handled=true; return; }
        if (!IsMouseCaptured || Session == null) return;
        var point = e.GetPosition(this);
        if (seeking) { Position = TimeAt(point.X); return; }
        if (original == null || ghost == null) return;
        moved |= Math.Abs(point.X - down.X) > 3 || Math.Abs(point.Y - down.Y) > 3;
        var frame = 1d / Session.Project.Output.FrameRate;
        var delta = Math.Round((point.X - down.X) / PixelsPerSecond / frame) * frame;
        if (trimSide == 0)
        {
            ghost.Start = Math.Max(0, original.Start + delta);
            var row = Math.Clamp(RowAt(point.Y), 0, Session.Project.Tracks.Count - 1);
            ghostTrack = Session.Project.Tracks[row].Id;
            if (Snap && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                var edges = Session.Project.Tracks[row].Clips.Where(c => c.Id != original.Id).SelectMany(c => new[] { c.Start, c.End }).Append(Position).Append(0);
                var candidates = edges.SelectMany(edge => new[] { edge, edge - ghost.Duration }).Where(s => s >= 0).OrderBy(s => Math.Abs(s - ghost.Start)).ToArray();
                if (candidates.Length > 0 && Math.Abs(candidates[0] - ghost.Start) * PixelsPerSecond < 8) ghost.Start = candidates[0];
            }
        }
        else if (trimSide < 0)
        {
            var media = original.Kind is ClipKind.Video or ClipKind.Audio;
            delta = Math.Clamp(delta, -(media ? Math.Min(original.SourceOffset, original.Start) : original.Start), original.Duration - Math.Max(frame, .04));
            ghost.Start = original.Start + delta; ghost.SourceOffset = media ? original.SourceOffset + delta : 0; ghost.Duration = original.Duration - delta;
        }
        else
        {
            var max = original.Kind is ClipKind.Video or ClipKind.Audio ? original.SourceDuration - original.SourceOffset : 86400 - original.Start;
            ghost.Duration = Math.Clamp(original.Duration + delta, Math.Max(frame, .04), max);
        }
        ghostValid = ghost.Duration >= .04 && Session.CanPlace(ghostTrack, ghost, ghost.Start, original.Id);
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (EndEffect()) { e.Handled=true; return; }
        if (ghost != null && moved && ghostValid) EditRequested?.Invoke(this, new(trimSide == 0 ? "move" : "trim", ghost.Id, ghostTrack, ghost.Start, ghost.Duration, ghost.SourceOffset));
        seeking = false; original = ghost = null; ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); effectDrag=null; seeking = false; original = ghost = null; InvalidateVisual(); }
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("StudioAsset") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }
    protected override void OnDrop(DragEventArgs e)
    {
        if (Session == null) return;
        var point = e.GetPosition(this); var row = RowAt(point.Y);
        if (row < 0 || row >= Session.Project.Tracks.Count) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? (e.Data.GetData("StudioAsset") is string path ? [path] : []);
        if (paths.Length > 0) FilesDropped?.Invoke(this, new(paths, Session.Project.Tracks[row].Id, TimeAt(point.X)));
        e.Handled = true;
    }
}

