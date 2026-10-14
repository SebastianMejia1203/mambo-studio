using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenStudio.Models;
namespace ScreenStudio.UI;

public static class CursorArtwork
{
    private sealed class Entry { public string? Data; public BitmapSource? Bitmap; }
    private static readonly ConditionalWeakTable<ClipEffects,Entry> Cache = new();
    public static BitmapSource? Get(ClipEffects effects)
    {
        if (effects.CursorImage == null) return null;
        var entry = Cache.GetOrCreateValue(effects);
        lock (entry)
        {
            if (entry.Data != effects.CursorImage) { entry.Bitmap = Decode(effects.CursorImage); entry.Data = effects.CursorImage; }
            return entry.Bitmap;
        }
    }
    public static BitmapSource Decode(string data)
    {
        if (data.Length > 3_000_000) throw new InvalidOperationException("La imagen del cursor es demasiado grande.");
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        var decoder = new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        if (image.PixelWidth > 512 || image.PixelHeight > 512) throw new InvalidOperationException("La imagen del cursor debe medir como máximo 512 × 512 píxeles.");
        image.Freeze(); return image;
    }
    public static string Import(string path)
    {
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidOperationException("Elige una imagen de cursor de menos de 8 MB.");
        using var stream = File.OpenRead(path);
        var bitmap = BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        if (bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096) throw new InvalidOperationException("Elige una imagen de cursor de hasta 4096 × 4096 píxeles.");
        var scale = Math.Min(1,512d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight));
        BitmapSource image = scale < 1 ? new TransformedBitmap(bitmap,new ScaleTransform(scale,scale)) : bitmap;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = new MemoryStream(); encoder.Save(output);
        var data = Convert.ToBase64String(output.ToArray()); Decode(data); return data;
    }
}
