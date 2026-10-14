using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio.Models;
namespace ScreenStudio.Services;

public static class TextArtwork
{
    public static Task<byte[]> BackgroundAsync(StudioProject project)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(UI.FramePainter.Background(project),null,new Rect(0,0,project.Width,project.Height));
                var image = new RenderTargetBitmap(project.Width,project.Height,96,96,PixelFormats.Pbgra32); image.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new MemoryStream(); encoder.Save(stream); completion.SetResult(stream.ToArray());
            }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    public static Task<byte[]> RenderAsync(TimelineClip clip, int width, int height)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new Grid { Width = width, Height = height, Background = Brushes.Transparent };
                canvas.Children.Add(new TextBlock { Text = clip.Text, FontFamily = new FontFamily("Segoe UI"), FontSize = clip.FontSize, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)new BrushConverter().ConvertFromString(clip.TextColor)!, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    MaxWidth = width * .9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
                var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(canvas);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream(); encoder.Save(stream); completion.SetResult(stream.ToArray());
            }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    public static BitmapImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    // Plan B cuando ffmpeg no puede generar el póster: decodificación directa de WPF
    // (cubre los formatos con códec de sistema y casos donde el decodificador falla).
    public static BitmapImage? PosterFromFile(string path)
    {
        try
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".jfif" or ".bmp" or ".tif" or ".tiff" or ".gif" or ".ico")) return null;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 1280; image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze(); return image;
        }
        catch { return null; }
    }
}
