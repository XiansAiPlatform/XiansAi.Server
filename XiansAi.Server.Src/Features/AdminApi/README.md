# AdminAPI

## Overview

AdminAPI provides administrative operations for managing the XiansAI platform. It offers endpoints for managing tenants, agents, templates, knowledge bases, workflows, and messaging configurations.

AdminAPI is designed for:
- System administrators
- DevOps teams
- Automated management scripts
- Internal tools and dashboards

## Key Features

- **API Key Authentication**: Secure access using admin API keys
- **Multi-tenant Support**: Manage multiple tenants from a single API
- **Versioned API**: Supports multiple API versions (currently v1)
- **Debug Logging**: Comprehensive request/response logging for troubleshooting
- **OpenAPI Documentation**: Auto-generated API documentation

## API Versioning

AdminAPI follows a URL-based versioning strategy:

```
/api/v1/admin/...
/api/v2/admin/...
```

Current version: **v1**

See [API_VERSIONING_GUIDE.md](./API_VERSIONING_GUIDE.md) for details on adding new API versions.

## Authentication

AdminAPI uses API key authentication via the `X-Admin-Api-Key` header:

```http
X-Admin-Api-Key: your-admin-api-key-here
```

### Authentication Implementation

- **Authentication Scheme**: `AdminEndpointApiKeyScheme`
- **Handler**: `AdminEndpointAuthenticationHandler`
- **Authorization Policy**: `AdminEndpointAuthPolicy`
- **Requirement**: `ValidAdminEndpointAccessRequirement`

## Available Endpoints

### Tenant Management
- `GET /api/v1/admin/tenants` - List all tenants
- `GET /api/v1/admin/tenants/{tenantId}` - Get tenant by ID
- `POST /api/v1/admin/tenants` - Create tenant
- `PATCH /api/v1/admin/tenants/{tenantId}` - Update tenant
- `DELETE /api/v1/admin/tenants/{tenantId}` - Delete tenant

### Agent Management
- `GET /api/v1/admin/agents` - List all agents
- `GET /api/v1/admin/agents/{agentId}` - Get agent by ID
- `POST /api/v1/admin/agents` - Create agent
- `PATCH /api/v1/admin/agents/{agentId}` - Update agent
- `DELETE /api/v1/admin/agents/{agentId}` - Delete agent

### Template Management
- `GET /api/v1/admin/templates` - List all templates
- `GET /api/v1/admin/templates/{templateId}` - Get template by ID
- `POST /api/v1/admin/templates` - Create template
- `PATCH /api/v1/admin/templates/{templateId}` - Update template
- `DELETE /api/v1/admin/templates/{templateId}` - Delete template

### Knowledge Management
- `GET /api/v1/admin/knowledge` - List knowledge entries
- `GET /api/v1/admin/knowledge/{knowledgeId}` - Get knowledge by ID
- `POST /api/v1/admin/knowledge` - Create knowledge
- `PATCH /api/v1/admin/knowledge/{knowledgeId}` - Update knowledge
- `DELETE /api/v1/admin/knowledge/{knowledgeId}` - Delete knowledge

### Workflow Management
- `GET /api/v1/admin/workflows` - List workflows
- `GET /api/v1/admin/workflows/{workflowId}` - Get workflow by ID
- `POST /api/v1/admin/workflows` - Create workflow
- `PATCH /api/v1/admin/workflows/{workflowId}` - Update workflow
- `DELETE /api/v1/admin/workflows/{workflowId}` - Delete workflow

### Messaging Configuration
- `GET /api/v1/admin/messaging` - List messaging configurations
- `POST /api/v1/admin/messaging` - Create messaging configuration
- `PATCH /api/v1/admin/messaging/{configId}` - Update messaging configuration
- `DELETE /api/v1/admin/messaging/{configId}` - Delete messaging configuration

### Ownership Management
- `POST /api/v1/admin/ownership/transfer` - Transfer resource ownership

## Debug Logging

AdminAPI includes comprehensive debug logging middleware that captures all request and response details.

### Enable Debug Logging

Add to `appsettings.json` or `appsettings.Development.json`:

```json
{
  "AdminApi": {
    "EnableDebugLogging": true
  }
}
```

### What Gets Logged

**Request Information:**
- HTTP Method, Path, Query String
- Headers (sensitive headers redacted)
- Request Body (formatted JSON)
- Unique Request ID

**Response Information:**
- Status Code, Headers
- Response Body (formatted JSON)
- Elapsed Time
- Request ID (for correlation)

**Security:**
- Sensitive headers automatically redacted
- Only AdminAPI paths logged

See [DEBUG_LOGGING.md](./DEBUG_LOGGING.md) for complete documentation.

## Project Structure

