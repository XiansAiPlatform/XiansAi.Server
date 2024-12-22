using System.Text.Json;
using OpenAI.Chat;
using Temporalio.Client;


/* 
    curl -X GET http://localhost:5257/api/workflows/ProspectingWorkflow/definition -H "Content-Type: application/json" 
*/
public class WorkflowDefinitionEndpoint
{
    private readonly ILogger<WorkflowStarterEndpoint> _logger;
    private readonly IOpenAIClientService _openAIClientService;
    public WorkflowDefinitionEndpoint(
        ILogger<WorkflowStarterEndpoint> logger,
        IOpenAIClientService openAIClientService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _openAIClientService = openAIClientService ?? throw new ArgumentNullException(nameof(openAIClientService));
    }

    /// <summary>
    /// Handles the HTTP request to start a workflow.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>An IResult representing the HTTP response.</returns>
    public async Task<IResult> GetWorkflowDefinition(HttpContext context)
    {
        var workflowType = context.Request.RouteValues["workflowType"] as string;
        _logger.LogInformation("Getting workflow definition for {WorkflowType}", workflowType);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant that generates workflow definitions."),
            new UserChatMessage("Generate a workflow definition for the following workflow type: " + workflowType)
        };
        var workflowDefinition = await _openAIClientService.GetChatCompletionAsync(messages);

        return Results.Ok(workflowDefinition);
    }
}