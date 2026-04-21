using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

public record SecretVaultCreateInput(
    string Key,
    string Value,
    string? TenantId,
    string? AgentId,
    string? UserId,
    string? ActivationName,
    string? AdditionalData);

public record SecretVaultUpdateInput(
    string? Value,
    string? TenantId,
    string? AgentId,
    string? UserId,
    string? ActivationName,
    string? AdditionalData);

/// <summary>AdditionalData is returned as JSON object for API consumers.</summary>
public record SecretVaultListItem(
    string Id,
    string Key,
    string? TenantId,
    string? AgentId,
    string? UserId,
    string? ActivationName,
    object? AdditionalData,
    DateTime CreatedAt,
    string CreatedBy);

/// <summary>AdditionalData is returned as JSON object for API consumers.</summary>
public record SecretVaultGetResponse(
    string Id,
    string Key,
    string Value,
    string? TenantId,
    string? AgentId,
    string? UserId,
    string? ActivationName,
    object? AdditionalData,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy);

/// <summary>AdditionalData is returned as JSON object for API consumers.</summary>
public record SecretVaultFetchResponse(string Value, object? AdditionalData);

/// <summary>
/// Value-redacted view of a secret. Used by the Admin API where the secret value must never be exposed.
/// Mirrors <see cref="SecretVaultGetResponse"/> minus the <c>Value</c> field.
/// </summary>
public record SecretVaultMetadataResponse(
    string Id,
    string Key,
    string? TenantId,
    string? AgentId,
    string? UserId,
    string? ActivationName,
    object? AdditionalData,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy);

public interface ISecretVaultService
{
    Task<ServiceResult<SecretVaultGetResponse>> CreateAsync(SecretVaultCreateInput input, string actorUserId);
    Task<ServiceResult<SecretVaultGetResponse?>> GetByIdAsync(string id);
    Task<ServiceResult<List<SecretVaultListItem>>> ListAsync(string? tenantId, string? agentId, string? activationName);
    Task<ServiceResult<SecretVaultGetResponse>> UpdateAsync(string id, SecretVaultUpdateInput input, string actorUserId);
    Task<ServiceResult<bool>> DeleteAsync(string id);
    Task<ServiceResult<SecretVaultFetchResponse?>> FetchByKeyAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName);

    /// <summary>
    /// Returns metadata for a secret without ever loading or returning the secret value.
    /// The store provider is not consulted, so this is also cheaper than <see cref="GetByIdAsync"/>.
    /// Intended for the Admin API.
    /// </summary>
    Task<ServiceResult<SecretVaultMetadataResponse?>> GetMetadataByIdAsync(string id);

    /// <summary>
    /// Resolves a secret by key + scope and returns its metadata only (no value).
    /// Useful for "does this scoped secret exist?" admin probes.
    /// </summary>
    Task<ServiceResult<SecretVaultMetadataResponse?>> FindMetadataByKeyAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName);
}

