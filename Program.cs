using System.Reflection;
using Flowmaxer.Common;
using System.Runtime.Loader;
using Flowmaxer.Utils;

var builder = WebApplication.CreateBuilder(args);

var assemblyLoader = new AssemblyLoader(builder.Configuration);
ServiceConfigurator.ConfigureTemporalServices(builder.Services, builder.Configuration, assemblyLoader);

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

    var workflowTypes = assemblyLoader.GetWorkflowTypes();

    app.MapPost("/workflow/start", new WorkflowStarterEndpoint(workflowTypes).StartWorkflow)
       .WithName("StartWorkflow");
}
