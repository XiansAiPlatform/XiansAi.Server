using Features.WebApi.Services;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace Shared.Auditing;

/// <summary>
/// Wraps an endpoint's <see cref="IResult"/> so that, once the wrapped result has executed
/// with a 2xx status, an <see cref="IAuditActivityService"/> entry is recorded under an action
/// name (plus optional activation name / free-form details). Reports its status code
/// identically to the inner result, so wrapping never changes what the caller receives.
/// </summary>
public sealed class AuditedResult : IResult, IStatusCodeHttpResult
{
    private readonly IResult _inner;
    public string? ActivationName { get; }
    public Dictionary<string, object?>? Details { get; }

    internal AuditedResult(IResult inner, string? activationName, Dictionary<string, object?>? details)
    {
        _inner = inner;
        ActivationName = activationName;
        Details = details;
    }

    public int? StatusCode => (_inner as IStatusCodeHttpResult)?.StatusCode;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await _inner.ExecuteAsync(httpContext);

        if (StatusCode is not (null or (>= 200 and < 300)))
        {
            httpContext.RequestServices.GetService<ILogger<AuditedResult>>()?.LogInformation(
                "Skipping audit recording for {Endpoint}: response status {StatusCode} was not successful",
                httpContext.GetEndpoint()?.DisplayName ?? "endpoint",
                StatusCode);
            return;
        }

        var action = ResolveEndpointName(httpContext);
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }
        var description = ResolveEndpointDescription(httpContext);

        var auditActivityService = httpContext.RequestServices.GetService<IAuditActivityService>();
        if (auditActivityService == null)
        {
            return;
        }

        await auditActivityService.RecordActivityAsync(action, description, ActivationName, Details);
    }

    private static string? ResolveEndpointName(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? endpoint?.DisplayName;
    }

    /// <summary>
    /// Uses the endpoint's <c>.WithSummary(...)</c> metadata, not <c>.WithDescription(...)</c>:
    /// summaries are short one-liners, while descriptions in this codebase are often multi-
    /// paragraph markdown that would blow past <c>AuditActivity.Description</c>'s 500 character
    /// limit and silently fail to save. Truncated defensively in case a summary is ever longer
    /// than that anyway.
    /// </summary>
    private static string? ResolveEndpointDescription(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        var summary = endpoint?.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;
        return summary?.Length > 500 ? summary[..500] : summary;
    }
}

public static class AuditResultExtensions
{
    /// <summary>
    /// Marks an endpoint's result as audited: once it has executed with a 2xx status, records
    /// an <c>AuditActivity</c> entry, optionally tagged with the activation it applies to
    /// and/or free-form details.
    ///
    /// The action name recorded is the endpoint's <c>.WithName(...)</c> metadata (falling back
    /// to its route <c>DisplayName</c>) — there is no separate action string to pass here, so
    /// keep endpoint names consistent if you plan to filter or group on them in the audit trail.
    /// </summary>
    public static IResult WithAudit(
        this IResult result,
        string? activationName = null,
        Dictionary<string, object?>? details = null)
        => new AuditedResult(result, activationName, details);
}
