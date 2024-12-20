using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/api/workflow/start", async (HttpContext context) =>
{
    var endpoint = new WorkflowStarterEndpoint();
    string handle = await endpoint.StartWorkflow(context);
    context.Response.StatusCode = StatusCodes.Status200OK;
    await context.Response.WriteAsync($"Workflow started with handle - {handle}");
})
.WithName("WorkflowStart");

app.Run();
