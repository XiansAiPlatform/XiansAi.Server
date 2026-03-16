using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Data.Models;
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

public interface ISecretVaultService
{
    Task<ServiceResult<SecretVaultGetResponse>> CreateAsync(SecretVaultCreateInput input, string actorUserId);
    Task<ServiceResult<SecretVaultGetResponse?>> GetByIdAsync(string id);
    Task<ServiceResult<List<SecretVaultListItem>>> ListAsync(string? tenantId, string? agentId, string? activationName);
    Task<ServiceResult<SecretVaultGetResponse>> UpdateAsync(string id, SecretVaultUpdateInput input, string actorUserId);
    Task<ServiceResult<bool>> DeleteAsync(string id);
    Task<ServiceResult<SecretVaultFetchResponse?>> FetchByKeyAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName);
}

public class SecretVaultService : ISecretVaultService
{
    private const int AdditionalDataMaxKeys = 50;
    private const int AdditionalDataMaxKeyLength = 128;
    private const int AdditionalDataMaxValueLength = 2048;
    private const int AdditionalDataMaxTotalBytes = 8192;
    private static readonly Regex AdditionalDataKeyRegex = new(@"^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);

    private readonly ISecretVaultRepository _repository;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<SecretVaultService> _logger;
    private readonly string _uniqueSecret;

    public SecretVaultService(
        ISecretVaultRepository repository,
        ISecureEncryptionService encryption,
        ILogger<SecretVaultService> logger,
        IConfiguration configuration)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:SecretVaultKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:SecretVaultKey is not configured. Using BaseSecret.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<ServiceResult<SecretVaultGetResponse>> CreateAsync(SecretVaultCreateInput input, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(input.Key))
            return ServiceResult<SecretVaultGetResponse>.BadRequest("Key is required");
        if (string.IsNullOrWhiteSpace(input.Value))
            return ServiceResult<SecretVaultGetResponse>.BadRequest("Value is required");

        // Normalize scope values so that uniqueness is enforced on the composite (Key + scope) with consistent null handling.
        var normalizedTenantId = string.IsNullOrWhiteSpace(input.TenantId) ? null : input.TenantId;
        var normalizedAgentId = string.IsNullOrWhiteSpace(input.AgentId) ? null : input.AgentId;
        var normalizedUserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId;
        var normalizedActivationName = string.IsNullOrWhiteSpace(input.ActivationName) ? null : input.ActivationName;

        var exists = await _repository.ExistsByKeyAndScopeAsync(
            input.Key,
            normalizedTenantId,
            normalizedAgentId,
            normalizedUserId,
            normalizedActivationName);
        if (exists)
            return ServiceResult<SecretVaultGetResponse>.Conflict("A secret with this key already exists for the same scope");

        var (sanitizedAdditionalData, additionalDataError) = ValidateAndSanitizeAdditionalData(input.AdditionalData);
        if (additionalDataError != null)
            return ServiceResult<SecretVaultGetResponse>.BadRequest(additionalDataError);

        try
        {
            var encrypted = _encryption.Encrypt(input.Value, _uniqueSecret);
            var now = DateTime.UtcNow;
            var entity = new SecretVault
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Key = input.Key,
                EncryptedValue = encrypted,
                TenantId = normalizedTenantId,
                AgentId = normalizedAgentId,
                UserId = normalizedUserId,
                ActivationName = normalizedActivationName,
                AdditionalData = sanitizedAdditionalData,
                CreatedAt = now,
                CreatedBy = actorUserId
            };
            await _repository.CreateAsync(entity);
            var decrypted = _encryption.Decrypt(entity.EncryptedValue, _uniqueSecret);
            return ServiceResult<SecretVaultGetResponse>.Success(ToGetResponse(entity, decrypted!), StatusCode.Ok);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            _logger.LogWarning(ex, "Encryption failed for secret vault create");
            return ServiceResult<SecretVaultGetResponse>.InternalServerError("Encryption failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret vault key {Key}", input.Key);
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
            var decrypted = _encryption.Decrypt(entity.EncryptedValue, _uniqueSecret);
            return ServiceResult<SecretVaultGetResponse?>.Success(ToGetResponse(entity, decrypted ?? ""));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            _logger.LogWarning(ex, "Decryption failed for secret vault id {Id}", id);
            return ServiceResult<SecretVaultGetResponse?>.Conflict("Decryption failed; encryption keys may have changed");
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
                x.Id, x.Key, x.TenantId, x.AgentId, x.UserId, x.ActivationName, ParseAdditionalDataToObject(x.AdditionalData), x.CreatedAt, x.CreatedBy)).ToList();
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
            if (input.Value != null)
                entity.EncryptedValue = _encryption.Encrypt(input.Value, _uniqueSecret);
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

            // Enforce uniqueness on (Key + scope) combination, excluding the current document.
            var existsForScope = await _repository.ExistsByKeyAndScopeAsync(
                entity.Key,
                entity.TenantId,
                entity.AgentId,
                entity.UserId,
                entity.ActivationName,
                entity.Id);
            if (existsForScope)
                return ServiceResult<SecretVaultGetResponse>.Conflict("A secret with this key already exists for the same scope");

            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = actorUserId;

            await _repository.UpdateAsync(entity);
            var decrypted = _encryption.Decrypt(entity.EncryptedValue, _uniqueSecret);
            return ServiceResult<SecretVaultGetResponse>.Success(ToGetResponse(entity, decrypted ?? ""), StatusCode.Ok);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            _logger.LogWarning(ex, "Encryption failed for secret vault update");
            return ServiceResult<SecretVaultGetResponse>.InternalServerError("Encryption failed");
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
            return removed ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound("Secret not found");
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
            var decrypted = _encryption.Decrypt(entity.EncryptedValue, _uniqueSecret);
            return ServiceResult<SecretVaultFetchResponse?>.Success(
                new SecretVaultFetchResponse(decrypted ?? "", ParseAdditionalDataToObject(entity.AdditionalData)));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            _logger.LogWarning(ex, "Decryption failed for secret vault fetch key {Key}", key);
            return ServiceResult<SecretVaultFetchResponse?>.Conflict("Decryption failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching secret vault key {Key}", key);
            return ServiceResult<SecretVaultFetchResponse?>.InternalServerError("Failed to fetch secret");
        }
    }

    private static SecretVaultGetResponse ToGetResponse(SecretVault entity, string decryptedValue)
    {
        return new SecretVaultGetResponse(
            entity.Id,
            entity.Key,
            decryptedValue,
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