```
AdminApi/
├── README.md                           # This file
├── API_VERSIONING_GUIDE.md             # API versioning documentation
├── DEBUG_LOGGING.md                    # Debug logging documentation
├── ADMIN_API_EXAMPLE.http              # Example HTTP requests
├── Auth/                               # Authentication handlers
│   ├── AdminEndpointAuthenticationHandler.cs
│   ├── ValidAdminEndpointAccessHandler.cs
│   └── ValidAdminEndpointAccessRequirement.cs
├── Configuration/                      # Service configuration
│   └── AdminApiConfiguration.cs
├── Constants/                          # Constants and configuration
│   └── AdminApiConstants.cs
├── Endpoints/                          # API endpoints
│   ├── AdminAgentEndpoints.cs
│   ├── AdminKnowledgeEndpoints.cs
│   ├── AdminMessagingEndpoints.cs
│   ├── AdminOwnershipEndpoints.cs
│   ├── AdminTemplateEndpoints.cs
│   ├── AdminTenantEndpoints.cs
│   └── WorkflowManagementEndpoints.cs
└── Utils/                              # Utilities and middleware
    └── AdminApiDebugLoggingMiddleware.cs
```

## Configuration

### Service Registration

AdminAPI services are registered in `Program.cs`:

```csharp
builder.AddAdminApiServices();
builder.AddAdminApiAuth();
```

### Middleware and Endpoints

AdminAPI middleware and endpoints are configured in `Program.cs`:

```csharp
app.UseAdminApiMiddleware();
app.UseAdminApiEndpoints();
```

### Configuration Options

```json
{
  "AdminApi": {
    "EnableDebugLogging": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Features.AdminApi": "Debug"
    }
  }
}
```

## Usage Examples

See [ADMIN_API_EXAMPLE.http](./ADMIN_API_EXAMPLE.http) for complete examples.

### Create a Tenant

```http
POST /api/v1/admin/tenants
X-Admin-Api-Key: your-admin-api-key
Content-Type: application/json

{
  "tenantId": "acme01",
  "name": "Acme Corporation",
  "description": "Acme Corp tenant",
  "isActive": true
}
```

### List All Tenants

```http
GET /api/v1/admin/tenants
X-Admin-Api-Key: your-admin-api-key
```

### Update a Tenant

```http
PATCH /api/v1/admin/tenants/acme01
X-Admin-Api-Key: your-admin-api-key
Content-Type: application/json

{
  "name": "Acme Corporation - Updated",
  "isActive": true
}
```

## Development

### Adding New Endpoints

1. Create endpoint methods in the appropriate file under `Endpoints/`
2. Map endpoints in `AdminApiConfiguration.MapAdminApiVersion()`
3. Add OpenAPI documentation using `.WithOpenApi()`
4. Apply authorization using `.RequireAuthorization("AdminEndpointAuthPolicy")`

### Adding New API Versions

See [API_VERSIONING_GUIDE.md](./API_VERSIONING_GUIDE.md) for detailed instructions.

## Testing

### Using HTTP Files

Use the provided [ADMIN_API_EXAMPLE.http](./ADMIN_API_EXAMPLE.http) file with your IDE's HTTP client.

### Integration Tests

Integration tests are located in:
```
XiansAi.Server.Tests/IntegrationTests/AdminApi/
```

### Debug Logging for Testing

Enable debug logging during development to see detailed request/response information:

```json
{
  "AdminApi": {
    "EnableDebugLogging": true
  }
}
```

## Security Considerations

1. **API Key Management**: Store admin API keys securely (environment variables, Azure Key Vault, etc.)
2. **Rate Limiting**: AdminAPI is subject to global rate limiting policies
3. **HTTPS**: Always use HTTPS in production
4. **Tenant Isolation**: Some endpoints require `X-Tenant-Id` header for tenant-scoped operations
5. **Debug Logging**: Only enable debug logging when actively troubleshooting; disable in production

## Related Documentation

- [API Versioning Guide](./API_VERSIONING_GUIDE.md)
- [Debug Logging Documentation](./DEBUG_LOGGING.md)
- [Authentication Configuration](../../../docs/AUTH_CONFIGURATION.md)
- [Rate Limiting Implementation](../../../docs/RATE_LIMITING_IMPLEMENTATION.md)
- [OpenAPI Documentation](../../../docs/OPENAPI_DOCS.md)

## Support

For issues or questions about AdminAPI:
1. Check the debug logs (enable `AdminApi:EnableDebugLogging`)
2. Review the OpenAPI documentation at `/swagger`
3. Check existing integration tests for usage examples
4. Review related documentation files

## Future Enhancements

- [ ] Admin dashboard UI
- [ ] Audit logging for all administrative operations
- [ ] Bulk operations support
- [ ] Export/import configuration
- [ ] Advanced filtering and pagination
- [ ] Real-time notifications for admin operations
