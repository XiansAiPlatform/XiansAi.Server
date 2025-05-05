using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Shared.Repositories;
using XiansAi.Server.Shared.Services;

namespace XiansAi.Server.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        
        // Register services
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        
        return services;
    }
} 