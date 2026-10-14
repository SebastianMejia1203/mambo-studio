using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenStudio.Models;

public enum TrackKind { Visual, Audio }
public enum ClipKind { Video, Image, Text, Audio }
public enum EditTool { Select, Trim, Split }

public sealed class OutputSettings
{
    public int FrameRate { get; set; } = 30;
    public string VideoEncoder { get; set; } = "libx264";
    public string AudioEncoder { get; set; } = "aac";
    public string Format { get; set; } = "mp4";
    public bool Subtitles { get; set; }
    public bool Stabilize { get; set; }
    public void Validate()
    {
        if (FrameRate is not (24 or 25 or 30 or 50 or 60)) throw new InvalidOperationException("Fotogramas permitidos: 24, 25, 30, 50 o 60.");
        if (VideoEncoder is not ("libx264" or "libx265" or "libvpx-vp9")) throw new InvalidOperationException("Codificador de vídeo no compatible.");
        if (AudioEncoder is not ("aac" or "libopus" or "pcm_s16le")) throw new InvalidOperationException("Codificador de audio no compatible.");
        if (Format is not ("mp4" or "mkv" or "webm" or "mov")) throw new InvalidOperationException("Formato no compatible.");
        if (Format == "webm" && (VideoEncoder != "libvpx-vp9" || AudioEncoder != "libopus")) throw new InvalidOperationException("WebM requiere VP9 y Opus.");
        if (Format is "mp4" or "mov" && (VideoEncoder == "libvpx-vp9" || AudioEncoder != "aac")) throw new InvalidOperationException("Para MP4/MOV selecciona H.264 o H.265 y AAC. MKV admite todas las combinaciones disponibles.");
    }
}

public sealed class MediaAsset
{
    public string Path { get; set; } = "";
    public ClipKind Kind { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool HasAudio { get; set; }
    public bool HasAlpha { get; set; }
    public CursorTrace? Cursor { get; set; }
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path);
}

public sealed class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Clip";
    public ClipKind Kind { get; set; }
    public string SourcePath { get; set; } = "";
    public double Start { get; set; }
    public double Duration { get; set; } = 5;
    public double SourceOffset { get; set; }
    public double SourceDuration { get; set; } = 5;
    public double Scale { get; set; } = 1;
    public double X { get; set; } = .5;
    public double Y { get; set; } = .5;
    public double Opacity { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public double FadeIn { get; set; }
    public double FadeOut { get; set; }
    public List<Keyframe> OpacityKeys { get; set; } = [];
    public List<Keyframe> VolumeKeys { get; set; } = [];
    public List<Keyframe> ScaleKeys { get; set; } = [];
    public List<Keyframe> XKeys { get; set; } = [];
    public List<Keyframe> YKeys { get; set; } = [];
    [JsonIgnore] public bool HasTransformKeys => ScaleKeys.Count > 0 || XKeys.Count > 0 || YKeys.Count > 0;
    public string Text { get; set; } = "Texto";
    public double FontSize { get; set; } = 72;
    public string TextColor { get; set; } = "#FFFFFF";
    public ClipEffects Effects { get; set; } = new();
    public Guid? CaptureGroupId { get; set; }
    public int ScreenIndex { get; set; }
    public string? ScreenName { get; set; }
    public ScreenLayout CaptureLayout { get; set; }
    public bool CaptureReversed { get; set; }
    [JsonIgnore] public double End => Start + Duration;
    [JsonIgnore] public TrackKind Category => Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Visual;
    public TimelineClip Clone(bool newId = false)
    {
        var copy = JsonSerializer.Deserialize<TimelineClip>(JsonSerializer.Serialize(this))!;
        if (newId) copy.Id = Guid.NewGuid();
        return copy;
    }
}

public sealed class StudioTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Visual 1";
    public TrackKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public List<TimelineClip> Clips { get; set; } = [];
}

