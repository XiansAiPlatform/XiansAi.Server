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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseExceptionHandler("/error");

app.MapPost("/api/workflows", async (
    HttpContext context,
    [FromServices] WorkflowStarterEndpoint endpoint) =>
{
    return await endpoint.HandleStartWorkflow(context);
})
.WithName("Start a New Workflow")
.WithOpenApi()
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest) 
.Produces(StatusCodes.Status500InternalServerError);


app.MapGet("/api/workflows/{workflowId}/events", async (
    HttpContext context,
    [FromServices] WorkflowEventsEndpoint endpoint) =>
{
    return await endpoint.GetWorkflowEvents(context);
})
.WithName("GetWorkflowEvents")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/api/workflows/{workflowType}/definition", async (
    HttpContext context,
    [FromServices] WorkflowDefinitionEndpoint endpoint) =>
{
    return await endpoint.GetWorkflowDefinition(context);
})
.WithName("GetWorkflowDefinition")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

app.Run();
