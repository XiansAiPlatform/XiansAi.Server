using System.Collections.Concurrent;
using Temporalio.Client;

namespace Shared.Utils.Temporal;

/// <summary>
/// In-memory Temporal client cache keyed by the caller's tenant/agent, while tracking the
/// tenant whose Temporal config actually produced the connection (the origin tenant).
/// </summary>
public sealed class TemporalClientCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public static string BuildRequestKey(string tenantId, string? agentName)
    {
        return string.IsNullOrEmpty(agentName) ? tenantId : $"{tenantId}:{agentName}";
    }

    public bool TryGet(string requestKey, out ITemporalClient client)
    {
        if (_entries.TryGetValue(requestKey, out var entry))
        {
            client = entry.Client;
            return true;
        }

        client = null!;
        return false;
    }

    public bool TryGetByConfigTenant(string configTenantId, out ITemporalClient client)
    {
        foreach (var entry in _entries.Values)
        {
            if (string.Equals(entry.ConfigTenantId, configTenantId, StringComparison.Ordinal))
            {
                client = entry.Client;
                return true;
            }
        }

        client = null!;
        return false;
    }

    public void Add(string requestKey, string configTenantId, ITemporalClient client)
    {
        var entry = new CacheEntry(client, configTenantId);
        _entries[requestKey] = entry;

        // Also index the tenant-default key so GetClientsAsync / GetClientAsync() without an
        // agent can find a connection that was first opened via an origin-routed agent.
        if (!string.Equals(requestKey, configTenantId, StringComparison.Ordinal))
        {
            _entries.TryAdd(configTenantId, entry);
        }
    }

    /// <summary>
    /// Evicts the tenant-default key, per-agent keys for that tenant, and any other tenant's
    /// agent keys whose connection was resolved through this tenant as OriginTenant.
    /// Returns client instances that are no longer referenced and should be disposed.
    /// </summary>
    public IReadOnlyList<ITemporalClient> RemoveByTenant(string tenantId)
    {
        var removedClients = new List<ITemporalClient>();
        foreach (var pair in _entries)
        {
            if (!IsKeyForTenant(pair.Key, tenantId)
                && !string.Equals(pair.Value.ConfigTenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_entries.TryRemove(pair.Key, out var entry))
            {
                removedClients.Add(entry.Client);
            }
        }

        var stillReferenced = _entries.Values.Select(entry => entry.Client).ToHashSet();
        return removedClients
            .Distinct()
            .Where(client => !stillReferenced.Contains(client))
            .ToList();
    }

    public IEnumerable<ITemporalClient> GetDistinctClients()
    {
        return _entries.Values.Select(entry => entry.Client).Distinct();
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public static bool IsKeyForTenant(string clientKey, string tenantId)
    {
        return string.Equals(clientKey, tenantId, StringComparison.Ordinal)
            || clientKey.StartsWith($"{tenantId}:", StringComparison.Ordinal);
    }

    private sealed record CacheEntry(ITemporalClient Client, string ConfigTenantId);
}
