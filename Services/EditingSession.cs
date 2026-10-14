using ScreenStudio.Models;

namespace ScreenStudio.Services;

public sealed class EditingSession
{
    public StudioProject Project { get; private set; }
    private readonly Stack<StudioProject> undo = new();
    private readonly Stack<StudioProject> redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public EditingSession(StudioProject project) => Project = project;
    public void Change(Action<StudioProject> edit)
    {
        var before = Project.Clone();
        try { edit(Project); Project.Validate(false); }
        catch { Project = before; throw; }
        undo.Push(before); redo.Clear();
    }
    public void Undo() { if (undo.Count == 0) return; redo.Push(Project.Clone()); Project = undo.Pop(); }
    public void Redo() { if (redo.Count == 0) return; undo.Push(Project.Clone()); Project = redo.Pop(); }
    public StudioTrack? Track(Guid id) => Project.Tracks.FirstOrDefault(t => t.Id == id);
    public TimelineClip? Clip(Guid id) => Project.Clips.FirstOrDefault(c => c.Id == id);
    public StudioTrack? Owner(Guid id) => Project.Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == id));
    public bool CanPlace(Guid trackId, TimelineClip clip, double start, Guid? ignoreId = null)
    {
        var track = Track(trackId);
        return track != null && track.Kind == clip.Category && double.IsFinite(start) && start >= 0 && !track.Clips.Any(c => c.Id != ignoreId && start < c.End - .0001 && start + clip.Duration > c.Start + .0001);
    }
    public static double NextFree(StudioTrack track, double start, double duration)
    {
        foreach (var clip in track.Clips.OrderBy(c => c.Start))
            if (start < clip.End && start + duration > clip.Start) start = clip.End;
        return start;
    }
    public void Move(Guid clipId, Guid trackId, double start)
    {
        var clip = Clip(clipId) ?? throw new InvalidOperationException("Selecciona un clip.");
        if (!CanPlace(trackId, clip, start, clipId)) throw new InvalidOperationException("El destino no es compatible o ya está ocupado.");
        Change(_ => { Owner(clipId)!.Clips.Remove(clip); clip.Start = start; Track(trackId)!.Clips.Add(clip); });
    }
    public void Split(Guid id, double time)
    {
        var clip = Clip(id) ?? throw new InvalidOperationException("Selecciona un clip.");
        if (time - clip.Start < .04 || clip.End - time < .04) throw new InvalidOperationException("Sitúa el cabezal dentro del clip para dividirlo.");
        Change(_ => { var right = clip.Clone(true); right.SourceOffset += (time - clip.Start) / clip.Speed; right.Duration = clip.End - time; right.Start = time; right.Effects.TransitionIn=new(); clip.Effects.TransitionOut=new(); clip.Duration = time - clip.Start; Owner(id)!.Clips.Add(right); });
    }
    public void Trim(Guid id, double start, double duration, double offset)
    {
        var clip = Clip(id) ?? throw new InvalidOperationException("Selecciona un clip.");
        Change(_ => { clip.Start = start; clip.Duration = duration; clip.SourceOffset = offset; });
    }
    public void AddTrack(TrackKind kind) => Change(p =>
    {
        var n = p.Tracks.Count(t => t.Kind == kind) + 1;
        var track = new StudioTrack { Kind = kind, Name = (kind == TrackKind.Audio ? "Audio " : "Visual ") + n };
        if (kind == TrackKind.Visual) p.Tracks.Insert(0, track); else p.Tracks.Add(track);
    });
    public void RemoveTrack(Guid id)
    {
        var track = Track(id) ?? throw new InvalidOperationException("Selecciona una pista.");
        if (track.Clips.Count != 0) throw new InvalidOperationException("Sólo se pueden eliminar pistas vacías.");
        Change(p => p.Tracks.Remove(track));
    }
}
