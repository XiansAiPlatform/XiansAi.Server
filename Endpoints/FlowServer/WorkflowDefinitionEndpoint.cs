using OpenAI.Chat;

public class WorkflowDefinitionEndpointConfig
{
    public required string DefinitionFileLocation { get; set; }
    public required string ApiKey { get; set; }
    public required string Model { get; set; }
}


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

    public async Task<IResult> RegisterWorkflowDefinition(HttpContext context)
    {
        var workflowType = context.Request.RouteValues["workflowType"] as string;
        _logger.LogInformation("Registering workflow definition for {WorkflowType}", workflowType);

        await Task.Delay(1000);
        return Results.Ok();
    }

    public async Task<IResult> SaveWorkflowDefinition(HttpContext context)
    {
        var workflowType = context.Request.RouteValues["workflowType"] as string;
        _logger.LogInformation("Saving workflow definition for {WorkflowType}", workflowType);

        var workflowDefinition = await GetWorkflowDefinition(context);
        return workflowDefinition;
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
            new SystemChatMessage(GetSystemMessage()),
            new UserChatMessage(GetUserMessage(workflowType))
        };
        var workflowDefinition = await _openAIClientService.GetChatCompletionAsync(messages);

        await Task.Delay(10000);

        return Results.Ok(workflowDefinition);
    }

    private string GetUserMessage(string? workflowType)
    {
        return $"Generate a workflow definition for the following workflow type: {workflowType}";
    }

    private string GetSystemMessage()
    {
        return @"
        You are a mermaid flowchart definition generator for a Temporal workflow. You are given a 
        temporal workflow class and you generate a mermaid flowchart for it. Use the BPMN type notations
        to describe the workflow. 

        Include the following:
        - Start with nodes for flow parameters then flow start. 
        - End with flow end.
        - All activities in the workflow.
        - Decisions (if/else) in the workflow.
        - Loop (for/while) in the workflow.
        - Delays and Timers
        - Important data collection and storing of variables.
        - Signals in the workflow.
        - Child workflow in the workflow.
        - Signal handlers.


        Exclude/skip the following:
        - Timeout and Retry policies
        - Cron schedule in the workflow.

        Example output:
         flowchart TD
            classDef startEvent fill:#9acd32,stroke:#666,stroke-width:2px;
            classDef endEvent fill:#ff6347,stroke:#666,stroke-width:2px;
            classDef task fill:white,stroke:#4488cc,stroke-width:2px;
            classDef gateway fill:#ffd700,stroke:#666,stroke-width:2px;
            classDef loop fill:#87ceeb,stroke:#666,stroke-width:2px;
            classDef subprocess fill:white,stroke:#666,stroke-width:2px;
            
            Start(((Start Flow<br>●))) --> ScrapeLinks>Scrape News Links]
            ScrapeLinks --> ForEachLoop
            
            subgraph LoopProcess
                ForEachLoop((For Each<br>↻))
                ForEachLoop --> |Next Link| ScrapeDetails>Scrape News Details]
                ScrapeDetails --> SearchCompany>Google Search Company URL]
                SearchCompany --> CheckISV{Is ISV Company? <br> Yes/No}
                
                CheckISV -->|Yes| AddCompany>Add to isvCompanies]
                CheckISV -->|No| Delay
                AddCompany --> Delay>Delay 10s]
                
                Delay --> ForEachLoop
            end
            
            LoopProcess -->|Done| Return>Return Results]
            Return --> End(((End Flow<br>⬤)))
            
            subgraph Input Parameters
                Input1[sourceLink]
                Input2[prompt]
            end
            
            Input1 -.-> Start
            Input2 -.-> Start
            
            class Start startEvent
            class End endEvent
            class ForEachLoop loop
            class CheckISV gateway
            class Init,ScrapeLinks,ScrapeDetails,SearchCompany,AddCompany,Delay,Return task
            class Parameters,Config,LoopProcess subprocess
        
        Output restrictions:
        - Do not include any comments or other text in the output.
        - Do not include any code in the output.
        - Do not include ```mermaid or ``` in the output.
        ";
    }
}