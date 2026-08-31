namespace Shared.Providers;

public enum CacheInvalidationType
{
    UserAuth = 1,
    Tenant = 2,
    ApiKey = 3,
    Activation = 4,
    AgentWorkflowTypes = 5,
    ThreadOrigin = 6,
    ThreadId = 7
}

public sealed record CacheInvalidationEnvelope(
    CacheInvalidationType Type,
    string? UserId,
    string? TenantId,
    IReadOnlyList<string>? Keys,
    DateTimeOffset PublishedAtUtc);
