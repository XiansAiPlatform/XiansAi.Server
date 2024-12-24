using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<TenantContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

    
builder.Services.AddScoped<ITemporalClientService>(sp =>
{
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var tenantId = tenantContext.TenantId;

    // Fetch tenant-specific configuration
    var temporalConfig = builder.Configuration.GetSection($"Tenants:{tenantId}:Temporal").Get<TemporalConfig>();
    if (temporalConfig == null)
    {
        throw new Exception($"Temporal configuration not found for tenant {tenantId}");
    }

    return new TemporalClientService(temporalConfig);
});

builder.Services.AddScoped<IOpenAIClientService>(sp =>
{
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var tenantId = tenantContext.TenantId;

    // Fetch tenant-specific configuration
    var openAIClientServiceConfig = builder.Configuration.GetSection($"Tenants:{tenantId}:OpenAI").Get<OpenAIClientServiceConfig>();
    if (openAIClientServiceConfig == null)
    {
        throw new Exception($"OpenAI configuration not found for tenant {tenantId}");
    }
    Console.WriteLine($"OpenAI configuration for tenant {tenantId}: {openAIClientServiceConfig}");

    return new OpenAIClientService(openAIClientServiceConfig);
});

// Register the endpoints
builder.Services.AddScoped<WorkflowStarterEndpoint>();
builder.Services.AddScoped<WorkflowEventsEndpoint>();
builder.Services.AddScoped<WorkflowDefinitionEndpoint>();
builder.Services.AddScoped<WorkflowFinderEndpoint>();
builder.Services.AddScoped<WorkflowCancelEndpoint>();


var app = builder.Build();

app.UseMiddleware<TenantMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseExceptionHandler("/error");
app.UseCors("AllowAll");

app.MapPost("/api/workflows", async (
    HttpContext context,
    [FromServices] WorkflowStarterEndpoint endpoint) =>
{
    return await endpoint.HandleStartWorkflow(context);
})
.WithName("Create New Workflow")
.WithOpenApi();

app.MapGet("/api/workflows/{workflowId}/events", async (
    HttpContext context,
    [FromServices] WorkflowEventsEndpoint endpoint) =>
{
    return await endpoint.GetWorkflowEvents(context);
})
.WithName("Get Workflow Events")
.WithOpenApi();

app.MapGet("/api/workflows/{workflowType}/definition", async (
    HttpContext context,
    [FromServices] WorkflowDefinitionEndpoint endpoint) =>
{
    return await endpoint.GetWorkflowDefinition(context);
})
.WithName("Get Workflow Definition")
.WithOpenApi();

app.MapGet("/api/workflows", async (
    HttpContext context,
    [FromServices] WorkflowFinderEndpoint endpoint) =>
{
    return await endpoint.GetWorkflows(context);
})
.WithName("Get Workflows")
.WithOpenApi();

app.MapPost("/api/workflows/{workflowId}/cancel", async (
    HttpContext context,
    [FromServices] WorkflowCancelEndpoint endpoint) =>
{
    return await endpoint.CancelWorkflow(context);
})
.WithName("Cancel Workflow")
.WithOpenApi();

app.Run();
