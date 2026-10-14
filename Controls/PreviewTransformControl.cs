using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio.Controls;

public record VisualTransform(Guid ClipId,double Scale,double X,double Y);
public sealed class PreviewTransformControl : FrameworkElement
{
    private static readonly (int X,int Y)[] Handles=[(-1,-1),(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0)];
    private sealed record Gesture(TimelineClip Clip,Rect Bounds,Point Pointer,int Handle);
    private Gesture? gesture;
    private VisualTransform? draft;
    public Canvas? Stage { get; set; }
    public PreviewRenderer? Renderer { get; set; }
    public EditingSession? Session { get; set; }
    public Guid? SelectedClipId { get; set; }
    public event EventHandler<Guid?>? SelectionRequested;
    public event EventHandler? TransformStarted;
    public event EventHandler<VisualTransform>? TransformPreviewed;
    public event EventHandler<VisualTransform>? TransformCommitted;
    public event EventHandler<VisualTransform>? TransformCancelled;
    public bool IsTransforming=>gesture!=null;
    public PreviewTransformControl()
    {
        Focusable=true; ClipToBounds=false;
        IsEnabledChanged+=(_,_)=> { if(!IsEnabled) CancelGesture(); InvalidateVisual(); };
    }
    protected override Size MeasureOverride(Size constraint)=>new(0,0);
    private Point ToStage(Point point)=>Stage==null?point:TransformToVisual(Stage).Transform(point);
    private Rect? SelectedBounds()
    {
        if(Stage==null || Renderer==null || SelectedClipId is not Guid id || Renderer.VisualBounds(id) is not {} bounds) return null;
        return Stage.TransformToVisual(this).TransformBounds(bounds);
    }
    public Rect? SelectionBounds=>SelectedBounds();
    private static Point HandlePoint(Rect r,int index)
    {
        var (x,y)=Handles[index]; return new Point(r.Left+(x+1)*r.Width/2,r.Top+(y+1)*r.Height/2);
    }
    private int HandleAt(Point point)
    {
        if(SelectedBounds() is not {} bounds) return -1;
        for(int i=0;i<Handles.Length;i++) if(new Rect(HandlePoint(bounds,i)-new Vector(7,7),new Size(14,14)).Contains(point)) return i;
        return -1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        if(!IsEnabled || SelectedBounds() is not {} bounds) return;
        var accent=TryFindResource("Accent") as Brush ?? Brushes.LightGreen;
        var surface=TryFindResource("Surface") as Brush ?? Brushes.Black;
        dc.DrawRectangle(null,new Pen(accent,1.5),bounds);
        for(int i=0;i<Handles.Length;i++) dc.DrawRectangle(surface,new Pen(accent,1.5),new Rect(HandlePoint(bounds,i)-new Vector(4.5,4.5),new Size(9,9)));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(BeginGesture(e.GetPosition(this))) e.Handled=true;
    }
    private bool BeginGesture(Point point)
    {
        if(!IsEnabled || Renderer==null || Session==null || Stage==null) return false;
        Focus(); var handle=HandleAt(point); var stagePoint=ToStage(point);
        var id=handle>=0?SelectedClipId:Renderer.HitVisual(stagePoint);
        SelectedClipId=id; SelectionRequested?.Invoke(this,id); InvalidateVisual();
        if(id is not Guid clipId || Session.Clip(clipId) is not {} clip || Renderer.VisualBounds(clipId) is not {} bounds) return false;
        TransformStarted?.Invoke(this,EventArgs.Empty);
        gesture=new(clip.Clone(),bounds,stagePoint,handle); draft=new(clip.Id,clip.Scale,clip.X,clip.Y);
        if(!CaptureMouse()) { CancelGesture(); return false; }
        return true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point=e.GetPosition(this);
        if(gesture!=null && IsMouseCaptured)
        {
            if(e.LeftButton==MouseButtonState.Pressed) UpdateGesture(point);
            e.Handled=true; return;
        }
        var handle=HandleAt(point);
        Cursor=handle is 0 or 4?Cursors.SizeNWSE:handle is 2 or 6?Cursors.SizeNESW:handle is 1 or 5?Cursors.SizeNS:handle is 3 or 7?Cursors.SizeWE:
            Renderer?.HitVisual(ToStage(point))!=null?Cursors.SizeAll:Cursors.Arrow;
    }
    private void UpdateGesture(Point point)
    {
        if(gesture is not {} g || Session==null) return;
        var project=Session.Project; var delta=ToStage(point)-g.Pointer;
        var scale=g.Clip.Scale; var center=new Point(project.Width*g.Clip.X,project.Height*g.Clip.Y);
        if(g.Handle<0) center+=delta;
        else
        {
            var (hx,hy)=Handles[g.Handle]; var bounds=g.Bounds;
            var anchor=new Point(hx<0?bounds.Right:hx>0?bounds.Left:bounds.Left+bounds.Width/2,hy<0?bounds.Bottom:hy>0?bounds.Top:bounds.Top+bounds.Height/2);
            double ratio;
            if(hx==0) ratio=(bounds.Height+hy*delta.Y)/bounds.Height;
            else if(hy==0) ratio=(bounds.Width+hx*delta.X)/bounds.Width;
            else ratio=(bounds.Width*(bounds.Width+hx*delta.X)+bounds.Height*(bounds.Height+hy*delta.Y))/(bounds.Width*bounds.Width+bounds.Height*bounds.Height);
            scale=Math.Clamp(g.Clip.Scale*ratio,.05,2); ratio=scale/g.Clip.Scale;
            center=new Point(anchor.X+hx*bounds.Width*ratio/2,anchor.Y+hy*bounds.Height*ratio/2);
        }
        draft=new(g.Clip.Id,scale,Math.Clamp(center.X/project.Width,0,1),Math.Clamp(center.Y/project.Height,0,1));
        TransformPreviewed?.Invoke(this,draft); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(gesture==null) return; UpdateGesture(e.GetPosition(this)); EndGesture(); e.Handled=true;
    }
    private void EndGesture()
    {
        if(gesture==null) return;
        var value=draft; gesture=null; draft=null; ReleaseMouseCapture();
        if(value!=null) TransformCommitted?.Invoke(this,value);
        InvalidateVisual();
    }
    public void CancelGesture()
    {
        if(gesture is not {} g) return;
        gesture=null; draft=null;
        TransformCancelled?.Invoke(this,new(g.Clip.Id,g.Clip.Scale,g.Clip.X,g.Clip.Y));
        if(IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); CancelGesture(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape && IsTransforming) { CancelGesture(); e.Handled=true; return; }
        base.OnKeyDown(e);
    }
}
