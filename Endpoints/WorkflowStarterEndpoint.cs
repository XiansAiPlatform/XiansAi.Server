using System.Text.Json;
using Temporalio.Client;


/// <summary>
/// Endpoints for managing workflows.
/// </summary>
public class WorkflowStarterEndpoint
{
    private readonly IEnumerable<Type> workflowTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowEndpoints"/> class.
    /// </summary>
    /// <param name="workflowTypes">A collection of available workflow types.</param>
    public WorkflowStarterEndpoint(IEnumerable<Type> workflowTypes)
    {
        this.workflowTypes = workflowTypes;
    }

    /// <summary>
    /// Starts a workflow based on the request received in the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartWorkflow(HttpContext context)
    {
        var body = await ReadRequestBodyAsync(context);
        var request = DeserializeRequest(body);

        if (!ValidateRequest(context, request))
            return;

        var workflowTypeObj = FindWorkflowType(context, request!.WorkflowType);
        if (workflowTypeObj == null)
            return;

        var client = await GetTemporalClientAsync(context);
        var options = ConfigureWorkflowOptions(request.WorkflowType!);

        var handle = await StartWorkflowAsync(client, workflowTypeObj.Name, request.Input!, options);
        await RespondWithWorkflowHandleIdAsync(context, handle);
    }

    /// <summary>
    /// Reads the request body from the HTTP context asynchronously.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>A task that represents the asynchronous operation, containing the request body as a string.</returns>
    private async Task<string> ReadRequestBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        Console.WriteLine($"Received body: {body}"); // Log the raw request body
        return body;
    }

    /// <summary>
    /// Deserializes the request body into a <see cref="WorkflowRequest"/> object.
    /// </summary>
    /// <param name="body">The request body as a string.</param>
    /// <returns>A <see cref="WorkflowRequest"/> object or null if deserialization fails.</returns>
    private WorkflowRequest? DeserializeRequest(string body)
    {
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
            context.Response.WriteAsync("Invalid request payload.").Wait();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Finds the workflow type by name from the available workflow types.
    /// </summary>
    /// <param name="context">The HTTP context for setting response status and messages.</param>
    /// <param name="workflowType">The name of the workflow type to find.</param>
    /// <returns>The <see cref="Type"/> of the workflow if found; otherwise, null.</returns>
    private Type? FindWorkflowType(HttpContext context, string? workflowType)
    {
        var workflowTypeObj = workflowTypes.FirstOrDefault(t => t.Name == workflowType);
        if (workflowTypeObj == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.WriteAsync($"Workflow type '{workflowType}' not found.").Wait();
        }
        return workflowTypeObj;
    }

    /// <summary>
    /// Retrieves the Temporal client asynchronously from the service provider.
    /// </summary>
    /// <param name="context">The HTTP context containing the service provider.</param>
    /// <returns>A task that represents the asynchronous operation, containing the <see cref="TemporalClient"/>.</returns>
    private async Task<TemporalClient> GetTemporalClientAsync(HttpContext context)
    {
        var clientService = context.RequestServices.GetRequiredService<TemporalClientService>();
        return (TemporalClient) await clientService.GetClientAsync();
    }

    /// <summary>
    /// Configures the workflow options for starting a new workflow.
    /// </summary>
    /// <param name="workflowType">The type of the workflow to configure options for.</param>
    /// <returns>A <see cref="WorkflowOptions"/> object with configured settings.</returns>
    private WorkflowOptions ConfigureWorkflowOptions(string workflowType)
    {
        return new WorkflowOptions
        {
            TaskQueue = "flowmaxer-queue",
            Id = $"{workflowType}-{Guid.NewGuid()}"
        };
    }

    /// <summary>
    /// Starts a workflow asynchronously using the Temporal client.
    /// </summary>
    /// <param name="client">The Temporal client to use for starting the workflow.</param>
    /// <param name="workflowName">The name of the workflow to start.</param>
    /// <param name="input">The input data for the workflow.</param>
    /// <param name="options">The workflow options to use.</param>
    /// <returns>A task that represents the asynchronous operation, containing the workflow handle.</returns>
    private async Task<IWorkflowHandle> StartWorkflowAsync(TemporalClient client, string workflowName, string input, WorkflowOptions options)
    {
        return (IWorkflowHandle) await client.StartWorkflowAsync(workflowName, new[] { input }, options);
    }

    /// <summary>
    /// Sends a response with the workflow handle ID to the client.
    /// </summary>
    /// <param name="context">The HTTP context for sending the response.</param>
    /// <param name="handleId">The ID of the started workflow handle.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RespondWithWorkflowHandleIdAsync(HttpContext context, IWorkflowHandle handle)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync($"Workflow started successfully with ID: {handle.Id}");
    }
}