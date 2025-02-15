using System.Xml;
using OpenAI.Chat;

namespace XiansAi.Server.GenAi;
public class MarkdownGenerator
{
    private readonly IOpenAIClientService _openAIClientService;
    private readonly ILogger _logger;
    public MarkdownGenerator(IOpenAIClientService openAIClientService, ILogger logger)
    {
        _logger = logger;
        _openAIClientService = openAIClientService;
    }

    public async Task<string?> GenerateMarkdown(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(Instruction.Prompt),
            new UserChatMessage("Workflow code:\n" + source)
        };
        var markdown = await _openAIClientService.GetChatCompletionAsync(messages);

        // Remove spaces between classes to make it valid mermaid code
        markdown = markdown.Replace(", ", ",");
        markdown = markdown.Replace("\"", "");

        _logger.LogInformation("New *markdown* generated for definition");

        return markdown;
    }
}

static class Instruction
{
    private static string Role = @"You are a specialized assistant that converts workflow code 
    into Mermaid flowchart markdown diagrams for mermaid version 11. Flows should be meaningful to business users. 
    Skip extracting technical nodes such as async, await, object validations etc.
    Mainly focus on identifying activities, parameters, and flow logic. 
    Only include the parent workflow method identified with the attribute '[WorkflowRun]'.

    Do not include any comments or other non Mermaid markdown text in the markdown. 
    **Do NOT generate ```mermaid or ``` symbols in the markdown.**
    Do not imagine any additional nodes or logic that is not present in the code. ";
    
    private static string Content = @"Generate a Mermaid flowchart that follows these rules:
        1. Use 'flowchart TD' syntax for top-down diagrams
        2. Apply relevant style classes as in the example:
        3. Identify and include:
        - Input parameters as a separate subgraph
        - Activity methods as task nodes
        - Loops as loop nodes with subgraphs
        - Conditional logic as gateway nodes";
    private static string Formatting = @"Markdown formatting rules:
        1. Do not include spaces in subgraph names
        2. Sub graph names should be unique and not repeated in the same flowchart";
    private static string Example = @"Example: 
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
  `
    ";

    public static string Prompt = Role + "\n\n" + Content + "\n\n" + Formatting + "\n\n" + Example;
}
