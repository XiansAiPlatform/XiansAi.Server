# Service Layer Design

This document outlines the design principles and best practices for service classes within the XiansAi Server application. The service layer acts as an intermediary between the API endpoints (both Web API and Agent API) and the underlying data repositories or other external dependencies.

## Purpose

Service classes encapsulate specific business logic, coordinate operations involving multiple repositories or external calls, and provide a clean interface for the API endpoints to interact with. They help maintain separation of concerns, making the application more modular, testable, and maintainable.

## Design Principles

### Dependency Injection

Services rely heavily on Dependency Injection (DI) to receive their dependencies. Dependencies, such as repositories (e.g., `IFlowDefinitionRepository`, `ITenantRepository`), other services, or configuration objects (`HttpClient`), are injected via the constructor.

#### Constructor Injection Example

Here's an example from `TenantService` showing how dependencies (`ITenantRepository`, `ILogger`, `ITenantContext`) are injected via the constructor:

```csharp
public class TenantService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<TenantService> _logger;
    private readonly ITenantContext _tenantContext;

    // Dependencies are injected here
    public TenantService(
        ITenantRepository tenantRepository,
        ILogger<TenantService> logger,
        ITenantContext tenantContext)
    {
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    // ... other methods ...
}
```

#### Service Registration Examples

Service registration occurs within the configuration setup for each API feature using methods like `AddScoped`. This tells the DI container how to create instances of the services when they are requested.

**Agent API Service Registration (`AgentApiConfiguration.cs`)**

```csharp
public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
{
    // ... repository registrations ...

    // Register Lib API specific services
    builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
    builder.Services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
    builder.Services.AddScoped<IDefinitionsService, DefinitionsService>();
    builder.Services.AddScoped<IWorkflowSignalService, WorkflowSignalService>();
    builder.Services.AddScoped<IObjectCacheWrapperService, ObjectCacheWrapperService>();

    return builder;
}
```

**Web API Service Registration (`WebApiConfiguration.cs`)**

```csharp
public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
{
    // ... other registrations ...

    // Register Web API specific services
    builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>(); // Singleton example
    builder.Services.AddScoped<WorkflowStarterService>();
    builder.Services.AddScoped<WorkflowEventsService>();
    builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
    builder.Services.AddScoped<WorkflowCancelService>();
    builder.Services.AddScoped<CertificateService>();
    builder.Services.AddScoped<InstructionsService>();
    builder.Services.AddScoped<DefinitionsService>();
    builder.Services.AddScoped<TenantService>();
    builder.Services.AddScoped<ActivitiesService>();
    builder.Services.AddScoped<IMessagingService, MessagingService>();

    // ... repository registrations ...

    return builder;
}
```

Using DI promotes loose coupling and facilitates unit testing by allowing dependencies to be mocked.

### Interface-Based Programming (Where Applicable)

While not universally enforced for every service, defining interfaces (e.g., `IKnowledgeService`, `IWorkflowFinderService`, `IMessagingService`) for services is a recommended practice. This further enhances decoupling and testability, allowing different implementations to be swapped if needed. Services are often registered against their interfaces in the DI container.

### Single Responsibility Principle (SRP)

Each service class should have a single, well-defined responsibility related to a specific business domain or feature set. For example, `TenantService` handles tenant-related operations, while `WorkflowStarterService` manages the initiation of workflows. Avoid creating large, monolithic services that handle too many unrelated tasks.

### Statelessness

Services should strive to be stateless. They operate on the input provided to their methods and the dependencies injected via the constructor, without retaining state between method calls related to specific requests. This improves scalability and predictability.

### Error Handling

Services are responsible for handling potential errors that may arise during their operation, such as database exceptions or failures in external service calls. Errors should be handled gracefully, potentially logged, and communicated appropriately back to the calling layer (API endpoint), often through exceptions or standardized response objects.

## Examples

Concrete examples of service implementations can be found in the following directories:

- **Web API Services**: `/XiansAi.Server.Src/Features/WebApi/Services`
- **Agent API Services**: `/XiansAi.Server.Src/Features/AgentApi/Services`

Reviewing these existing services provides practical insight into applying the principles outlined here.

## Best Practices Summary

- **Use Constructor Injection**: Always inject dependencies through the constructor.
- **Register Services**: Ensure services are correctly registered in the relevant `*ApiConfiguration.cs` file.
- **Prefer Interfaces**: Define and use interfaces for services to promote loose coupling.
- **Keep Services Focused**: Adhere to the Single Responsibility Principle.
- **Implement Robust Error Handling**: Manage exceptions and edge cases appropriately.
- **Maintain Statelessness**: Avoid storing request-specific state within service instances.
