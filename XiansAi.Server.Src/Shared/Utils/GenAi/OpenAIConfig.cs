namespace Shared.Utils.GenAi;
public class OpenAIConfig
{
    public required string ApiKeyKeyVaultName { get; set; }
    public required string ApiKey { get; set; }
    public required string Model { get; set; }
}