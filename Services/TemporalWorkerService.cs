using Temporalio.Client;
using Temporalio.Worker;


public class TemporalWorkerService : IHostedService
{
    public required TemporalConfig Config {get; set;}
    public required TemporalClientService ClientService {get; set;}
    private List<TemporalWorker> _workers = new();

    public required Type[] Workflows { get; set; }
    public required Type[] Activities { get; set; }

    private void LoadAssemblies()
    {
        throw new NotImplementedException();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var client = await ClientService.GetClientAsync();
        
        foreach (var workflow in Workflows)
        {
            var worker = new TemporalWorker(
                client,
                new TemporalWorkerOptions()
                    .AddWorkflow(workflow)
                    .AddAllActivities(Activities));
            
            _workers.Add(worker);
            await worker.ExecuteAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var worker in _workers)
        {
            worker?.Dispose();
        }
        return Task.CompletedTask;
    }
}