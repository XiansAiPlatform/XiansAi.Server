using System.ComponentModel.DataAnnotations;
using XiansAi.Server.Providers;

namespace Features.WebApi.Models;

/// <summary>
/// Request model for Copilot code generation
/// </summary>
public class CopilotCodeRequest
{
    /// <summary>
    /// User's description of what the agent should do
    /// </summary>
    [Required]
    public required string Description { get; set; }

    /// <summary>
    /// Optional conversation context for iterative refinement
    /// </summary>
    public List<ChatMessage>? ConversationHistory { get; set; }

    /// <summary>
    /// Template to use as base (optional)
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Additional parameters for code generation
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Response model for Copilot generated code
/// </summary>
public class CopilotCodeResponse
{
    /// <summary>
    /// The generated C# agent files
    /// </summary>
    public required List<GeneratedFile> GeneratedFiles { get; set; }

    /// <summary>
    /// Suggested agent name based on description
    /// </summary>
    public required string SuggestedAgentName { get; set; }

    /// <summary>
    /// Description of what the agent does
    /// </summary>
    public required string AgentDescription { get; set; }

    /// <summary>
    /// AI Copilot's response message
    /// </summary>
    public required string AssistantMessage { get; set; }

    /// <summary>
    /// Template used for generation (if any)
    /// </summary>
    public string? UsedTemplate { get; set; }

    /// <summary>
    /// Generation metadata
    /// </summary>
    public GenerationMetadata? Metadata { get; set; }
}

/// <summary>
/// Represents a generated code file
/// </summary>
public class GeneratedFile
{
    /// <summary>
    /// File name with extension
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// File content
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// File type/language for syntax highlighting
    /// </summary>
    public string Language { get; set; } = "csharp";

    /// <summary>
    /// File description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Relative path within the project
    /// </summary>
    public string? RelativePath { get; set; }
}

/// <summary>
/// Generation metadata
/// </summary>
public class GenerationMetadata
{
    /// <summary>
    /// Model used for generation
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Generation timestamp
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tokens used in generation
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Generation duration in milliseconds
    /// </summary>
    public long? GenerationDurationMs { get; set; }
}

/// <summary>
/// Request model for saving generated code to GitHub
/// </summary>
public class SaveToGitHubRequest
{
    /// <summary>
    /// Repository name
    /// </summary>
    [Required]
    public required string RepositoryName { get; set; }

    /// <summary>
    /// File path within the repository
    /// </summary>
    [Required]
    public required string FilePath { get; set; }

    /// <summary>
    /// The code content to save
    /// </summary>
    [Required]
    public required string Content { get; set; }

    /// <summary>
    /// Commit message
    /// </summary>
    [Required]
    public required string CommitMessage { get; set; }

    /// <summary>
    /// Branch name (optional, defaults to main)
    /// </summary>
    public string? Branch { get; set; }
}

/// <summary>
/// Response model for GitHub save operation
/// </summary>
public class SaveToGitHubResponse
{
    /// <summary>
    /// Whether the save was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// URL to the created file on GitHub
    /// </summary>
    public string? FileUrl { get; set; }

    /// <summary>
    /// Commit SHA
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// Status message
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Copilot template information
/// </summary>
public class CopilotTemplate
{
    /// <summary>
    /// Template identifier
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Template name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Template description
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Template category
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Template tags
    /// </summary>
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Template example use cases
    /// </summary>
    public List<string>? ExampleUseCases { get; set; }
}

/// <summary>
/// Request model for validating generated code
/// </summary>
public class ValidateCodeOptions
{
    /// <summary>
    /// The C# code to validate
    /// </summary>
    [Required]
    public required string Code { get; set; }

    /// <summary>
    /// Validation options
    /// </summary>
    public CodeValidationOptions? Options { get; set; }
}

/// <summary>
/// Code validation options
/// </summary>
public class CodeValidationOptions
{
    /// <summary>
    /// Check for compilation errors
    /// </summary>
    public bool CheckCompilation { get; set; } = true;

    /// <summary>
    /// Check for security issues
    /// </summary>
    public bool CheckSecurity { get; set; } = true;

    /// <summary>
    /// Check for best practices
    /// </summary>
    public bool CheckBestPractices { get; set; } = false;
}

/// <summary>
/// Response model for code validation
/// </summary>
public class ValidateCodeResponse
{
    /// <summary>
    /// Whether the code is valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors
    /// </summary>
    public List<ValidationError>? Errors { get; set; }

    /// <summary>
    /// Validation warnings
    /// </summary>
    public List<ValidationWarning>? Warnings { get; set; }

    /// <summary>
    /// Validation suggestions
    /// </summary>
    public List<ValidationSuggestion>? Suggestions { get; set; }
}

/// <summary>
/// Validation error
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Error message
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Line number (if applicable)
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Error severity
    /// </summary>
    public ErrorSeverity Severity { get; set; }

    /// <summary>
    /// Error category
    /// </summary>
    public string? Category { get; set; }
}

/// <summary>
/// Validation warning
/// </summary>
public class ValidationWarning
{
    /// <summary>
    /// Warning message
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Line number (if applicable)
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Warning category
    /// </summary>
    public string? Category { get; set; }
}

/// <summary>
/// Validation suggestion
/// </summary>
public class ValidationSuggestion
{
    /// <summary>
    /// Suggestion message
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Line number (if applicable)
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Suggested improvement
    /// </summary>
    public string? SuggestedImprovement { get; set; }
}

/// <summary>
/// Error severity levels
/// </summary>
public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Request model for refining existing code
/// </summary>
public class RefineCodeRequest
{
    /// <summary>
    /// Current code to be refined
    /// </summary>
    [Required]
    public required string CurrentCode { get; set; }

    /// <summary>
    /// User's refinement request
    /// </summary>
    [Required]
    public required string RefinementRequest { get; set; }

    /// <summary>
    /// Previous conversation context
    /// </summary>
    public List<ChatMessage>? ConversationHistory { get; set; }
}

/// <summary>
/// Structured response model for LLM Copilot code generation (used with OpenAI structured output)
/// This matches the existing CopilotCodeResponse structure for direct compatibility
/// </summary>
public class LlmCopilotResponse
{
    /// <summary>
    /// Generated code files
    /// </summary>
    [Required]
    public required List<LlmGeneratedFile> Files { get; set; }

    /// <summary>
    /// Suggested agent name
    /// </summary>
    [Required]
    public required string AgentName { get; set; }

    /// <summary>
    /// Description of what the agent does
    /// </summary>
    [Required]
    public required string Description { get; set; }

    /// <summary>
    /// Copilot's message to the user
    /// </summary>
    [Required]
    public required string Message { get; set; }
}

/// <summary>
/// Structured file model for LLM responses
/// This matches the existing GeneratedFile structure for direct compatibility
/// </summary>
public class LlmGeneratedFile
{
    /// <summary>
    /// Name of the file (e.g., "WorkflowName.cs")
    /// </summary>
    [Required]
    public required string FileName { get; set; }

    /// <summary>
    /// Complete C# code content for the file
    /// </summary>
    [Required]
    public required string Content { get; set; }

    /// <summary>
    /// Description of what this file contains
    /// </summary>
    [Required]
    public required string Description { get; set; }
} 