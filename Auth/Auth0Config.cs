public class Auth0Config
{
    public string? Domain { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
}

public class ManagementApiConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}