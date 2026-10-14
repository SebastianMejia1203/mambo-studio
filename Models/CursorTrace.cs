using System.IO;
using System.Text.Json;
namespace ScreenStudio.Models;

public enum FollowMode { Off, Clicks, Always, Manual }
public sealed class CursorSample
{
    public double Time { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool Visible { get; set; } = true;
    public int Click { get; set; }
}
public sealed class CursorTrace
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double Duration { get; set; }
    public bool ShowCursor { get; set; } = true;
    public List<CursorSample> Samples { get; set; } = [];
    public void Validate()
    {
        if (Width < 1 || Height < 1 || !double.IsFinite(Duration) || Duration < 0 || Duration > 86400 || Samples.Count > 6_000_000) throw new InvalidOperationException("Datos de cursor inválidos.");
        double last = -1;
        foreach (var s in Samples)
        {
            if (!double.IsFinite(s.Time) || !double.IsFinite(s.X) || !double.IsFinite(s.Y) || s.Time < last || s.Time > Duration + 1 || s.X < 0 || s.X > 1 || s.Y < 0 || s.Y > 1 || s.Click is < 0 or > 2) throw new InvalidOperationException("Muestra de cursor inválida.");
            last = s.Time;
        }
    }
    public async Task SaveAsync(string video)
    {
        Validate(); var path = video + ".cursor.json"; var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(this)); File.Move(temporary,path,true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<CursorTrace?> LoadAsync(string video)
    {
        var path = video + ".cursor.json"; if (!File.Exists(path)) return null;
        using var input = File.OpenRead(path); var trace = await JsonSerializer.DeserializeAsync<CursorTrace>(input); trace?.Validate(); return trace;
    }
}
public sealed class ClipEffects
{
    public FollowMode Follow { get; set; }
    public FollowMode LastFollow { get; set; } = FollowMode.Clicks;
    public List<ClickCue>? ClickCues { get; set; }
    public List<ZoomCue>? ZoomCues { get; set; }
    public double Zoom { get; set; } = 1.8;
    public double FollowSeconds { get; set; } = .3;
    public double TransitionSeconds { get; set; } = .4;
    public double HoldSeconds { get; set; } = 1.5;
    public double FocusX { get; set; } = .5;
    public double FocusY { get; set; } = .5;
    public bool ShowCursor { get; set; } = true;
    public bool ShowClicks { get; set; } = true;
    public bool HideIdleCursor { get; set; }
    public double CursorSize { get; set; } = 32;
    public double CursorSmoothSeconds { get; set; } = .07;
    public string? CursorImage { get; set; }
    public double CursorHotspotX { get; set; }
    public double CursorHotspotY { get; set; }
    public double CornerRadius { get; set; }
    public double BorderWidth { get; set; }
    public string BorderColor { get; set; } = "#FFFFFF";
    public double Shadow { get; set; }
    public double FadeSeconds { get; set; }
    public double BlurX { get; set; }
    public double BlurY { get; set; }
    public double BlurW { get; set; }
    public double BlurH { get; set; }
    public double BlurAmount { get; set; }
    public string ChromaColor { get; set; } = "#00FF00";
    public double ChromaSimilarity { get; set; }
    public double ChromaBlend { get; set; }
    public bool ChromaEnabled => ChromaSimilarity > 0;
    public bool BlurEnabled => BlurAmount > 0 && BlurW > 0 && BlurH > 0;
    public ClipTransition TransitionIn { get; set; }=new();
    public ClipTransition TransitionOut { get; set; }=new();
    public bool HasVisualEffects => Follow != FollowMode.Off || CornerRadius > 0 || BorderWidth > 0 || Shadow > 0 || FadeSeconds > 0 || TransitionIn.Active || TransitionOut.Active || BlurEnabled || ChromaEnabled;
    public void Validate()
    {
        TransitionIn.Validate(); TransitionOut.Validate();
        bool Range(double v,double min,double max) => double.IsFinite(v) && v >= min && v <= max;
        if (!Enum.IsDefined(LastFollow) || ClickCues?.Count > 100000 || ZoomCues?.Count > 100000) throw new InvalidOperationException("Eventos de efectos inválidos.");
        if (ClickCues != null && (ClickCues.Any(c=>string.IsNullOrEmpty(c.Id) || !Range(c.Time,0,86400)) || ClickCues.Select(c=>c.Id).Distinct().Count()!=ClickCues.Count)) throw new InvalidOperationException("Marcadores de clic inválidos.");
        if (ZoomCues != null && (ZoomCues.Any(c=>string.IsNullOrEmpty(c.Id) || !Range(c.Start,0,86400) || !Range(c.Duration,.04,86400) || c.End>86400 || !(c.Amount==0 || Range(c.Amount,1,4))) || ZoomCues.Select(c=>c.Id).Distinct().Count()!=ZoomCues.Count)) throw new InvalidOperationException("Intervalos de zoom inválidos.");
        if (!Range(CursorHotspotX,0,1) || !Range(CursorHotspotY,0,1)) throw new InvalidOperationException("El punto activo del cursor debe estar dentro de la imagen.");
        if (CursorImage != null) UI.CursorArtwork.Get(this);
        if (!Range(BlurX,0,1) || !Range(BlurY,0,1) || !Range(BlurW,0,1) || !Range(BlurH,0,1) || !Range(BlurAmount,0,50)) throw new InvalidOperationException("La zona de desenfoque debe estar dentro del clip (0–100 %) con intensidad 0–50.");
        if (!StudioProject.ValidColor(ChromaColor) || !Range(ChromaSimilarity,0,1) || !Range(ChromaBlend,0,1)) throw new InvalidOperationException("Ajustes de croma inválidos.");
        if (!Enum.IsDefined(Follow) || !Range(Zoom,1,4) || !Range(FollowSeconds,.02,3) || !Range(TransitionSeconds,.05,3) || !Range(HoldSeconds,.1,15) || !Range(FocusX,0,1) || !Range(FocusY,0,1) || !Range(CursorSize,8,100) || !Range(CursorSmoothSeconds,0,1) || !Range(CornerRadius,0,200) || !Range(BorderWidth,0,20) || !Range(Shadow,0,1) || !Range(FadeSeconds,0,3) || !StudioProject.ValidColor(BorderColor)) throw new InvalidOperationException("Ajustes de cursor, zoom o apariencia inválidos.");
    }
}
