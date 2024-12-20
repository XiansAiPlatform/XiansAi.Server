using System.Text.Json;
using Temporalio.Client;


/// <summary>
/// Endpoints for managing workflows.
/// </summary>
public class WorkflowStarterEndpoint
{
    private readonly TemporalConfig _config = new TemporalConfig();

    /// <summary>
    /// Starts a workflow based on the request received in the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<string> StartWorkflow(HttpContext context)
    {
        /*
            curl -X POST http://localhost:5257/workflow/start \
                -H "Content-Type: application/json" \
                -d '{"WorkflowType": "SampleWorkflow", "Input": "Hello, World!"}'
        */

        // Read the request body
        var request = await ReadRequestBodyAsync(context);
        // Validate the request
        if (!ValidateRequest(context, request))
            return "Invalid request";

        var client = await new TemporalClientService(_config).GetClientAsync();
        var options = new WorkflowOptions
        {
            TaskQueue = _config.TaskQueue,
            Id = $"{request!.WorkflowType!}-{Guid.NewGuid()}"
        };
        // Start the workflow
        var handle = await client.StartWorkflowAsync(request.WorkflowType!, request.Parameters ?? Array.Empty<string>(), options);
        // Respond with the workflow handle ID
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync($"Workflow started successfully with ID: {handle.Id}");
        return handle.Id;
    }

    /// <summary>
    /// Reads the request body from the HTTP context asynchronously.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>A task that represents the asynchronous operation, containing the request body as a string.</returns>
    private async Task<WorkflowRequest?> ReadRequestBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<WorkflowRequest>(body);
    }


    /// <summary>
    /// Validates the deserialized workflow request.
    /// </summary>
    /// <param name="context">The HTTP context for setting response status and messages.</param>
    /// <param name="request">The deserialized workflow request.</param>
    /// <returns>True if the request is valid; otherwise, false.</returns>
    private bool ValidateRequest(HttpContext context, WorkflowRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.WorkflowType))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.WriteAsync("Invalid request payload. Expected a JSON object with a WorkflowType and Input properties.").Wait();
            return false;
        }
        return true;
    }


}