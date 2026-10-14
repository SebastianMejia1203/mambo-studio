using System.Text.Json;
using ScreenStudio.Models;
using ScreenStudio.Controls;
using ScreenStudio.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace ScreenStudio;
public partial class MainWindow
{
    private void UpdateLiveEffects(Guid clipId,Action<ClipEffects> change,bool refreshPanels=false)
    {
        if(busy || Editing.Clip(clipId) is not { } clip) return;
        try
        {
            var before=JsonSerializer.Serialize(clip.Effects);
            var next=JsonSerializer.Deserialize<ClipEffects>(before)!; change(next); next.Validate();
            if(before==JsonSerializer.Serialize(next)) return;
            Editing.Change(_=>clip.Effects=next);
            dirty=true; preview?.RefreshEffects(Editing.Project,clipId,position,playing);
            Timeline.Refresh(); UpdateTitle(); UpdateCommands();
            if(refreshPanels) RefreshSidebars();
        }
        catch(Exception error) { ShowNotice(error.Message); RefreshSidebars(); }
    }
    private void LiveNumber(Guid clipId,TextBox box,Action<ClipEffects,double> set)
    {
        var modified=false; box.TextChanged+=(_,_)=>modified=true;
        void Commit() { try { var value=StudioDialogs.Number(box); UpdateLiveEffects(clipId,v=>set(v,value)); } catch(Exception error) { ShowNotice(error.Message); } }
        box.LostKeyboardFocus+=(_,_)=>{ if(modified){modified=false;Commit();} };
        box.PreviewKeyDown+=(_,args)=>{ if(args.Key==Key.Enter){modified=false;Commit();args.Handled=true;} };
    }
    private void LiveText(Guid clipId,TextBox box,Action<ClipEffects,string> set)
    {
        var modified=false; box.TextChanged+=(_,_)=>modified=true;
        void Commit() { try { UpdateLiveEffects(clipId,v=>set(v,box.Text)); } catch(Exception error) { ShowNotice(error.Message); } }
        box.LostKeyboardFocus+=(_,_)=>{ if(modified){modified=false;Commit();} };
        box.PreviewKeyDown+=(_,args)=>{ if(args.Key==Key.Enter){modified=false;Commit();args.Handled=true;} };
    }
    private static void SetFollow(ClipEffects effects,FollowMode mode)
    {
        if(effects.Follow!=FollowMode.Off) effects.LastFollow=effects.Follow;
        effects.Follow=mode;
        if(mode!=FollowMode.Off) effects.LastFollow=mode;
    }
    private void EditTimelineEffect(TimelineEffectEdit edit)
    {
        if(Editing.Clip(edit.Selection.ClipId) is not {} clip) return;
        var trace=Editing.Project.Library.FirstOrDefault(a=>a.Path==clip.SourcePath)?.Cursor;
        UpdateLiveEffects(clip.Id,e=>
        {
            var target=clip.Clone(); target.Effects=e;
            if(edit.Selection.Kind.StartsWith("transition"))
            {
                var transition=edit.Selection.Kind=="transitionin"?e.TransitionIn:e.TransitionOut;
                if(edit.Operation=="delete") transition.Kind=TransitionKind.Cut;
                else if(edit.Operation=="toggle") transition.Enabled=!transition.Enabled;
                return;
            }
            if(edit.Selection.Kind=="cursor") { e.ShowCursor=edit.Operation=="delete"?false:!e.ShowCursor; return; }
            if(edit.Selection.Kind=="click")
            {
                var list=EffectCues.EditClicks(target,trace); var cue=list.FirstOrDefault(c=>c.Id==edit.Selection.CueId); if(cue==null) return;
                if(edit.Operation=="delete") list.Remove(cue);
                else if(edit.Operation=="toggle") cue.Enabled=!cue.Enabled;
                else cue.Time=Math.Clamp(edit.Start,clip.SourceOffset,clip.SourceOffset+clip.Duration-.001);
            }
            else
            {
                var list=EffectCues.EditZooms(target,trace); var cue=list.FirstOrDefault(c=>c.Id==edit.Selection.CueId);
                if(edit.Operation=="add")
                {
                    var remaining=clip.SourceOffset+clip.Duration-edit.Start; if(remaining<.04) return;
                    list.Add(new ZoomCue { Id=edit.Selection.CueId,Start=edit.Start,Duration=Math.Min(2,remaining) });
                    if(e.Follow==FollowMode.Off) SetFollow(e,trace==null?FollowMode.Manual:FollowMode.Clicks);
                }
                else if(cue!=null)
                {
                    if(edit.Operation=="delete") list.Remove(cue);
                    else if(edit.Operation=="toggle") cue.Enabled=!cue.Enabled;
                    else if(edit.Operation=="amount") cue.Amount=edit.Amount;
                    else
                    {
                        if(edit.Start<clip.SourceOffset || edit.Duration<.04 || edit.Start+edit.Duration>clip.SourceOffset+clip.Duration+.0001) throw new InvalidOperationException("El zoom debe quedar dentro del clip.");
                        cue.Start=edit.Start; cue.Duration=edit.Duration;
                    }
                }
            }
        });
        Timeline.SelectedClipId=clip.Id; Timeline.SelectedTrackId=Editing.Owner(clip.Id)?.Id;
        Timeline.SelectedEffect=edit.Operation=="delete"?null:edit.Selection;
        if(edit.Operation=="add") AddPanel(false,"Movimiento"); else RefreshSidebars();
        Timeline.Refresh();
    }
    private void BuildSelectedEffect(StackPanel body,TimelineClip clip)
    {
        if(Timeline.SelectedEffect is not {} selection || selection.ClipId!=clip.Id || selection.Kind=="cursor") return;
        var trace=Editing.Project.Library.FirstOrDefault(a=>a.Path==clip.SourcePath)?.Cursor;
        var click=selection.Kind=="click"?EffectCues.Clicks(clip,trace).FirstOrDefault(c=>c.Id==selection.CueId):null;
        var zoom=selection.Kind=="zoom"?EffectCues.Zooms(clip,trace).FirstOrDefault(c=>c.Id==selection.CueId):null;
        if(click==null && zoom==null) return;
        var title=StudioUi.Label(click!=null?"Clic seleccionado":"Zoom seleccionado"); title.FontWeight=FontWeights.SemiBold; title.Margin=new Thickness(0,0,0,8); body.Children.Add(title);
        var enabled=Check(body,"Evento activo",click?.Enabled ?? zoom!.Enabled);
        enabled.Click+=(_,_)=>EditTimelineEffect(new(selection,"toggle"));
        var visibleStart=Math.Max(clip.SourceOffset,click?.Time ?? zoom!.Start);
        var start=Input(body,"Posición en el proyecto · segundos",Math.Round(clip.Start+visibleStart-clip.SourceOffset,3));
        TextBox? duration=null,amount=null;
        if(zoom!=null) { duration=Input(body,"Duración del zoom · segundos",Math.Round(Math.Min(clip.SourceOffset+clip.Duration,zoom.End)-visibleStart,3)); amount=Input(body,"Aumento del intervalo · 1–4×",zoom.Amount==0?clip.Effects.Zoom:zoom.Amount); }
        void Commit()
        {
            try { EditTimelineEffect(new(selection,"move",StudioDialogs.Number(start)-clip.Start+clip.SourceOffset,duration==null?0:StudioDialogs.Number(duration))); }
            catch(Exception error) { ShowNotice(error.Message); }
        }
        foreach(var input in new[]{start,duration}.OfType<TextBox>())
        {
            var modified=false; input.TextChanged+=(_,_)=>modified=true;
            input.LostKeyboardFocus+=(_,_)=> { if(modified) { modified=false; Commit(); } };
            input.PreviewKeyDown+=(_,args)=> { if(args.Key==Key.Enter) { modified=false; Commit(); args.Handled=true; } };
        }
        if(amount!=null)
        {
            void Amount() { try { EditTimelineEffect(new(selection,"amount",Amount:StudioDialogs.Number(amount))); } catch(Exception error) { ShowNotice(error.Message); } }
            var modified=false; amount.TextChanged+=(_,_)=>modified=true;
            amount.LostKeyboardFocus+=(_,_)=> { if(modified) { modified=false; Amount(); } };
            amount.PreviewKeyDown+=(_,args)=> { if(args.Key==Key.Enter) { modified=false; Amount(); args.Handled=true; } };
        }
        StudioUi.ActionRow(body).Children.Add(StudioUi.Button("delete","Eliminar efecto seleccionado",()=>EditTimelineEffect(new(selection,"delete"))));
        body.Children.Add(new Separator { Margin=new Thickness(0,12,0,16) });
    }
}
