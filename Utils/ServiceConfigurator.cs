using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Flowmaxer.Common;

namespace Flowmaxer.Utils
{
    public class ServiceConfigurator
    {
        public static void ConfigureTemporalServices(IServiceCollection services, IConfiguration configuration, AssemblyLoader assemblyLoader)
        {
            
            // Add services to the container.
            services.AddOpenApi();

            // Configure Temporal services
            services.Configure<TemporalConfig>(configuration.GetSection("Temporal"));
            services.AddSingleton<TemporalConfig>();
            services.AddSingleton<TemporalClientService>();

            ConfigureTemporalWorkers(services, assemblyLoader);

        }

        private static void ConfigureTemporalWorkers(IServiceCollection services, AssemblyLoader assemblyLoader)
        {
            // Add Temporal services
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