using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ScreenStudio.Services;
using ScreenStudio.UI;
namespace ScreenStudio;
public partial class MainWindow
{
    private readonly ExportQueue exportQueue = new();
    private Dictionary<Guid,string?> queueErrors = [];
    private (int Done, int Total, double Value) queueProgress;
    private CancellationTokenSource? queueRun;
    private ProgressBar? queueBar;
    private TextBlock? queueLabel;
    private void BuildQueue(StackPanel body)
    {
        if (exportQueue.Jobs.Count == 0) body.Children.Add(StudioUi.Label("Cola vacía. Añade el proyecto actual y expórtalo junto a otros sin esperas.","Muted",11));
        foreach (var job in exportQueue.Jobs)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            var name = StudioUi.Label(job.Name); name.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(name);
            var dest = StudioUi.Label(job.Destination,"Muted",10); dest.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(dest);
            var running = queueRun != null;
            var status = queueErrors.TryGetValue(job.Id,out var error)
                ? error == null ? "Listo" : "Error: " + error
                : running ? "En cola…" : "Pendiente";
            var state = StudioUi.Label(status,error == null ? "Muted" : "Danger",10); info.Children.Add(state);
            Grid.SetColumn(info,0); row.Children.Add(info);
            var captured = job;
            var remove = StudioUi.Button("delete","Quitar de la cola",() => { exportQueue.Remove(captured.Id); queueErrors.Remove(captured.Id); RefreshSidebars(); });
            remove.IsEnabled = !running; Grid.SetColumn(remove,1); row.Children.Add(remove);
            body.Children.Add(row);
        }
        var actions = StudioUi.ActionRow(body);
        actions.Children.Add(StudioUi.ActionButton("plus","Añadir proyecto actual","Encola una copia del proyecto actual",() =>
        {
            var format = Editing.Project.Output.Format;
            var dialog = new SaveFileDialog { Filter = $"Vídeo {format.ToUpperInvariant()}|*.{format}", FileName = Editing.Project.Name + "." + format };
            if (dialog.ShowDialog(this) != true) return;
            try { exportQueue.Enqueue(Editing.Project,dialog.FileName); queueErrors.Clear(); }
            catch (Exception e) { ShowNotice(e.Message); }
            RefreshSidebars();
        }));
        var runAll = StudioUi.ActionButton("export","Exportar todo","Procesa la cola en orden",async () => await ProcessQueueAsync());
        runAll.IsEnabled = exportQueue.Jobs.Count > 0 && queueRun == null && !busy; actions.Children.Add(runAll);
        if (queueRun != null) actions.Children.Add(StudioUi.ActionButton("close","Cancelar cola","Detiene la cola al terminar el trabajo actual",() => queueRun?.Cancel()));
        queueLabel = StudioUi.Label(queueRun != null ? $"Exportando {queueProgress.Done + 1} de {queueProgress.Total}…" : "","Muted",11);
        queueLabel.Margin = new Thickness(0,8,0,4); body.Children.Add(queueLabel);
        queueBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 }; body.Children.Add(queueBar);
        if (queueRun != null) queueBar.Value = queueProgress.Value * 100;
    }
    private async Task ProcessQueueAsync()
    {
        if (queueRun != null || busy || exportQueue.Jobs.Count == 0 || compositor == null) return;
        Pause(); busy = true; UpdateCommands(); RefreshSidebars();
        queueRun = new CancellationTokenSource();
        var progress = new Progress<(int Done,int Total,double Value)>(state =>
        {
            queueProgress = state;
            if (queueBar != null) queueBar.Value = state.Value * 100;
            if (queueLabel != null) queueLabel.Text = $"Exportando {Math.Min(state.Done + 1,state.Total)} de {state.Total}… {state.Value:P0}";
        });
        try { queueErrors = await exportQueue.ProcessAsync((project,destination,report,token) => compositor.ExportAsync(project,destination,report,token),progress,queueRun.Token); }
        catch (OperationCanceledException) { ShowNotice("Cola cancelada."); }
        catch (Exception e) { ShowNotice(e.Message); }
        finally { queueRun.Dispose(); queueRun = null; busy = false; RefreshSidebars(); UpdateCommands(); }
    }
}
