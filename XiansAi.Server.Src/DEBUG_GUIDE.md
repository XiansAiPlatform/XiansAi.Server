# Debugging Guide for XiansAi.Server

## Quick Start Debugging

### 1. Set Breakpoints
- Click in the left margin of any line in Cursor to set a breakpoint
- For the AgentService, set breakpoints on lines like:
  - Line 58: `var sanitizedUserId = ValidationHelpers.SanitizeString(_tenantContext.LoggedInUser);`
  - Line 311: `_logger.LogWarning("Invalid agent name provided for getting definitions");`
  - Line 354: `// Skip invalid definitions`

### 2. Start Debugging
1. **Press F5** or go to **Run and Debug** panel (Ctrl+Shift+D)
2. Select **"Debug WebApi (All Services)"** from the dropdown
3. Click the green play button

### 3. Debug Configurations Available

#### Debug WebApi (All Services)
- Runs all services (WebApi, AgentApi, UserApi)
- Good for testing the full application

#### Debug WebApi Only
- Runs only the WebApi service
- Faster startup, focused debugging

#### Debug AgentService Tests
- Runs the test suite
- Good for debugging specific test scenarios

#### Debug with Custom Env File
- Uses a custom environment file
- Useful for different configurations

#### Attach to Process
- Attaches to a running process
- Useful when the app is already running

## Debugging the AgentService

### Setting Breakpoints in AgentService.cs

```csharp
// Line 58: Input validation
var sanitizedUserId = ValidationHelpers.SanitizeString(_tenantContext.LoggedInUser);

// Line 67: Permission check
if (!ValidationHelpers.IsValidRequiredString(sanitizedUserId))

// Line 311: Error logging
_logger.LogWarning("Invalid agent name provided for getting definitions");

// Line 354: Skip invalid definitions
// Skip invalid definitions
```

### Debugging Steps

1. **Set breakpoints** in the AgentService methods you want to debug
2. **Start debugging** with F5
3. **Make API calls** to trigger the breakpoints:

```bash
# Test GetAgentNames
curl -X GET "http://localhost:5001/api/client/agents/names" \
  -H "Authorization: Bearer your-token" \
  -H "X-Tenant-Id: your-tenant"

# Test GetDefinitions
curl -X GET "http://localhost:5001/api/client/agents/test-agent/definitions/basic" \
  -H "Authorization: Bearer your-token" \
  -H "X-Tenant-Id: your-tenant"
```

## Environment Configuration

### Create .env.development file:

```bash
# Authentication Provider
AuthProvider__Provider=Keycloak

# Keycloak Configuration
Keycloak__AuthServerUrl=https://localhost:8080
Keycloak__Realm=master
Keycloak__ValidIssuer=https://localhost:8080/realms/master

# Development Settings
ASPNETCORE_ENVIRONMENT=Development
Auth__RequireHttpsMetadata=false

# MongoDB Configuration
MongoDB__ConnectionString=mongodb://localhost:27017
MongoDB__DatabaseName=xiansai_dev

# Logging
Logging__LogLevel__Features.WebApi.Services.AgentService=Debug
```

## Debugging Tips

### 1. Watch Variables
- In the Debug panel, add variables to watch:
  - `sanitizedUserId`
  - `sanitizedTenantId`
  - `agentName`
  - `result`

### 2. Step Through Code
- **F10**: Step Over (execute current line)
- **F11**: Step Into (go into method calls)
- **Shift+F11**: Step Out (exit current method)
- **F5**: Continue execution

### 3. Debug Console
- Use the Debug Console to evaluate expressions:
  ```csharp
  sanitizedUserId
  _tenantContext.LoggedInUser
  _tenantContext.TenantId
  ```

### 4. Conditional Breakpoints
- Right-click on a breakpoint
- Select "Edit Breakpoint"
- Add conditions like:
  ```csharp
  agentName == "test-agent"
  ```

### 5. Logging Debug
- Set logging level to Debug in your environment:
  ```bash
  Logging__LogLevel__Features.WebApi.Services.AgentService=Debug
  ```

## Common Debugging Scenarios

### 1. Authentication Issues
- Set breakpoint in `KeycloakProvider.ConfigureJwtBearer`
- Check if configuration is loaded correctly
- Verify JWT token validation

### 2. Permission Issues
- Set breakpoint in `_permissionsService.HasReadPermission`
- Check if user has proper permissions
- Verify tenant context

### 3. Validation Issues
- Set breakpoint in `ValidationHelpers.SanitizeString`
- Check input sanitization
- Verify validation patterns

### 4. Database Issues
- Set breakpoint in repository methods
- Check MongoDB connection
- Verify data retrieval

## Testing with Postman/curl

### Test GetAgentNames
```bash
curl -X GET "http://localhost:5001/api/client/agents/names" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "X-Tenant-Id: your-tenant-id"
```

### Test GetDefinitions
```bash
curl -X GET "http://localhost:5001/api/client/agents/your-agent/definitions/basic" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "X-Tenant-Id: your-tenant-id"
```

### Test DeleteAgent
```bash
curl -X DELETE "http://localhost:5001/api/client/agents/your-agent" \
  -H "Authorization: Bearer your-jwt-token" \
  -H "X-Tenant-Id: your-tenant-id"
```

## Troubleshooting

### 1. Build Errors
- Run `dotnet build` in terminal
- Check for missing dependencies
- Verify .NET version compatibility

### 2. Runtime Errors
- Check environment variables
- Verify configuration files
- Check MongoDB connection

### 3. Authentication Errors
- Verify Keycloak configuration
- Check JWT token format
- Verify tenant headers

### 4. Breakpoints Not Hit
- Ensure you're running the correct debug configuration
- Check if the code path is being executed
- Verify the application is actually running

## Advanced Debugging

### 1. Remote Debugging
- Configure remote debugging in launch.json
- Attach to running process on remote machine

### 2. Performance Profiling
- Use Visual Studio Profiler
- Monitor memory usage
- Check CPU utilization

### 3. Database Debugging
- Use MongoDB Compass for database inspection
- Check connection strings
- Verify indexes and queries 