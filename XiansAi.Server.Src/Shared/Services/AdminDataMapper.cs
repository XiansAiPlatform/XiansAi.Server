using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Shared.Data.Models;

namespace Shared.Services;

/// <summary>
/// Converts between document storage (BSON) and AdminAPI JSON payloads.
/// Matches the JsonElement ↔ BsonValue approach used by WebAPI/AgentAPI document services.
/// </summary>
internal static class AdminDataMapper
{
    public static AdminDataItemResponse ToItemResponse(Document document)
    {
        JsonElement content = default;
        if (document.Content != null && !document.Content.IsBsonNull)
        {
            var contentJson = document.Content.ToJson();
            content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        }

        Dictionary<string, object>? metadata = null;
        if (document.Metadata != null && !document.Metadata.IsBsonNull)
        {
            var metadataJson = document.Metadata.ToJson();
            metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
        }

        return new AdminDataItemResponse
        {
            Id = document.Id,
            Key = document.Key ?? string.Empty,
            Type = document.Type,
            AgentName = document.AgentId,
            ActivationName = document.ActivationName,
            ParticipantId = document.ParticipantId,
            Content = content,
            Metadata = metadata,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            ExpiresAt = document.ExpiresAt
        };
    }

    public static BsonValue ToBsonValue(JsonElement element)
    {
        var json = JsonSerializer.Serialize(element);
        return BsonSerializer.Deserialize<BsonValue>(json);
    }

    public static BsonDocument? ToBsonDocument(Dictionary<string, object>? metadata)
    {
        if (metadata == null)
        {
            return null;
        }

        var metadataJson = JsonSerializer.Serialize(metadata);
        var metadataBson = BsonSerializer.Deserialize<BsonValue>(metadataJson);
        return metadataBson.AsBsonDocument;
    }

    public static bool HasJsonContent(JsonElement content)
    {
        return content.ValueKind != JsonValueKind.Undefined && content.ValueKind != JsonValueKind.Null;
    }
}
