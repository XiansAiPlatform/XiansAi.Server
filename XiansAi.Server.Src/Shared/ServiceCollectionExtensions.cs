using Shared.Repositories;
using Shared.Services;

namespace Shared;

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