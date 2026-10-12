using Shared.Data.Models.HealthChecks;

namespace Shared.Services.HealthChecks;

/// <summary>What an executor observed. Persisted by the runner as an <see cref="AgentHealthCheckRun"/>.</summary>
public sealed record HealthCheckExecutionResult(
    HealthCheckStatus Status,
    string? Detail,
    string? ResponseSnippet = null,
    IReadOnlyList<HealthCheckAssertionResult>? AssertionResults = null)
{
    public static HealthCheckExecutionResult Pass(string? detail = null, string? response = null, IReadOnlyList<HealthCheckAssertionResult>? assertions = null)
        => new(HealthCheckStatus.Pass, detail, Snippet(response), assertions);

    public static HealthCheckExecutionResult Fail(string detail, string? response = null, IReadOnlyList<HealthCheckAssertionResult>? assertions = null)
        => new(HealthCheckStatus.Fail, detail, Snippet(response), assertions);

    public static HealthCheckExecutionResult Error(string detail)
        => new(HealthCheckStatus.Error, detail);

    private static string? Snippet(string? response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        return response.Length <= AgentHealthCheckRun.MaxSnippetLength
            ? response
            : response[..AgentHealthCheckRun.MaxSnippetLength];
    }
}
