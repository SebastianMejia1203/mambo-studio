using ScreenStudio.Controls;
namespace ScreenStudio;
public partial class MainWindow
{
    private void InitializePreviewEditing()
    {
        PreviewTransform.Stage=Stage; PreviewTransform.Renderer=preview; PreviewTransform.Session=Editing;
        PreviewTransform.SelectionRequested+=(_,id)=>
        {
            Timeline.SelectedClipId=id; Timeline.SelectedEffect=null;
            if(id is Guid clipId) Timeline.SelectedTrackId=Editing.Owner(clipId)?.Id;
            Timeline.Refresh(); RefreshSidebars(); UpdateCommands();
        };
        PreviewTransform.TransformStarted+=(_,_)=>Pause();
        PreviewTransform.TransformPreviewed+=(_,value)=>{ if (preview != null && Editing.Clip(value.ClipId) is {} clip) { preview.Held.Add(value.ClipId); preview.SetVisualTransform(Editing.Project,value.ClipId,value.Scale,value.X,value.Y); } };
        PreviewTransform.TransformCancelled+=(_,value)=>{ preview?.Held.Remove(value.ClipId); preview?.SetVisualTransform(Editing.Project,value.ClipId,value.Scale,value.X,value.Y); };
        PreviewTransform.TransformCommitted+=(_,value)=>{ preview?.Held.Remove(value.ClipId); CommitVisualTransform(value); };
    }
    private void CommitVisualTransform(VisualTransform value)
    {
        if(busy || Editing.Clip(value.ClipId) is not {} clip) return;
        if(Math.Abs(clip.Scale-value.Scale)<.000001 && Math.Abs(clip.X-value.X)<.000001 && Math.Abs(clip.Y-value.Y)<.000001) return;
        try
        {
            Editing.Change(_=> { clip.Scale=value.Scale; clip.X=value.X; clip.Y=value.Y; });
            preview?.SetVisualTransform(Editing.Project,clip.Id,clip.Scale,clip.X,clip.Y);
            dirty=true; UpdateTitle(); Timeline.Refresh(); RefreshSidebars(); UpdateCommands();
        }
        catch(Exception error) { ShowNotice(error.Message); _=RefreshAsync(); }
    }
    private void RefreshPreviewSelection()
    {
        if(PreviewTransform==null) return;
        PreviewTransform.Session=Editing; PreviewTransform.SelectedClipId=Timeline.SelectedClipId;
        PreviewTransform.IsEnabled=!busy && !recording; PreviewTransform.InvalidateVisual();
    }
}