public sealed class StudioProject
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Sin título";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Padding { get; set; } = 0;
    public string Background { get; set; } = "#15171C";
    public string BackgroundEnd { get; set; } = "#15171C";
    public bool GradientBackground { get; set; }
    public double ExportIn { get; set; }
    public double ExportOut { get; set; }
    public OutputSettings Output { get; set; } = new();
    public List<StudioTrack> Tracks { get; set; } = [new(), new() { Name = "Audio 1", Kind = TrackKind.Audio }];
    public List<MediaAsset> Library { get; set; } = [];
    // Legacy fields are retained to import version 1 documents without losing edits.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string SourcePath { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double StartSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double EndSeconds { get; set; }
    [JsonIgnore] public double Duration => Tracks.SelectMany(t => t.Clips).Select(c => c.End).DefaultIfEmpty(0).Max();
    [JsonIgnore] public IEnumerable<TimelineClip> Clips => Tracks.SelectMany(t => t.Clips);
    public static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    public StudioProject Clone() => JsonSerializer.Deserialize<StudioProject>(JsonSerializer.Serialize(this))!;

    // Devuelve una copia con solo el intervalo [start, end): recorta los bordes,
    // elimina lo exterior y desplaza el origen a cero. Las preferencias de salida se conservan.
    public StudioProject Slice(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end - start < .04) throw new InvalidOperationException("El rango de exportación es demasiado corto.");
        var copy = Clone();
        foreach (var track in copy.Tracks)
        {
            var kept = new List<TimelineClip>();
            foreach (var clip in track.Clips)
            {
                if (clip.End <= start || clip.Start >= end) continue;
                if (clip.Start < start)
                {
                    var cut = start - clip.Start;
                    clip.SourceOffset += cut / (clip.Speed <= 0 ? 1 : clip.Speed);
                    clip.Duration -= cut;
                    clip.OpacityKeys.RemoveAll(k => k.Time < cut - .0001);
                    clip.VolumeKeys.RemoveAll(k => k.Time < cut - .0001);
                    foreach (var keys in new[] { clip.OpacityKeys, clip.VolumeKeys })
                        for (var i = 0; i < keys.Count; i++) keys[i] = keys[i] with { Time = keys[i].Time - cut };
                    clip.Start = start;
                }
                if (clip.End > end) clip.Duration = end - clip.Start;
                if (clip.Duration < .04) continue;
                clip.Start -= start;
                kept.Add(clip);
            }
            track.Clips = kept;
        }
        copy.ExportIn = 0; copy.ExportOut = 0;
        copy.Validate(false);
        return copy;
    }

    public void Validate(bool requireFiles = true)
    {
        if (Width < 320 || Height < 240 || Width > 3840 || Height > 3840 || Width % 2 != 0 || Height % 2 != 0) throw new InvalidOperationException("El lienzo debe usar dimensiones pares entre 320 × 240 y 3840 × 3840.");
        if (!double.IsFinite(ExportIn) || !double.IsFinite(ExportOut) || ExportIn < 0 || ExportOut < 0 || (ExportOut > 0 && ExportOut <= ExportIn)) throw new InvalidOperationException("El rango de exportación debe empezar en cero o más y terminar después del inicio.");
        if (Padding < 0 || Padding * 2 >= Math.Min(Width, Height) || !ValidColor(Background)) throw new InvalidOperationException("Fondo o margen inválido.");
        Output.Validate();
        if (!ValidColor(BackgroundEnd)) throw new InvalidOperationException("Color de fondo inválido.");
        foreach (var asset in Library) asset.Cursor?.Validate();
        var ids = new HashSet<Guid>();
        foreach (var track in Tracks)
        {
            if (!ids.Add(track.Id)) throw new InvalidOperationException("Identificador de pista duplicado.");
            foreach (var clip in track.Clips)
            {
                clip.Effects.Validate();
                if(clip.CaptureGroupId!=null && (clip.Kind!=ClipKind.Video || clip.ScreenIndex is <0 or >1 || !Enum.IsDefined(clip.CaptureLayout))) throw new InvalidOperationException("Grupo de pantallas inválido.");
                if (!ids.Add(clip.Id) || clip.Category != track.Kind) throw new InvalidOperationException("El audio sólo puede estar en pistas de audio.");
                if (!double.IsFinite(clip.Start) || !double.IsFinite(clip.Duration) || !double.IsFinite(clip.SourceOffset) || !double.IsFinite(clip.SourceDuration) || clip.Start < 0 || clip.SourceOffset < 0 || clip.Duration < .04 || clip.End > 86400) throw new InvalidOperationException("Posición o duración de clip inválida.");
                if (clip.Kind is ClipKind.Video or ClipKind.Audio && clip.SourceOffset + clip.Duration / clip.Speed > clip.SourceDuration + .05) throw new InvalidOperationException("El recorte supera la duración del original.");
                if (requireFiles && clip.Kind != ClipKind.Text && !File.Exists(clip.SourcePath)) throw new InvalidOperationException("Falta el archivo: " + clip.SourcePath);
                if (!double.IsFinite(clip.Scale) || clip.Scale < .05 || clip.Scale > 2 || !double.IsFinite(clip.X) || !double.IsFinite(clip.Y) || clip.X < 0 || clip.X > 1 || clip.Y < 0 || clip.Y > 1 || !double.IsFinite(clip.Opacity) || clip.Opacity < 0 || clip.Opacity > 1 || !double.IsFinite(clip.Volume) || clip.Volume < 0 || clip.Volume > 1 || !double.IsFinite(clip.FontSize) || clip.FontSize < 8 || clip.FontSize > 500 || !ValidColor(clip.TextColor)) throw new InvalidOperationException("Propiedades del clip inválidas.");
                if (!double.IsFinite(clip.Speed) || clip.Speed < .25 || clip.Speed > 4) throw new InvalidOperationException("La velocidad debe estar entre el 25 % y el 400 %.");
                if (!double.IsFinite(clip.FadeIn) || !double.IsFinite(clip.FadeOut) || clip.FadeIn < 0 || clip.FadeOut < 0 || clip.FadeIn > 3600 || clip.FadeOut > 3600) throw new InvalidOperationException("Los fundidos de audio deben durar cero segundos o más.");
                KeyframeTrack.Validate(clip.OpacityKeys,"opacidad"); KeyframeTrack.Validate(clip.VolumeKeys,"volumen");
                KeyframeTrack.Validate(clip.ScaleKeys,"escala"); KeyframeTrack.Validate(clip.XKeys,"posición X"); KeyframeTrack.Validate(clip.YKeys,"posición Y");
                if (clip.OpacityKeys.Any(k => k.Value < 0 || k.Value > 2) || clip.VolumeKeys.Any(k => k.Value < 0 || k.Value > 4)) throw new InvalidOperationException("Los keyframes de opacidad (0–2) y volumen (0–4) están fuera de rango.");
                if (clip.ScaleKeys.Any(k => k.Value < .05 || k.Value > 2) || clip.XKeys.Concat(clip.YKeys).Any(k => k.Value < 0 || k.Value > 1)) throw new InvalidOperationException("Los keyframes de escala (5–200 %) y posición (0–100 %) están fuera de rango.");
            }
            var ordered = track.Clips.OrderBy(c => c.Start).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].Start < ordered[i - 1].End - .0001) throw new InvalidOperationException("Hay clips superpuestos en una misma pista. Usa otra pista para superponer elementos.");
        }
    }

    public void Validate(double duration)
    {
        if (!File.Exists(SourcePath) || StartSeconds < 0 || EndSeconds > duration + .1 || EndSeconds - StartSeconds < .1 || !ValidColor(Background)) throw new InvalidOperationException("Recorte original inválido.");
    }
    public static async Task<StudioProject> LoadAsync(string path)
    {
        using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<StudioProject>(stream) ?? throw new InvalidOperationException("Proyecto vacío.");
        if (project.Version == 1)
        {
            project.Version = 2;
            project.Name = Path.GetFileNameWithoutExtension(path);
            project.Tracks = [new StudioTrack(), new StudioTrack { Name = "Audio 1", Kind = TrackKind.Audio }];
            project.Tracks[0].Clips.Add(new TimelineClip { Name = Path.GetFileName(project.SourcePath), SourcePath = project.SourcePath, Kind = ClipKind.Video, SourceOffset = project.StartSeconds, Duration = project.EndSeconds - project.StartSeconds, SourceDuration = project.EndSeconds });
        }
        else if (project.Version != 2) throw new InvalidOperationException("Versión de proyecto no compatible.");
        return project;
    }
    public async Task SaveAsync(string path)
    {
        Validate();
        if (Clips.Any(c => c.Kind != ClipKind.Text && string.Equals(Path.GetFullPath(c.SourcePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("El proyecto no puede reemplazar un archivo original.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

