using System.Diagnostics;
using System.Text.Json;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using Features.WebApi.Models;
using XiansAi.Server.Providers;

namespace Features.WebApi.Services;

/// <summary>
/// Interface for the AI Copilot Code Generation Service
/// </summary>
public interface ICopilotService
{
    /// <summary>
    /// Generates C# agent code using AI Copilot based on user description
    /// </summary>
    /// <param name="request">Code generation request</param>
    /// <returns>Copilot-generated agent code response</returns>
    Task<ServiceResult<CopilotCodeResponse>> GenerateAgentCodeAsync(CopilotCodeRequest request);

    /// <summary>
    /// Refines existing code using AI Copilot based on user feedback
    /// </summary>
    /// <param name="currentCode">Current generated code</param>
    /// <param name="refinementRequest">User's refinement request</param>
    /// <param name="conversationHistory">Previous conversation context</param>
    /// <returns>Copilot-refined agent code response</returns>
    Task<ServiceResult<CopilotCodeResponse>> RefineCodeAsync(string currentCode, string refinementRequest, List<ChatMessage>? conversationHistory = null);
}

/// <summary>
/// AI Copilot service for generating C# agent code using LLM
/// </summary>
public class CopilotService : ICopilotService
{
    private readonly ILlmService _llmService;
    private readonly ILogger<CopilotService> _logger;
    private readonly ITenantContext _tenantContext;

    // Predefined templates
    private static readonly List<CopilotTemplate> _availableTemplates = new()
    {
        new CopilotTemplate
        {
            Id = "research-agent",
            Name = "Research Agent",
            Description = "Agent that performs deep research and creates comprehensive reports",
            Category = "Analysis",
            Tags = new List<string> { "research", "analysis", "reports" },
            ExampleUseCases = new List<string> 
            { 
                "Market research analysis", 
                "Academic research compilation", 
                "Industry trend analysis" 
            }
        },
        new CopilotTemplate
        {
            Id = "data-processing",
            Name = "Data Processing Agent",
            Description = "Agent that processes and analyzes various data formats",
            Category = "Data",
            Tags = new List<string> { "data", "processing", "analysis" },
            ExampleUseCases = new List<string> 
            { 
                "CSV data analysis", 
                "Log file processing", 
                "API data aggregation" 
            }
        },
        new CopilotTemplate
        {
            Id = "monitoring",
            Name = "Monitoring Agent",
            Description = "Agent that monitors systems and sends alerts",
            Category = "Operations",
            Tags = new List<string> { "monitoring", "alerts", "operations" },
            ExampleUseCases = new List<string> 
            { 
                "Website uptime monitoring", 
                "Performance tracking", 
                "Error detection" 
            }
        },
        new CopilotTemplate
        {
            Id = "custom-generic",
            Name = "Generic Agent",
            Description = "Flexible agent template for custom workflows",
            Category = "General",
            Tags = new List<string> { "generic", "flexible", "custom" },
            ExampleUseCases = new List<string> 
            { 
                "Custom business logic", 
                "API integrations", 
                "Scheduled tasks" 
            }
        }
    };

    public CopilotService(
        ILlmService llmService,
        ILogger<CopilotService> logger,
        ITenantContext tenantContext)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<ServiceResult<CopilotCodeResponse>> GenerateAgentCodeAsync(CopilotCodeRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Starting Copilot agent code generation for user {UserId} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Validate input
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return ServiceResult<CopilotCodeResponse>.BadRequest("Description is required for code generation");
            }

            // Build conversation context
            var messages = BuildConversationMessages(request.Description, request.ConversationHistory, request.TemplateId);

            // Generate code using LLM with structured output
            var model = _llmService.GetModel();
            var structuredResponse = await _llmService.GetStructuredChatCompletionAsync<LlmCopilotResponse>(messages, model);

            // Convert structured response to our response format
            var codeResponse = ConvertStructuredResponse(structuredResponse);

