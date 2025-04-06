using Microsoft.OpenApi.Models;

public static class OpenAPIUtils
{
    public static OpenApiParameter CertificateParameter()
    {
        return new OpenApiParameter
        {
            Name = "X-Client-Cert",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Client certificate for authentication",
            Schema = new OpenApiSchema { Type = "string" }
        };
    }
}
