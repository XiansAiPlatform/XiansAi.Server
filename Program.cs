using System.Reflection;
using Flowmaxer.Common;
using System.Runtime.Loader;
using Flowmaxer.Utils;

var builder = WebApplication.CreateBuilder(args);

ServiceConfigurator.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

ConfigurePipeline(app);

app.Run();



void ConfigurePipeline(WebApplication app)
{
    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    var assemblyLoader = new AssemblyLoader(app.Configuration);
    var workflowTypes = assemblyLoader.GetWorkflowTypes();

    app.MapPost("/workflow/start", new WorkflowStarterEndpoint(workflowTypes).StartWorkflow)
       .WithName("StartWorkflow");
}