            // Automatically validate the generated code internally (validate the main workflow file)
            var mainCode = codeResponse.Files?.FirstOrDefault()?.Content ?? "";
            var validationResult = await ValidateCodeAsync(new ValidateCodeOptions() 
            { 
                Code = mainCode,
                Options = new CodeValidationOptions
                {
                    CheckCompilation = true,
                    CheckSecurity = true,
                    CheckBestPractices = false // Keep it simple for now
                }
            });

            // Enhance the assistant message with validation feedback if there are issues
            var assistantMessage = codeResponse.Message;
            if (validationResult.IsSuccess && validationResult.Data != null && !validationResult.Data.IsValid)
            {
                var issues = validationResult.Data.Errors?.Count + validationResult.Data.Warnings?.Count;
                if (issues > 0)
                {
                    assistantMessage += $"\n\nNote: I've generated the code, but detected {issues} potential issue(s). The code should still work, but you might want to review it.";
                }
            }

            // Create metadata
            var metadata = new GenerationMetadata
            {
                Model = model,
                GeneratedAt = DateTime.UtcNow,
                GenerationDurationMs = stopwatch.ElapsedMilliseconds
            };

            var response = new CopilotCodeResponse
            {
                GeneratedFiles = codeResponse.Files,
                SuggestedAgentName = codeResponse.AgentName,
                AgentDescription = codeResponse.Description,
                AssistantMessage = assistantMessage,
                UsedTemplate = request.TemplateId,
                Metadata = metadata
            };

            _logger.LogInformation("Copilot successfully generated agent code in {Duration}ms for user {UserId}", 
                stopwatch.ElapsedMilliseconds, _tenantContext.LoggedInUser);

            return ServiceResult<CopilotCodeResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copilot error generating agent code for user {UserId}", _tenantContext.LoggedInUser);
            return ServiceResult<CopilotCodeResponse>.InternalServerError("Copilot failed to generate agent code: " + ex.Message);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task<ServiceResult<List<CopilotTemplate>>> GetAvailableTemplatesAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving available templates for user {UserId}", _tenantContext.LoggedInUser);
            
            // For now, return predefined templates
            // In the future, this could query a database for user/tenant-specific templates
            await Task.Delay(1); // Simulate async operation
            
