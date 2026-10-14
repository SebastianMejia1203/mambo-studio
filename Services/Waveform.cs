using System.IO;
namespace ScreenStudio.Services;

// Picos de audio para dibujar formas de onda en el timeline.
// Decodifica a mono 8 kHz y devuelve el máximo absoluto por cubeta.
public static class Waveform
{
    public static async Task<float[]> PeaksAsync(string ffmpeg,string path,int buckets,CancellationToken cancellation = default)
    {
        if (buckets < 1) return [];
        using var process = VideoEngine.CreateProcess(ffmpeg,["-v","error","-i",path,"-vn","-ar","8000","-ac","1","-f","f32le","pipe:1"]);
        process.Start();
        using var data = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(data,cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        try { await process.WaitForExitAsync(cancellation); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        await copy;
        if (process.ExitCode != 0) throw new InvalidOperationException("No se pudo leer el audio. " + await errors);
        var bytes = data.ToArray(); var samples = bytes.Length / 4;
        var peaks = new float[buckets];
        if (samples == 0) return peaks;
        for (var i = 0; i < samples; i++)
        {
            var bucket = (int)((long)i * buckets / samples);
            var value = Math.Abs(BitConverter.ToSingle(bytes,i * 4));
            if (float.IsFinite(value) && value > peaks[bucket]) peaks[bucket] = value;
        }
        return peaks;
    }
    public static float[] Slice(float[] peaks,double offset,double duration,double sourceDuration,int columns)
    {
        var slice = new float[Math.Max(1,columns)];
        if (peaks.Length == 0 || sourceDuration <= 0 || columns < 1) return slice;
        for (var x = 0; x < columns; x++)
        {
            var t0 = offset + duration * x / columns; var t1 = offset + duration * (x + 1) / columns;
            var b0 = (int)(t0 / sourceDuration * peaks.Length); var b1 = Math.Max(b0 + 1,(int)(t1 / sourceDuration * peaks.Length));
            var peak = 0f;
            for (var b = Math.Max(0,b0); b < Math.Min(peaks.Length,b1); b++) peak = Math.Max(peak,peaks[b]);
            slice[x] = Math.Min(1,peak);
        }
        return slice;
    }
}