public class SecretVaultService : ISecretVaultService
{
    private const int AdditionalDataMaxKeys = 50;
    private const int AdditionalDataMaxKeyLength = 128;
    private const int AdditionalDataMaxValueLength = 2048;
    private const int AdditionalDataMaxTotalBytes = 8192;
    private static readonly Regex AdditionalDataKeyRegex = new(@"^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);

    private readonly ISecretVaultRepository _repository;
    private readonly ISecretStoreProvider _secretStore;
    private readonly ILogger<SecretVaultService> _logger;

    public SecretVaultService(
        ISecretVaultRepository repository,
        ISecretStoreProvider secretStore,
        ILogger<SecretVaultService> logger)
    {
        _repository = repository;
        _secretStore = secretStore;
        _logger = logger;
    }

    public async Task<ServiceResult<SecretVaultGetResponse>> CreateAsync(SecretVaultCreateInput input, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(input.Key))
            return ServiceResult<SecretVaultGetResponse>.BadRequest("Key is required");
        if (string.IsNullOrWhiteSpace(input.Value))
            return ServiceResult<SecretVaultGetResponse>.BadRequest("Value is required");

        var exists = await _repository.ExistsByKeyAsync(input.Key, input.TenantId);
        if (exists)
            return ServiceResult<SecretVaultGetResponse>.Conflict("A secret with this key already exists");

        var (sanitizedAdditionalData, additionalDataError) = ValidateAndSanitizeAdditionalData(input.AdditionalData);
        if (additionalDataError != null)
            return ServiceResult<SecretVaultGetResponse>.BadRequest(additionalDataError);

        var id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        var storeWritten = false;

        try
        {
            await _secretStore.SetAsync(id, input.Value);
            storeWritten = true;

            var now = DateTime.UtcNow;
            var entity = new SecretVault
            {
                Id = id,
                Key = input.Key,
                TenantId = string.IsNullOrWhiteSpace(input.TenantId) ? null : input.TenantId,
                AgentId = string.IsNullOrWhiteSpace(input.AgentId) ? null : input.AgentId,
                UserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId,
                ActivationName = string.IsNullOrWhiteSpace(input.ActivationName) ? null : input.ActivationName,
                AdditionalData = sanitizedAdditionalData,
                CreatedAt = now,
                CreatedBy = actorUserId
            };
            await _repository.CreateAsync(entity);

            _logger.LogInformation(
                "Secret vault entry created. id={SecretId} key={Key} tenant={TenantId} actor={Actor} provider={Provider}",
                id, input.Key, entity.TenantId ?? "*", actorUserId, _secretStore.Name);

            return ServiceResult<SecretVaultGetResponse>.Success(ToGetResponse(entity, input.Value), StatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error creating secret vault key {Key} (provider={Provider}). storeWritten={StoreWritten}",
                input.Key, _secretStore.Name, storeWritten);

            if (storeWritten)
            {
                try
                {
                    await _secretStore.DeleteAsync(id);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx,
                        "Failed to clean up orphaned secret value for id {SecretId} after metadata insert failure",
                        id);
                }
            }

            return ServiceResult<SecretVaultGetResponse>.InternalServerError("Failed to create secret");
        }
    }

    public async Task<ServiceResult<SecretVaultGetResponse?>> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ServiceResult<SecretVaultGetResponse?>.BadRequest("Id is required");

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null)
            return ServiceResult<SecretVaultGetResponse?>.NotFound("Secret not found");

        try
        {
            var value = await _secretStore.GetAsync(entity.Id);
            if (value == null)
            {
                _logger.LogError(
                    "Secret value missing in store for id {SecretId} (provider={Provider})",
                    entity.Id, _secretStore.Name);
                return ServiceResult<SecretVaultGetResponse?>.Conflict("Secret value missing in store");
            }

            return ServiceResult<SecretVaultGetResponse?>.Success(ToGetResponse(entity, value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting secret vault id {Id}", id);
            return ServiceResult<SecretVaultGetResponse?>.InternalServerError("Failed to get secret");
        }
    }

    public async Task<ServiceResult<List<SecretVaultListItem>>> ListAsync(string? tenantId, string? agentId, string? activationName)
    {
        try
        {
            var list = await _repository.ListAsync(tenantId, agentId, activationName);
            var items = list.Select(x => new SecretVaultListItem(
                x.Id, x.Key, x.TenantId, x.AgentId, x.UserId, x.ActivationName,
                ParseAdditionalDataToObject(x.AdditionalData), x.CreatedAt, x.CreatedBy)).ToList();
            return ServiceResult<List<SecretVaultListItem>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secret vault");
            return ServiceResult<List<SecretVaultListItem>>.InternalServerError("Failed to list secrets");
        }
    }

    public async Task<ServiceResult<SecretVaultGetResponse>> UpdateAsync(string id, SecretVaultUpdateInput input, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ServiceResult<SecretVaultGetResponse>.BadRequest("Id is required");

        var entity = await _repository.GetByIdAsync(id);
        if (entity == null)
            return ServiceResult<SecretVaultGetResponse>.NotFound("Secret not found");

        try
        {
            string? updatedValue = null;
            if (input.Value != null)
            {
                await _secretStore.SetAsync(entity.Id, input.Value);
                updatedValue = input.Value;
            }

            if (input.TenantId != null)
                entity.TenantId = string.IsNullOrWhiteSpace(input.TenantId) ? null : input.TenantId;
            if (input.AgentId != null)
                entity.AgentId = string.IsNullOrWhiteSpace(input.AgentId) ? null : input.AgentId;
            if (input.UserId != null)
                entity.UserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId;
            if (input.ActivationName != null)
                entity.ActivationName = string.IsNullOrWhiteSpace(input.ActivationName) ? null : input.ActivationName;
            if (input.AdditionalData != null)
            {
                var (sanitizedAdditionalData, additionalDataError) = ValidateAndSanitizeAdditionalData(input.AdditionalData);
                if (additionalDataError != null)
                    return ServiceResult<SecretVaultGetResponse>.BadRequest(additionalDataError);
                entity.AdditionalData = sanitizedAdditionalData;
            }

            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = actorUserId;

            await _repository.UpdateAsync(entity);

            _logger.LogInformation(
                "Secret vault entry updated. id={SecretId} key={Key} tenant={TenantId} actor={Actor} valueChanged={ValueChanged} provider={Provider}",
                entity.Id, entity.Key, entity.TenantId ?? "*", actorUserId, updatedValue != null, _secretStore.Name);

            // For the response value: prefer the just-set value to avoid an extra round-trip to the store.
            var responseValue = updatedValue ?? await _secretStore.GetAsync(entity.Id) ?? string.Empty;
            return ServiceResult<SecretVaultGetResponse>.Success(ToGetResponse(entity, responseValue), StatusCode.Ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating secret vault id {Id}", id);
            return ServiceResult<SecretVaultGetResponse>.InternalServerError("Failed to update secret");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ServiceResult<bool>.BadRequest("Id is required");

        try
        {
            var removed = await _repository.DeleteAsync(id);
            if (!removed)
                return ServiceResult<bool>.NotFound("Secret not found");

            try
            {
                await _secretStore.DeleteAsync(id);
            }
            catch (Exception storeEx)
            {
                // Metadata is already gone; log the orphan but do not fail the caller.
                _logger.LogError(storeEx,
                    "Metadata for secret {SecretId} was deleted but store {Provider} delete failed; value may need manual cleanup",
                    id, _secretStore.Name);
            }

            _logger.LogInformation("Secret vault entry deleted. id={SecretId} provider={Provider}", id, _secretStore.Name);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret vault id {Id}", id);
            return ServiceResult<bool>.InternalServerError("Failed to delete secret");
        }
    }

    public async Task<ServiceResult<SecretVaultFetchResponse?>> FetchByKeyAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ServiceResult<SecretVaultFetchResponse?>.BadRequest("Key is required");

        var entity = await _repository.FindForAccessAsync(key, tenantId, agentId, userId, activationName);
        if (entity == null)
            return ServiceResult<SecretVaultFetchResponse?>.NotFound("Secret not found or access denied");

        try
        {
            var value = await _secretStore.GetAsync(entity.Id);
            if (value == null)
            {
                _logger.LogError(
                    "Secret value missing in store for key {Key} id {SecretId} (provider={Provider})",
                    key, entity.Id, _secretStore.Name);
                return ServiceResult<SecretVaultFetchResponse?>.Conflict("Secret value missing in store");
            }

            return ServiceResult<SecretVaultFetchResponse?>.Success(
                new SecretVaultFetchResponse(value, ParseAdditionalDataToObject(entity.AdditionalData)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching secret vault key {Key}", key);
            return ServiceResult<SecretVaultFetchResponse?>.InternalServerError("Failed to fetch secret");
        }
    }

    public async Task<ServiceResult<SecretVaultMetadataResponse?>> GetMetadataByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ServiceResult<SecretVaultMetadataResponse?>.BadRequest("Id is required");

        try
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
                return ServiceResult<SecretVaultMetadataResponse?>.NotFound("Secret not found");

            return ServiceResult<SecretVaultMetadataResponse?>.Success(ToMetadataResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting secret vault metadata for id {Id}", id);
            return ServiceResult<SecretVaultMetadataResponse?>.InternalServerError("Failed to get secret metadata");
        }
    }

    public async Task<ServiceResult<SecretVaultMetadataResponse?>> FindMetadataByKeyAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ServiceResult<SecretVaultMetadataResponse?>.BadRequest("Key is required");

        try
        {
            var entity = await _repository.FindForAccessAsync(key, tenantId, agentId, userId, activationName);
            if (entity == null)
                return ServiceResult<SecretVaultMetadataResponse?>.NotFound("Secret not found or access denied");

            return ServiceResult<SecretVaultMetadataResponse?>.Success(ToMetadataResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding secret vault metadata for key {Key}", key);
            return ServiceResult<SecretVaultMetadataResponse?>.InternalServerError("Failed to find secret metadata");
        }
    }

    private static SecretVaultMetadataResponse ToMetadataResponse(SecretVault entity) => new(
        entity.Id,
        entity.Key,
        entity.TenantId,
        entity.AgentId,
        entity.UserId,
        entity.ActivationName,
        ParseAdditionalDataToObject(entity.AdditionalData),
        entity.CreatedAt,
        entity.CreatedBy,
        entity.UpdatedAt,
        entity.UpdatedBy);

    private static SecretVaultGetResponse ToGetResponse(SecretVault entity, string value)
    {
        return new SecretVaultGetResponse(
            entity.Id,
            entity.Key,
            value,
            entity.TenantId,
            entity.AgentId,
            entity.UserId,
            entity.ActivationName,
            ParseAdditionalDataToObject(entity.AdditionalData),
            entity.CreatedAt,
            entity.CreatedBy,
            entity.UpdatedAt,
            entity.UpdatedBy);
    }

    /// <summary>Parses stored JSON string to object for API response; returns null if empty or invalid.</summary>
    private static object? ParseAdditionalDataToObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Normalizes request AdditionalData to JSON string: object is serialized, string used as-is. For use by API endpoints.</summary>
    public static string? NormalizeAdditionalDataFromRequest(object? value)
    {
        if (value == null) return null;
        if (value is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
        try { return JsonSerializer.Serialize(value); }
        catch { return null; }
    }

    /// <summary>
    /// Validates and sanitizes additionalData: flat key-value pairs with values as string, number, or boolean only.
    /// Keys: alphanumeric, underscore, period, hyphen only; max length 128. Values: string (sanitized, max 2048), number, or boolean.
    /// Max 50 keys, max 8KB total. Returns (sanitized JSON string, or null) and (error message, or null).
    /// </summary>
    private (string? SanitizedJson, string? ErrorMessage) ValidateAndSanitizeAdditionalData(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, "additionalData must be a JSON object with string, number, or boolean values only.");

            var count = 0;
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var prop in root.EnumerateObject())
            {
                if (count >= AdditionalDataMaxKeys)
                    return (null, $"additionalData cannot have more than {AdditionalDataMaxKeys} keys.");

                var key = prop.Name.Trim();
                if (string.IsNullOrEmpty(key)) continue;

                if (key.Length > AdditionalDataMaxKeyLength)
                    key = key[..AdditionalDataMaxKeyLength];

                if (!AdditionalDataKeyRegex.IsMatch(key))
                    key = SanitizeAdditionalDataKey(key);
                if (string.IsNullOrEmpty(key)) continue;

                object? value;
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        var valueStr = prop.Value.GetString() ?? "";
                        valueStr = SanitizeAdditionalDataValue(valueStr);
                        if (valueStr.Length > AdditionalDataMaxValueLength)
                            valueStr = valueStr[..AdditionalDataMaxValueLength];
                        value = valueStr;
                        break;
                    case JsonValueKind.Number:
                        if (prop.Value.TryGetInt64(out var i64))
                            value = i64;
                        else if (prop.Value.TryGetDouble(out var d))
                            value = d;
                        else
                            continue; // skip unrepresentable numbers
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        value = prop.Value.GetBoolean();
                        break;
                    case JsonValueKind.Object:
                    case JsonValueKind.Array:
                        return (null, "additionalData values must be string, number, or boolean only; nested objects or arrays are not allowed.");
                    default:
                        continue;
                }

                result[key] = value;
                count++;
            }

            if (result.Count == 0) return (null, null);

            var json = JsonSerializer.Serialize(result);
            if (json.Length > AdditionalDataMaxTotalBytes)
                return (null, $"additionalData total size cannot exceed {AdditionalDataMaxTotalBytes / 1024} KB.");

            return (json, null);
        }
        catch (JsonException)
        {
            return (null, "additionalData must be valid JSON.");
        }
    }

    private static string SanitizeAdditionalDataKey(string key)
    {
        var sanitized = new char[key.Length];
        var len = 0;
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-')
                sanitized[len++] = c;
        }
        return len == 0 ? "" : new string(sanitized, 0, len);
    }

    private static string SanitizeAdditionalDataValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        value = value.Trim();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c >= 0x20 || c == '\t' || c == '\n' || c == '\r')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
