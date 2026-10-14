using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenStudio.Models;
using ScreenStudio.UI;
namespace ScreenStudio;
public partial class MainWindow
{
    private void BuildTransitions(StackPanel body)
    {
        if(SelectedClip is not {} clip || clip.Kind==ClipKind.Audio) { body.Children.Add(StudioUi.Label("Selecciona un clip visual","Muted",11)); return; }
        foreach(var incoming in new[]{true,false})
        {
            var transition=incoming?clip.Effects.TransitionIn:clip.Effects.TransitionOut;
            var title=StudioUi.Label(incoming?"Entrada / cambio de clip":"Salida"); title.FontWeight=FontWeights.SemiBold; title.Margin=new Thickness(0,8,0,12); body.Children.Add(title);
            var mode=new ComboBox { ItemsSource=new[]{"Corte · sin efecto","Fundido cruzado","Fundido a negro","Barrido desde la izquierda","Barrido desde la derecha"},SelectedIndex=(int)transition.Kind };
            if(!incoming) mode.ItemsSource=new[]{"Corte · sin efecto","Fundido de salida","Fundido a negro","Barrido hacia la izquierda","Barrido hacia la derecha"};
            StudioUi.Field(body,incoming?"Transición de entrada":"Transición de salida",mode);
            var duration=Input(body,incoming?"Duración de entrada · segundos":"Duración de salida · segundos",transition.Duration);
            var enabled=Check(body,incoming?"Entrada activa":"Salida activa",transition.Enabled);
            void Update(Action<ClipTransition> change)=>UpdateLiveEffects(clip.Id,e=>change(incoming?e.TransitionIn:e.TransitionOut));
            mode.SelectionChanged+=(_,_)=>Update(t=>t.Kind=(TransitionKind)mode.SelectedIndex);
            enabled.Checked+=(_,_)=>Update(t=>t.Enabled=true); enabled.Unchecked+=(_,_)=>Update(t=>t.Enabled=false);
            var modified=false; duration.TextChanged+=(_,_)=>modified=true;
            void Commit() { try { var value=StudioDialogs.Number(duration); Update(t=>t.Duration=value); } catch(Exception error) { ShowNotice(error.Message); } }
            duration.LostKeyboardFocus+=(_,_)=> { if(modified) { modified=false; Commit(); } };
            duration.PreviewKeyDown+=(_,args)=> { if(args.Key==Key.Enter) { modified=false; Commit(); args.Handled=true; } };
        }
    }
}
