using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
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

builder.Services.Configure<TemporalConfig>(
    builder.Configuration.GetSection("Temporal"));

builder.Services.AddSingleton(sp => 
    sp.GetRequiredService<IOptions<TemporalConfig>>().Value);
    
builder.Services.AddSingleton<ITemporalClientService, TemporalClientService>();
builder.Services.AddSingleton<IOpenAIClientService>(sp =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OpenAI API Key not found in environment variables");
        
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL")
        ?? throw new InvalidOperationException("OpenAI Model not found in environment variables");

    return new OpenAIClientService(apiKey, model);
});
// Register the endpoints
builder.Services.AddScoped<WorkflowStarterEndpoint>();
builder.Services.AddScoped<WorkflowEventsEndpoint>();
builder.Services.AddScoped<WorkflowDefinitionEndpoint>();
builder.Services.AddScoped<WorkflowFinderEndpoint>();
builder.Services.AddScoped<WorkflowCancelEndpoint>();

var app = builder.Build();

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
.WithName("Cerate New Workflow")
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
