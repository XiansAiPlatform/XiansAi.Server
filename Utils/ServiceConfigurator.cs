using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Flowmaxer.Common;

namespace Flowmaxer.Utils
{
    public class ServiceConfigurator
    {
        public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Add services to the container.
            services.AddOpenApi();

            // Configure Temporal services
            services.Configure<TemporalConfig>(configuration.GetSection("Temporal"));
            services.AddSingleton<TemporalConfig>();
            services.AddSingleton<TemporalClientService>();

            // Add Temporal services
            var assemblyLoader = new AssemblyLoader(configuration);
            var workflowTypes = assemblyLoader.GetWorkflowTypes();
            var activityTypes = assemblyLoader.GetActivityTypes();

            services.AddSingleton<IHostedService>(sp => new TemporalWorkerService
            {
                Config = sp.GetRequiredService<TemporalConfig>(),
                ClientService = sp.GetRequiredService<TemporalClientService>(),
                Workflows = workflowTypes.ToArray(),
                Activities = activityTypes.ToArray()
            });
        }


    }
}