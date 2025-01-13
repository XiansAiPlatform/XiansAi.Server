using System.Text.Json.Serialization;

public class AppMetadata
{
    [JsonPropertyName("tenants")]
    public required string[] Tenants { get; set; } = [];
}