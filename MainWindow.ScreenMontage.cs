using System.Windows;
using System.Windows.Controls;
using ScreenStudio.Models;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio;
public partial class MainWindow
{
    private void BuildScreenMontage(StackPanel body)
    {
        if(SelectedClip?.CaptureGroupId is not Guid group) return;
        var title=StudioUi.Label("Montaje de pantallas"); title.FontWeight=FontWeights.SemiBold; title.Margin=new Thickness(0,16,0,12); body.Children.Add(title);
        var layout=new ComboBox { ItemsSource=new[]{"Izquierda / derecha","Arriba / abajo","Pistas separadas · alternar"},SelectedIndex=(int)SelectedClip.CaptureLayout };
        StudioUi.Field(body,"Distribución",layout); var reverse=Check(body,"Invertir orden",SelectedClip.CaptureReversed);
        async Task Distribute() { try { await ChangeAsync(p=>ScreenLayoutEditor.Apply(p,group,(ScreenLayout)layout.SelectedIndex,reverse.IsChecked==true)); } catch(Exception error) { ShowNotice(error.Message); } }
        layout.SelectionChanged+=async(_,_)=>await Distribute();
        reverse.Checked+=async(_,_)=>await Distribute(); reverse.Unchecked+=async(_,_)=>await Distribute();
        var switches=StudioUi.ActionRow(body);
        for(int index=0;index<2;index++)
        {
            var screen=index; var name=Editing.Project.Clips.FirstOrDefault(c=>c.CaptureGroupId==group && c.ScreenIndex==screen)?.ScreenName ?? $"Pantalla {screen+1}";
            switches.Children.Add(StudioUi.ActionButton("video",name.Split('·')[0].Trim(),$"Mostrar {name} desde el cabezal",async()=>
            {
                try { await ChangeAsync(p=> { Timeline.SelectedClipId=ScreenLayoutEditor.SwitchAt(p,group,screen,position); if(Timeline.SelectedClipId is Guid id) Timeline.SelectedTrackId=p.Tracks.First(t=>t.Clips.Any(c=>c.Id==id)).Id; }); }
                catch(Exception error) { ShowNotice(error.Message); }
            }));
        }
    }
}