            return ServiceResult<List<CopilotTemplate>>.Success(_availableTemplates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving templates for user {UserId}", _tenantContext.LoggedInUser);
            return ServiceResult<List<CopilotTemplate>>.InternalServerError("Failed to retrieve templates: " + ex.Message);
        }
    }

    private async Task<ServiceResult<ValidateCodeResponse>> ValidateCodeAsync(ValidateCodeOptions options)
    {
        try
        {
            _logger.LogInformation("Validating generated code for user {UserId}", _tenantContext.LoggedInUser);

            var response = new ValidateCodeResponse
            {
                IsValid = true,
                Errors = new List<ValidationError>(),
                Warnings = new List<ValidationWarning>(),
                Suggestions = new List<ValidationSuggestion>()
            };

            // Basic validation checks
            if (string.IsNullOrWhiteSpace(options.Code))
            {
                response.IsValid = false;
                response.Errors!.Add(new ValidationError
                {
                    Message = "Code cannot be empty",
                    Severity = ErrorSeverity.Error,
                    Category = "Structure"
                });
                return ServiceResult<ValidateCodeResponse>.Success(response);
            }

            // Check for basic C# structure
            await ValidateBasicCSharpStructure(options.Code, response);

            // Additional validation based on options
            if (options.Options != null)
            {
                if (options.Options.CheckSecurity)
                {
                    await ValidateSecurityConcerns(options.Code, response);
                }

                if (options.Options.CheckBestPractices)
                {
                    await ValidateBestPractices(options.Code, response);
                }
            }

            return ServiceResult<ValidateCodeResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating code for user {UserId}", _tenantContext.LoggedInUser);
            return ServiceResult<ValidateCodeResponse>.InternalServerError("Failed to validate code: " + ex.Message);
        }
    }

    public async Task<ServiceResult<CopilotCodeResponse>> RefineCodeAsync(string currentCode, string refinementRequest, List<ChatMessage>? conversationHistory = null)
    {
        try
        {
            _logger.LogInformation("Refining agent code for user {UserId}", _tenantContext.LoggedInUser);

            // Build refinement messages
            var messages = BuildRefinementMessages(currentCode, refinementRequest, conversationHistory);

            // Generate refined code using LLM with structured output
            var model = _llmService.GetModel();
            var structuredResponse = await _llmService.GetStructuredChatCompletionAsync<LlmCopilotResponse>(messages, model);

            // Convert structured response to our response format
            var codeResponse = ConvertStructuredResponse(structuredResponse);

            var response = new CopilotCodeResponse
            {
                GeneratedFiles = codeResponse.Files,
                SuggestedAgentName = codeResponse.AgentName,
                AgentDescription = codeResponse.Description,
                AssistantMessage = codeResponse.Message,
                UsedTemplate = null,
                Metadata = new GenerationMetadata
                {
                    Model = model,
                    GeneratedAt = DateTime.UtcNow
                }
            };

            return ServiceResult<CopilotCodeResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refining agent code for user {UserId}", _tenantContext.LoggedInUser);
            return ServiceResult<CopilotCodeResponse>.InternalServerError("Failed to refine agent code: " + ex.Message);
        }
    }

    #region Private Methods

    private List<XiansAi.Server.Providers.ChatMessage> BuildConversationMessages(string description, List<ChatMessage>? history, string? templateId)
    {
        var messages = new List<XiansAi.Server.Providers.ChatMessage>();

        // System prompt for agent generation
        var systemPrompt = GetSystemPrompt(templateId);
        messages.Add(new XiansAi.Server.Providers.ChatMessage
        {
            Role = "system",
            Content = systemPrompt
        });

        // Add conversation history if provided
        if (history != null)
        {
            foreach (var msg in history)
            {
                messages.Add(new XiansAi.Server.Providers.ChatMessage
                {
                    Role = msg.Role,
                    Content = msg.Content
                });
            }
        }

        // Add current user request
        messages.Add(new XiansAi.Server.Providers.ChatMessage
        {
            Role = "user",
            Content = $"Please generate a C# agent based on this description: {description}"
        });

        return messages;
    }

    private List<XiansAi.Server.Providers.ChatMessage> BuildRefinementMessages(string currentCode, string refinementRequest, List<ChatMessage>? history)
    {
        var messages = new List<XiansAi.Server.Providers.ChatMessage>();

        // System prompt for code refinement
        messages.Add(new XiansAi.Server.Providers.ChatMessage
        {
            Role = "system",
            Content = GetRefinementSystemPrompt()
        });

        // Add conversation history if provided
        if (history != null)
        {
            foreach (var msg in history)
            {
                messages.Add(new XiansAi.Server.Providers.ChatMessage
                {
                    Role = msg.Role,
                    Content = msg.Content
                });
            }
        }

        // Add current code and refinement request
        messages.Add(new XiansAi.Server.Providers.ChatMessage
        {
            Role = "user",
            Content = $"Here's the current code:\n\n```csharp\n{currentCode}\n```\n\nPlease refine it based on this request: {refinementRequest}"
        });

        return messages;
    }

    private string GetSystemPrompt(string? templateId)
    {
        var basePrompt = @"You are an expert C# developer specializing in creating temporal workflow agents using the XiansAi platform. 

Your task is to generate complete, working C# agent code based on user descriptions. Follow these guidelines:

1. **Multi-File Structure**: Always generate separate files for better organization:
   - Workflow class file (inheriting from FlowBase)
   - Activities interface and implementation file
   - Program.cs file with dependency injection setup
2. **Namespace**: Use appropriate namespaces based on the agent purpose
3. **Workflow Attribute**: Include proper [Workflow(""Agent Name: Workflow"")] attributes
4. **Activities**: Define clear activities with [Activity] attributes
5. **Error Handling**: Include proper logging and error handling
6. **Documentation**: Add XML documentation comments
7. **File Organization**: Each file should be focused and contain related code only

**Multi-File Structure Example:**

**File 1: [AgentName]Workflow.cs**
```csharp
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using XiansAi.Flow;

namespace [AgentNamespace];

[Workflow(""[Agent Name]: Workflow"")]
public class [AgentName]Workflow : FlowBase
{
    private readonly ActivityOptions _activityOptions = new()
    {
        ScheduleToCloseTimeout = TimeSpan.FromMinutes(30)
    };

    [WorkflowRun]
    public async Task<string> Run(string input)
    {
        // Implementation
        return await Workflow.ExecuteActivityAsync(
            (I[AgentName]Activities act) => act.ProcessAsync(input), 
            _activityOptions);
    }
}
```

**File 2: [AgentName]Activities.cs**
```csharp
using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace [AgentNamespace];

public interface I[AgentName]Activities
{
    [Activity]
    Task<string> ProcessAsync(string input);
}

public class [AgentName]Activities : I[AgentName]Activities
{
    private readonly ILogger<[AgentName]Activities> _logger;

    public [AgentName]Activities(ILogger<[AgentName]Activities> logger)
    {
        _logger = logger;
    }

    public async Task<string> ProcessAsync(string input)
    {
        _logger.LogInformation(""Processing: {Input}"", input);
        // Implementation
        return $""Processed: {input}"";
    }
}
```

**File 3: Program.cs**
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Extensions.Hosting;
using [AgentNamespace];

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTemporalClient();
builder.Services.AddScoped<I[AgentName]Activities, [AgentName]Activities>();

var host = builder.Build();
await host.RunAsync();
```

**Available Agent Templates:**
- **Research Agent**: For deep research and comprehensive reports (market analysis, academic research, trend analysis)
- **Data Processing Agent**: For processing and analyzing various data formats (CSV analysis, log processing, API aggregation)  
- **Monitoring Agent**: For system monitoring and alerts (uptime monitoring, performance tracking, error detection)
- **Generic Agent**: Flexible template for custom workflows (business logic, API integrations, scheduled tasks)

**Response Requirements:**
Generate complete, working C# agent code with proper structure and formatting. The response will be automatically formatted, so focus on creating high-quality, properly structured C# code with:

- Proper indentation and formatting
- Complete using statements
- Full class implementations
- XML documentation comments
- Error handling and logging
- XiansAi platform compatibility

Your response will include:
1. **AgentName**: A descriptive name for the agent
2. **Description**: Clear description of what the agent does
3. **Message**: Your response to the user explaining what you've created
4. **Files**: Array of code files with:
   - **FileName**: Descriptive filename (e.g., ""CustomerAnalysisWorkflow.cs"")
   - **Content**: Complete, properly formatted C# code
   - **Description**: What this file contains";

        // Add template-specific guidance if specified
        if (!string.IsNullOrEmpty(templateId))
        {
            var template = _availableTemplates.FirstOrDefault(t => t.Id == templateId);
            if (template != null)
            {
                basePrompt += $"\n\n**Using Template: {template.Name}**\n{template.Description}\n";
                if (template.ExampleUseCases != null)
                {
                    basePrompt += $"Focus on these use cases: {string.Join(", ", template.ExampleUseCases)}\n";
                }
            }
        }
        else
        {
            basePrompt += "\n\n**Instructions**: Analyze the user's description and automatically select the most appropriate template pattern from the available options above. Use the template's characteristics to guide your code generation.";
        }

        return basePrompt;
    }

    private string GetRefinementSystemPrompt()
    {
        return @"You are an expert C# developer helping to refine and improve existing agent code. 

Your task is to modify the provided code based on the user's refinement request while maintaining:
1. Proper C# syntax and structure
2. XiansAi platform compatibility
3. Existing functionality where not explicitly changed
4. Code quality and best practices

**Response Requirements:**
Modify the provided code based on the user's refinement request while maintaining quality and XiansAi compatibility. Focus on creating properly structured C# code with:

- Clean, readable formatting and indentation
- Maintained functionality where not explicitly changed
- Proper error handling and logging
- Code quality improvements

Your response will include:
1. **AgentName**: Updated agent name (if changed)
2. **Description**: Updated description of what the agent does
3. **Message**: Explanation of the changes you made
4. **Files**: Updated code files with the requested modifications";
    }

    private (List<GeneratedFile> Files, string AgentName, string Description, string Message) ConvertStructuredResponse(LlmCopilotResponse structuredResponse)
    {
        // Convert structured files to our internal format
        var files = structuredResponse.Files.Select(sf => new GeneratedFile
        {
            FileName = sf.FileName,
            Content = sf.Content, // Content is already properly formatted from structured output
            Description = sf.Description,
            Language = "csharp"
        }).ToList();

        _logger.LogInformation("Copilot successfully generated agent: {AgentName}, FileCount={FileCount}", 
            structuredResponse.AgentName, files.Count);

        return (
            Files: files,
            AgentName: structuredResponse.AgentName,
            Description: structuredResponse.Description,
            Message: structuredResponse.Message
        );
    }

    // Note: ExtractCodeFromMarkdown and UnescapeJsonContent methods removed
    // as they're no longer needed with OpenAI structured output

    private async Task ValidateBasicCSharpStructure(string code, ValidateCodeResponse response)
    {
        await Task.Delay(1); // Simulate async validation

        // Check for basic C# keywords
        if (!code.Contains("class") && !code.Contains("interface"))
        {
            response.Warnings!.Add(new ValidationWarning
            {
                Message = "Code should contain at least one class or interface declaration",
                Category = "Structure"
            });
        }

        // Check for workflow patterns
        if (!code.Contains("FlowBase"))
        {
            response.Warnings!.Add(new ValidationWarning
            {
                Message = "Agent workflows should inherit from FlowBase",
                Category = "XiansAi"
            });
        }

        if (!code.Contains("[Workflow"))
        {
            response.Warnings!.Add(new ValidationWarning
            {
                Message = "Workflow classes should have [Workflow] attribute",
                Category = "XiansAi"
            });
        }
    }

    private async Task ValidateSecurityConcerns(string code, ValidateCodeResponse response)
    {
        await Task.Delay(1); // Simulate async validation

        // Check for potentially dangerous patterns
        var dangerousPatterns = new[]
        {
            "System.Diagnostics.Process.Start",
            "File.Delete",
            "Directory.Delete",
            "Registry.",
            "Environment.Exit"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (code.Contains(pattern))
            {
                response.Warnings!.Add(new ValidationWarning
                {
                    Message = $"Potentially dangerous operation detected: {pattern}",
                    Category = "Security"
                });
            }
        }
    }

    private async Task ValidateBestPractices(string code, ValidateCodeResponse response)
    {
        await Task.Delay(1); // Simulate async validation

        // Check for best practices
        if (!code.Contains("ILogger"))
        {
            response.Suggestions!.Add(new ValidationSuggestion
            {
                Message = "Consider adding logging using ILogger for better observability",
                SuggestedImprovement = "Inject ILogger<ClassName> in constructor and use it for logging important events"
            });
        }

        if (!code.Contains("async") && !code.Contains("await"))
        {
            response.Suggestions!.Add(new ValidationSuggestion
            {
                Message = "Consider using async/await patterns for better performance",
                SuggestedImprovement = "Make methods async and use await for I/O operations"
            });
        }
    }

    #endregion
} 