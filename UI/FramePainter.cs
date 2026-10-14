using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio.Models;
using ScreenStudio.Services;
namespace ScreenStudio.UI;

public static class FramePainter
{
    private static readonly Geometry Arrow = Geometry.Parse("M 0,0 L 0,25 L 6,19 L 11,30 L 16,28 L 11,17 L 20,17 Z");
    public static Brush Background(StudioProject project) => project.GradientBackground
        ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString(project.Background),(Color)ColorConverter.ConvertFromString(project.BackgroundEnd),45)
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString(project.Background));
    public static int Bleed(ClipEffects effects) => effects.Shadow > 0 ? 32 : (int)Math.Ceiling(effects.BorderWidth/2);
    // Variante para clips con keyframes de escala/posición: el intermediario tiene el
    // tamaño de la caja de contenido y la geometría animada se pinta en coordenadas
    // absolutas de lienzo. El overlay del grafo centra la caja, con origen en
    // ((projectW-boxW)/2, (projectH-boxH)/2); el pintor suma el bleed.
    public static void DrawTransformed(DrawingContext dc,double projectW,double projectH,double boxW,double boxH,double sourceW,double sourceH,MotionFrame frame,ClipEffects e,Action<DrawingContext,Rect> video)
    {
        var bleed = Bleed(e);
        var boxOx = (projectW - boxW) / 2; var boxOy = (projectH - boxH) / 2;
        var ratio = Math.Min(boxW / Math.Max(1,sourceW),boxH / Math.Max(1,sourceH));
        var baseW = Math.Max(2,sourceW * ratio); var baseH = Math.Max(2,sourceH * ratio);
        var tw = baseW * frame.Scale; var th = baseH * frame.Scale;
        var tcx = bleed - boxOx + projectW * frame.PositionX;
        var tcy = bleed - boxOy + projectH * frame.PositionY;
        var placed = new Rect(tcx - tw / 2,tcy - th / 2,tw,th);
        var zoomed = new Rect(placed.X + tw * (.5 - frame.CenterX * frame.Zoom),placed.Y + th * (.5 - frame.CenterY * frame.Zoom),tw * frame.Zoom,th * frame.Zoom);
        var radius = Math.Min(e.CornerRadius,Math.Min(zoomed.Width,zoomed.Height) / 2);
        dc.PushOpacity(frame.Opacity);
        if(frame.Reveal<1)
        {
            var revealed=Math.Clamp(frame.Reveal,0,1)*zoomed.Width;
            dc.PushClip(new RectangleGeometry(new Rect(frame.RevealDirection>0?zoomed.Right-revealed:zoomed.Left,zoomed.Top,revealed,zoomed.Height)));
        }
        if (e.Shadow > 0)
        {
            for (int i = 18; i > 0; i--)
            {
                var shadow = new SolidColorBrush(Color.FromArgb((byte)(e.Shadow * 255 / 26),0,0,0));
                dc.DrawRoundedRectangle(shadow,null,new Rect(zoomed.X-i,zoomed.Y-i+5,zoomed.Width+2*i,zoomed.Height+2*i),radius+i,radius+i);
            }
        }
        dc.PushClip(new RectangleGeometry(new Rect(zoomed.X,zoomed.Y,Math.Max(1,zoomed.Width),Math.Max(1,zoomed.Height)),radius,radius));
        video(dc,zoomed);
        var x = zoomed.X + frame.X*zoomed.Width; var y = zoomed.Y + frame.Y*zoomed.Height;
        if (e.ShowClicks && frame.Visible && frame.ClickAge >= 0 && frame.ClickAge < .35)
        {
            var progress = frame.ClickAge/.35; var color = new SolidColorBrush(Color.FromArgb((byte)(180*(1-progress)),160,210,120));
            dc.DrawEllipse(null,new Pen(color,Math.Max(2,zoomed.Height/200)),new Point(x,y),(10+20*progress)*zoomed.Height/720,(10+20*progress)*zoomed.Height/720);
        }
        if (frame.Visible && e.ShowCursor && !frame.CursorHidden)
        {
            var cursorHeight = e.CursorSize * zoomed.Height/1080;
            if (CursorArtwork.Get(e) is { } custom)
            {
                var cursorWidth = cursorHeight * custom.PixelWidth / custom.PixelHeight;
                dc.DrawImage(custom,new Rect(x-e.CursorHotspotX*cursorWidth,y-e.CursorHotspotY*cursorHeight,cursorWidth,cursorHeight));
            }
            else
            {
                var size = cursorHeight / 30;
                dc.PushTransform(new TranslateTransform(x,y)); dc.PushTransform(new ScaleTransform(size,size));
                dc.DrawGeometry(Brushes.White,new Pen(Brushes.Black,2),Arrow); dc.Pop(); dc.Pop();
            }
        }
        DrawBlur(dc,e,zoomed,zoomed.Width,zoomed.Height,zoomed,video);
        dc.Pop();
        if(frame.Blackout>0) { dc.PushOpacity(frame.Blackout); dc.DrawRoundedRectangle(Brushes.Black,null,new Rect(zoomed.X,zoomed.Y,Math.Max(1,zoomed.Width),Math.Max(1,zoomed.Height)),radius,radius); dc.Pop(); }
        if (e.BorderWidth > 0) dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(e.BorderColor)),e.BorderWidth),new Rect(zoomed.X,zoomed.Y,Math.Max(1,zoomed.Width),Math.Max(1,zoomed.Height)),radius,radius);
        if(frame.Reveal<1) dc.Pop();
        dc.Pop();
    }
    private static void DrawBlur(DrawingContext dc,ClipEffects e,Rect rect,double width,double height,Rect zoomed,Action<DrawingContext,Rect> video)
    {
        if (!e.BlurEnabled) return;
        var bw = width * e.BlurW; var bh = height * e.BlurH;
        if (bw < 4 || bh < 4) return;
        var bx = Math.Clamp(rect.X + width * e.BlurX,rect.X,rect.Right - 1); var by = Math.Clamp(rect.Y + height * e.BlurY,rect.Y,rect.Bottom - 1);
        bw = Math.Min(bw,rect.Right - bx); bh = Math.Min(bh,rect.Bottom - by);
        if (bw < 4 || bh < 4) return;
        // Render the region small, blur cheaply there and upscale: smooth enough for a blur, fast enough for preview.
        var factor = 4; var sw = Math.Max(2,(int)bw / factor); var sh = Math.Max(2,(int)bh / factor);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) video(context,new Rect((zoomed.X - bx) * sw / bw,(zoomed.Y - by) * sh / bh,zoomed.Width * sw / bw,zoomed.Height * sh / bh));
        var small = new RenderTargetBitmap(sw,sh,96,96,PixelFormats.Pbgra32); small.Render(visual);
        var pixels = new byte[sw*sh*4]; small.CopyPixels(pixels,sw*4,0);
        BoxBlur(pixels,sw,sh,Math.Max(1,(int)Math.Round(e.BlurAmount / factor)));
        var blurred = new WriteableBitmap(sw,sh,96,96,PixelFormats.Bgra32,null); blurred.WritePixels(new Int32Rect(0,0,sw,sh),pixels,sw*4,0);
        dc.DrawImage(blurred,new Rect(bx,by,bw,bh));
    }
    private static void BoxBlur(byte[] pixels,int width,int height,int radius)
    {
        var copy = new byte[pixels.Length]; Array.Copy(pixels,copy,pixels.Length);
        var tmp = new int[pixels.Length];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    long b = 0, g = 0, r = 0, a = 0; var n = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var xx = Math.Clamp(x + k,0,width - 1); var i = (row + xx) * 4;
                        b += copy[i]; g += copy[i+1]; r += copy[i+2]; a += copy[i+3]; n++;
                    }
                    var o = (row + x) * 4; tmp[o] = (int)(b / n); tmp[o+1] = (int)(g / n); tmp[o+2] = (int)(r / n); tmp[o+3] = (int)(a / n);
                }
            }
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    long b = 0, g = 0, r = 0, a = 0; var n = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var yy = Math.Clamp(y + k,0,height - 1); var i = (yy * width + x) * 4;
                        b += tmp[i]; g += tmp[i+1]; r += tmp[i+2]; a += tmp[i+3]; n++;
                    }
                    var o = (y * width + x) * 4; copy[o] = (byte)(b / n); copy[o+1] = (byte)(g / n); copy[o+2] = (byte)(r / n); copy[o+3] = (byte)(a / n);
                }
            }
        }
        Array.Copy(copy,pixels,pixels.Length);
    }
    public static void Draw(DrawingContext dc,double width,double height,MotionFrame frame,ClipEffects e,Action<DrawingContext,Rect> video)
    {
        var bleed = Bleed(e); var rect = new Rect(bleed,bleed,width,height); var radius = Math.Min(e.CornerRadius,Math.Min(width,height)/2);
        dc.PushOpacity(frame.Opacity);
        if(frame.Reveal<1)
        {
            var revealed=Math.Clamp(frame.Reveal,0,1)*width;
            dc.PushClip(new RectangleGeometry(new Rect(frame.RevealDirection>0?rect.Right-revealed:rect.Left,rect.Top,revealed,height)));
        }
        if (e.Shadow > 0)
        {
            for (int i = 18; i > 0; i--)
            {
                var shadow = new SolidColorBrush(Color.FromArgb((byte)(e.Shadow * 255 / 26),0,0,0));
                dc.DrawRoundedRectangle(shadow,null,new Rect(rect.X-i,rect.Y-i+5,width+2*i,height+2*i),radius+i,radius+i);
            }
        }
        dc.PushClip(new RectangleGeometry(rect,radius,radius));
        var zoomed = new Rect(rect.X + width*(.5-frame.CenterX*frame.Zoom),rect.Y + height*(.5-frame.CenterY*frame.Zoom),width*frame.Zoom,height*frame.Zoom);
        video(dc,zoomed);
        DrawBlur(dc,e,rect,width,height,zoomed,video);
        var x = zoomed.X + frame.X*zoomed.Width; var y = zoomed.Y + frame.Y*zoomed.Height;
        if (e.ShowClicks && frame.Visible && frame.ClickAge >= 0 && frame.ClickAge < .35)
        {
            var progress = frame.ClickAge/.35; var color = new SolidColorBrush(Color.FromArgb((byte)(180*(1-progress)),160,210,120));
            dc.DrawEllipse(null,new Pen(color,Math.Max(2,height/200)),new Point(x,y),(10+20*progress)*height/720,(10+20*progress)*height/720);
        }
        if (frame.Visible && e.ShowCursor && !frame.CursorHidden)
        {
            var cursorHeight = e.CursorSize * height/1080;
            if (CursorArtwork.Get(e) is { } custom)
            {
                var cursorWidth = cursorHeight * custom.PixelWidth / custom.PixelHeight;
                dc.DrawImage(custom,new Rect(x-e.CursorHotspotX*cursorWidth,y-e.CursorHotspotY*cursorHeight,cursorWidth,cursorHeight));
            }
            else
            {
                var size = cursorHeight / 30;
                dc.PushTransform(new TranslateTransform(x,y)); dc.PushTransform(new ScaleTransform(size,size));
                dc.DrawGeometry(Brushes.White,new Pen(Brushes.Black,2),Arrow); dc.Pop(); dc.Pop();
            }
        }
        dc.Pop();
        if(frame.Blackout>0) { dc.PushOpacity(frame.Blackout); dc.DrawRoundedRectangle(Brushes.Black,null,rect,radius,radius); dc.Pop(); }
        if (e.BorderWidth > 0) dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(e.BorderColor)),e.BorderWidth),rect,radius,radius);
        if(frame.Reveal<1) dc.Pop();
        dc.Pop();
    }
}
public sealed class ClipSurface : FrameworkElement
{
    public ImageSource? Poster { get; set; }
    public MediaPlayer? Player { get; set; }
    public bool Ready { get; set; }
    public double ContentWidth { get; set; }
    public double ContentHeight { get; set; }
    public ClipEffects Effects { get; set; } = new();
    public MotionFrame Frame { get; set; } = new(.5,.5,false,100,1,.5,.5,1);
    protected override void OnRender(DrawingContext dc) => FramePainter.Draw(dc,ContentWidth,ContentHeight,Frame,Effects,(context,rect) =>
    {
        if (Poster != null) context.DrawImage(Poster,rect);
        if (Ready && Player != null) context.DrawVideo(Player,rect);
    });
}
