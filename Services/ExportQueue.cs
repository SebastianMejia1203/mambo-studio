using ScreenStudio.Models;
namespace ScreenStudio.Services;

// Cola de exportación por lotes: cada trabajo guarda una foto del proyecto y su
// destino. El procesado es secuencial; un trabajo fallido se registra y continúa.
public sealed record ExportJob(Guid Id, string Name, StudioProject Project, string Destination, DateTime Created);
public sealed class ExportQueue
{
    public List<ExportJob> Jobs { get; } = [];
    public ExportJob Enqueue(StudioProject project, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("Elige un destino para el trabajo.");
        var job = new ExportJob(Guid.NewGuid(),project.Name,project.Clone(),destination,DateTime.Now);
        Jobs.Add(job); return job;
    }
    public bool Remove(Guid id) => Jobs.RemoveAll(j => j.Id == id) > 0;
    public void Clear() => Jobs.Clear();
    public async Task<Dictionary<Guid,string?>> ProcessAsync(Func<StudioProject,string,IProgress<double>,CancellationToken,Task> export,IProgress<(int Done,int Total,double Value)>? progress,CancellationToken cancellation)
    {
        var errors = new Dictionary<Guid,string?>();
        for (var i = 0; i < Jobs.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var job = Jobs[i];
            try
            {
                job.Project.Validate();
                await export(job.Project,job.Destination,new CallbackProgress(v => progress?.Report((i,Jobs.Count,v))),cancellation);
                errors[job.Id] = null;
            }
            catch (OperationCanceledException) { errors[job.Id] = "Cancelado."; throw; }
            catch (Exception e) { errors[job.Id] = e.Message; }
            progress?.Report((i + 1,Jobs.Count,1));
        }
        return errors;
    }
    private sealed class CallbackProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
