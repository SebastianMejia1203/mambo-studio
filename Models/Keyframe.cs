namespace ScreenStudio.Models;

// Keyframe de animación: instante (segundos desde el inicio del clip) y valor.
// Si una propiedad tiene keys, estas definen su valor e ignoran el valor base.
public sealed record Keyframe(double Time, double Value)
{
    public void Validate()
    {
        if (!double.IsFinite(Time) || !double.IsFinite(Value) || Time < 0 || Time > 86400) throw new InvalidOperationException("Keyframe inválido.");
    }
}

public static class KeyframeTrack
{
    public static double Evaluate(List<Keyframe>? keys, double time, double baseValue)
    {
        if (keys == null || keys.Count == 0) return baseValue;
        if (time <= keys[0].Time) return keys[0].Value;
        for (var i = 1; i < keys.Count; i++)
        {
            if (time <= keys[i].Time)
            {
                var a = keys[i-1]; var b = keys[i];
                var span = b.Time - a.Time;
                var t = span <= 0 ? 1 : (time - a.Time) / span;
                t = Math.Clamp(t,0,1); var smooth = t * t * (3 - 2 * t);
                return a.Value + (b.Value - a.Value) * smooth;
            }
        }
        return keys[^1].Value;
    }
    public static void Validate(List<Keyframe>? keys, string name)
    {
        if (keys == null) return;
        if (keys.Count > 10000) throw new InvalidOperationException($"Demasiados keyframes en {name}.");
        foreach (var key in keys) key.Validate();
        for (var i = 1; i < keys.Count; i++)
            if (keys[i].Time < keys[i-1].Time) throw new InvalidOperationException($"Los keyframes de {name} deben estar ordenados en el tiempo.");
    }
}
