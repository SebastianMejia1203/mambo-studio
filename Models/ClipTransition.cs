namespace ScreenStudio.Models;
public enum TransitionKind { Cut, Dissolve, FadeBlack, WipeLeft, WipeRight }
public sealed class ClipTransition
{
    public TransitionKind Kind { get; set; }
    public double Duration { get; set; }=.5;
    public bool Enabled { get; set; }=true;
    public bool Active=>Enabled && Kind!=TransitionKind.Cut;
    public void Validate()
    {
        if(!Enum.IsDefined(Kind) || !double.IsFinite(Duration) || Duration<.05 || Duration>5) throw new InvalidOperationException("La transición debe durar entre 0,05 y 5 segundos.");
    }
}
