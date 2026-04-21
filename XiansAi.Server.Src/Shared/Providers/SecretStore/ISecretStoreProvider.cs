namespace Shared.Providers;

/// <summary>
/// Pluggable backend for storing and retrieving Secret Vault values.
/// Metadata (key, scope, audit) is always persisted in MongoDB by the Secret Vault repository;
/// only the secret <b>value</b> goes through this provider.
///
/// Implementations must:
/// <list type="bullet">
///   <item>Treat the <c>secretId</c> as an opaque identifier (do not log or expose it externally).</item>
///   <item>Never include the secret value in any thrown exception, log message, or metric.</item>
///   <item>Be safe to call concurrently for different ids.</item>
/// </list>
/// </summary>
public interface ISecretStoreProvider
{
    /// <summary>
    /// Stable, lower-case identifier of the active provider (e.g. <c>"database"</c>, <c>"azurekeyvault"</c>).
    /// Useful for diagnostics and audit logs.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Stores or replaces the value for the given <paramref name="secretId"/>.
    /// </summary>
    /// <param name="secretId">Stable identifier (typically the SecretVault Mongo _id).</param>
    /// <param name="value">The plaintext secret value to persist. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync(string secretId, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the previously stored plaintext value, or <c>null</c> if no value exists for that id.
    /// </summary>
    Task<string?> GetAsync(string secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the value for the given id. No-op if it does not exist.
    /// </summary>
    Task DeleteAsync(string secretId, CancellationToken cancellationToken = default);
}
