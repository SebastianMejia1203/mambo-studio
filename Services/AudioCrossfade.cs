using ScreenStudio.Models;
namespace ScreenStudio.Services;

// Fundido cruzado entre dos clips de audio adyacentes (sin hueco): iguala
// fundidos de salida/entrada para una transición suave y sin clics.
public static class AudioCrossfade
{
    public const double DefaultSeconds = .15;
    public static (TimelineClip Left, TimelineClip Next) Apply(StudioProject project, Guid clipId, double seconds = DefaultSeconds)
    {
        if (seconds <= 0 || seconds > 5) throw new InvalidOperationException("La duración del fundido debe estar entre 0 y 5 segundos.");
        var clip = project.Clips.FirstOrDefault(c => c.Id == clipId && c.Kind == ClipKind.Audio)
            ?? throw new InvalidOperationException("Selecciona un clip de audio.");
        var track = project.Tracks.FirstOrDefault(t => t.Clips.Contains(clip))
            ?? throw new InvalidOperationException("Selecciona un clip de audio.");
        var next = track.Clips.Where(c => c.Id != clip.Id && c.Start >= clip.End - .01).OrderBy(c => c.Start).FirstOrDefault()
            ?? throw new InvalidOperationException("Coloca otro clip de audio justo después para fundirlos.");
        if (next.Start - clip.End > .01) throw new InvalidOperationException("Coloca otro clip de audio justo después (sin hueco) para fundirlos.");
        clip.FadeOut = Math.Max(clip.FadeOut,seconds); next.FadeIn = Math.Max(next.FadeIn,seconds);
        return (clip,next);
    }
}
