using Features.AdminApi.Constants;
using Shared.Repositories;
using Shared.Services;
using Shared.Auth;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for knowledge management.
/// Knowledge is only connected to deployed agents (in the agents collection).
/// </summary>
public static class AdminKnowledgeEndpoints
{
    /// <summary>
    /// Checks if user can modify agent resources (knowledge, tokens, etc.)
    /// </summary>
    private static bool CanModifyAgentResource(ITenantContext tenantContext, Agent agent)
    {
        // System Admin can modify anything
        if (tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            return true;
        
        // Tenant Admin can modify all agents in their tenant
        if (tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            return true;
        
        // Regular users can only modify agents they own
        return agent.OwnerAccess.Contains(tenantContext.LoggedInUser);
    }
    /// <summary>
    /// Request model for creating knowledge.
    /// </summary>
    public class CreateKnowledgeRequest
    {
        [Required(ErrorMessage = "Name is required")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters")]
        public required string Name { get; set; }
        
        [Required(ErrorMessage = "Content is required")]
        public required string Content { get; set; }
        
        [Required(ErrorMessage = "Type is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Type must be between 1 and 50 characters")]
        public required string Type { get; set; }
        
        /// <summary>
        /// Optional version identifier. If not provided, a hash will be generated.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Optional description of the knowledge item.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether the knowledge item is visible. Defaults to true.
        /// </summary>
        public bool Visible { get; set; } = true;
    }

    /// <summary>
    /// Request model for updating knowledge.
    /// </summary>
    public class UpdateKnowledgeRequest
    {
        [Required(ErrorMessage = "Content is required")]
        public required string Content { get; set; }
        
        [Required(ErrorMessage = "Type is required")]
        [StringLength(50, MinimumLength = 1, ErrorMessage = "Type must be between 1 and 50 characters")]
        public required string Type { get; set; }
        
        /// <summary>
        /// Optional version identifier. If not provided, a hash will be generated.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Optional description of the knowledge item.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether the knowledge item is visible.
        /// </summary>
        public bool? Visible { get; set; }
    }

    /// <summary>
    /// Maps all AdminApi knowledge management endpoints.
    /// </summary>
    /// <param name="adminApiGroup">AdminAPI route group</param>
    public static void MapAdminKnowledgeEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminKnowledgeGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/knowledge")
            .WithTags("AdminAPI - Knowledge Management")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List all knowledge for a tenant (filtered by agentName, required)
        adminKnowledgeGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // agentName is now required
                if (string.IsNullOrEmpty(agentName))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = "agentName query parameter is required" },
                        statusCode: 400);
                }

                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                    return Results.Json(
                        new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" },
                        statusCode: 404);
                    }

                    // Check permissions
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this agent's knowledge" },
                            statusCode: 403);
                    }

                // Call GetLatestAll to get the knowledge tree
                var result = await knowledgeService.GetLatestAll(agentName);
                
                // If result is OK and activationName is provided, filter the activations
                if (result is Microsoft.AspNetCore.Http.HttpResults.Ok<GroupedKnowledgeResponse> okResult 
                    && !string.IsNullOrEmpty(activationName))
                {
                    var response = okResult.Value;
                    if (response != null)
                    {
                        // Filter activations in each group to only include the specified activation
                        foreach (var group in response.Groups)
                        {
                            group.Activations = group.Activations
                                .Where(k => k.ActivationName == activationName)
                                .ToList();
                        }
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("List Knowledge")
        .Produces<GroupedKnowledgeResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "List knowledge tree for an agent";
            operation.Description = @"Retrieves all knowledge for a specific agent, grouped by name with system-scoped, tenant-default, and activation-specific versions. 
            
            **Query Parameters:**
            - `agentName` (required): The name of the agent to retrieve knowledge for
            - `activationName` (optional): If provided, filters the activations to only include this specific activation
            
            **Response format:**
            - `groups`: Array of knowledge groups, each containing:
              - `name`: Knowledge item name
              - `system_scoped`: Latest system-scoped knowledge (null if none)
              - `tenant_default`: Latest tenant-scoped knowledge without activation (null if none)
              - `activations`: Array of latest knowledge for each unique activation name (filtered by activationName if provided)";
            return operation;
        });

        // Get specific knowledge by ID
        adminKnowledgeGroup.MapGet("/{knowledgeId}", async (
            string tenantId,
            string knowledgeId,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get the knowledge by ID (without tenant restriction initially)
                var knowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                
                // If not found in tenant scope, try to get it as system-scoped (tenantId = null)
                if (knowledge == null)
                {
                    var systemKnowledge = await knowledgeService.GetById(knowledgeId);
                    
                    // Check if it's an OK result with system-scoped knowledge
                    if (systemKnowledge is Microsoft.AspNetCore.Http.HttpResults.Ok<Knowledge> okResult)
                    {
                        knowledge = okResult.Value;
                        
                        // Verify it's actually system-scoped (tenantId is null)
                        if (knowledge?.TenantId != null)
                        {
                            // It belongs to a different tenant, not accessible
                            return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                        }
                    }
                }
                
                if (knowledge == null)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                }

                // If knowledge is agent-scoped, verify permissions
                if (!string.IsNullOrEmpty(knowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(knowledge.Agent, tenantId);
                    if (agent != null && !CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this knowledge" },
                            statusCode: 403);
                    }
                }

                return Results.Ok(knowledge);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Get Knowledge by ID")
        .Produces<Knowledge>()
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Get specific knowledge by ID";
            operation.Description = "Retrieves a specific knowledge item by its ID. Returns both tenant-scoped and system-scoped (tenantId is null) knowledge.";
            return operation;
        });

        // Override knowledge at different scope levels
        adminKnowledgeGroup.MapPost("/{knowledgeId}/override/{level}", async (
            string tenantId,
            string knowledgeId,
            string level,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Validate level parameter
                level = level.ToLowerInvariant();
                if (level != "tenant" && level != "activation")
                {
                    return Results.Json(
                        new { error = "BadRequest", message = "Level must be either 'tenant' or 'activation'" },
                        statusCode: 400);
                }

                // Get the original knowledge (check both tenant-scoped and system-scoped)
                Knowledge? originalKnowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                
                // If not found in tenant scope, try system-scoped
                if (originalKnowledge == null)
                {
                    var systemKnowledge = await knowledgeService.GetById(knowledgeId);
                    if (systemKnowledge is Microsoft.AspNetCore.Http.HttpResults.Ok<Knowledge> okResult)
                    {
                        originalKnowledge = okResult.Value;
                        // Verify it's actually system-scoped
                        if (originalKnowledge?.TenantId != null)
                        {
                            return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                        }
                    }
                }

                if (originalKnowledge == null)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                }

                // Check permissions for the agent if it's agent-scoped
                if (!string.IsNullOrEmpty(originalKnowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(originalKnowledge.Agent, tenantId);
                    if (agent != null && !CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to override this knowledge" },
                            statusCode: 403);
                    }
                }

                // Validate override rules based on current knowledge scope
                string? newTenantId = null;
                string? newActivationName = null;

                if (originalKnowledge.SystemScoped && originalKnowledge.TenantId == null)
                {
                    // System-scoped knowledge: can override at tenant or activation level
                    if (level == "tenant")
                    {
                        // Tenant override: set tenantId
                        newTenantId = tenantId;
                        newActivationName = null;
                    }
                    else if (level == "activation")
                    {
                        // Activation override: set both tenantId and activationName
                        if (string.IsNullOrEmpty(activationName))
                        {
                    return Results.Json(
                                new { error = "BadRequest", message = "activationName query parameter is required for activation level override" },
                                statusCode: 400);
                        }
                        newTenantId = tenantId;
                        newActivationName = activationName;
                    }
                }
                else if (!string.IsNullOrEmpty(originalKnowledge.TenantId) && string.IsNullOrEmpty(originalKnowledge.ActivationName))
                {
                    // Tenant-level knowledge: can only override at activation level
                    if (level == "tenant")
                    {
                        return Results.Json(
                            new { error = "BadRequest", message = "Cannot create tenant-level override of tenant-level knowledge. Only activation level override is allowed." },
                            statusCode: 400);
                    }
                    else if (level == "activation")
                    {
                        // Activation override: keep tenantId, set activationName
                        if (string.IsNullOrEmpty(activationName))
                        {
                            return Results.Json(
                                new { error = "BadRequest", message = "activationName query parameter is required for activation level override" },
                                statusCode: 400);
                        }
                        newTenantId = originalKnowledge.TenantId;
                        newActivationName = activationName;
                    }
                }
                else
                {
                    // Activation-level knowledge: cannot override
                    return Results.Json(
                        new { error = "BadRequest", message = "Cannot override activation-level knowledge. It is already at the most specific scope." },
                        statusCode: 400);
                }

                // Create the override copy
                var overrideKnowledge = await knowledgeService.CreateForTenantAsync(
                    originalKnowledge.Name,
                    originalKnowledge.Content,
                    originalKnowledge.Type,
                    newTenantId!,
                    tenantContext.LoggedInUser,
                    originalKnowledge.Agent,
                    originalKnowledge.Version,
                    newActivationName,
                    systemScoped: false,
                    description: originalKnowledge.Description,
                    visible: originalKnowledge.Visible
                );

                return Results.Created(
                    AdminApiConstants.BuildVersionedPath($"tenants/{tenantId}/knowledge/{overrideKnowledge.Id}"),
                    overrideKnowledge);
            }
            catch (Exception ex)
                    {
                        return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Override Knowledge")
        .Produces<Knowledge>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create a scoped override of existing knowledge";
            operation.Description = @"Creates a copy of existing knowledge at a more specific scope level. This allows you to customize system-wide knowledge for a specific tenant or activation without modifying the original.

## Parameters

### Path Parameters
- **tenantId** (string, required): The tenant context for the override
- **knowledgeId** (string, required): ID of the knowledge to override
- **level** (string, required): Target scope level - must be either `tenant` or `activation`

### Query Parameters
- **activationName** (string, conditional): Required when `level=activation`. Specifies the activation name for the override.

## Override Rules & Flow

### Scope Hierarchy
```
System-Scoped (most general)
    ↓ override at 'tenant' level
Tenant-Scoped
    ↓ override at 'activation' level
Activation-Scoped (most specific)
```

### 1. Overriding System-Scoped Knowledge
**Original**: `{ systemScoped: true, tenantId: null, activationName: null }`

#### Option A: Tenant-Level Override
```http
POST /api/v1/admin/tenants/default/knowledge/{knowledgeId}/override/tenant
```
**Result**: `{ systemScoped: false, tenantId: ""default"", activationName: null }`
- Creates a tenant-specific copy
- Only affects the specified tenant
- Original system knowledge remains unchanged

#### Option B: Activation-Level Override
```http
POST /api/v1/admin/tenants/default/knowledge/{knowledgeId}/override/activation?activationName=prod
```
**Result**: `{ systemScoped: false, tenantId: ""default"", activationName: ""prod"" }`
- Creates an activation-specific copy for the tenant
- Only affects the specified tenant and activation
- Original system knowledge remains unchanged

### 2. Overriding Tenant-Scoped Knowledge
**Original**: `{ systemScoped: false, tenantId: ""default"", activationName: null }`

#### Only Option: Activation-Level Override
```http
POST /api/v1/admin/tenants/default/knowledge/{knowledgeId}/override/activation?activationName=staging
```
**Result**: `{ systemScoped: false, tenantId: ""default"", activationName: ""staging"" }`
- Creates an activation-specific copy within the same tenant
- Only affects the specified activation
- Original tenant knowledge remains unchanged

**❌ Not Allowed**: `level=tenant` - Cannot create tenant override of tenant-scoped knowledge

### 3. Activation-Scoped Knowledge
**Original**: `{ systemScoped: false, tenantId: ""default"", activationName: ""prod"" }`

**❌ Cannot Override**: Already at the most specific scope level. Returns 400 Bad Request.

## Use Cases

### Example 1: Tenant-Specific Customization
You have a global knowledge item used across all tenants, but Tenant A needs a custom version:
```http
POST /api/v1/admin/tenants/tenant-a/knowledge/global-123/override/tenant
```
- Tenant A gets the customized version
- All other tenants continue using the global version

### Example 2: Environment-Specific Configuration
You have tenant-level knowledge, but need different values for production:
```http
POST /api/v1/admin/tenants/default/knowledge/config-456/override/activation?activationName=prod
```
- Production activation gets the custom version
- Dev/Staging activations use the tenant-level version

## Response

### Success (201 Created)
Returns the newly created override knowledge object with:
- Same `name`, `content`, `type`, and `version` as original
- Updated `tenantId` and/or `activationName` based on override level
- New unique `id`
- `createdBy` set to current user
- New `createdAt` timestamp

### Error Responses
- **400 Bad Request**: 
  - Invalid level parameter (must be 'tenant' or 'activation')
  - Missing activationName when required
  - Attempting invalid override (e.g., tenant override of tenant knowledge)
  - Attempting to override activation-scoped knowledge
- **403 Forbidden**: Insufficient permissions to override this knowledge
- **404 Not Found**: Original knowledge not found
- **500 Internal Server Error**: Unexpected server error

## Permissions
Same as the original knowledge item. User must have permission to modify the agent associated with the knowledge.

## Important Notes
- ✅ Override creates a **copy** - original knowledge is never modified
- ✅ More specific overrides take precedence in knowledge resolution
- ✅ Override preserves the `version` hash from the original
- ✅ Can override multiple times at different levels
- ❌ Cannot override ""upward"" (e.g., activation → tenant)";
            return operation;
        });

        // Create knowledge
        adminKnowledgeGroup.MapPost("", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromBody] CreateKnowledgeRequest request,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext,
            [FromQuery] bool systemScoped = false,
            [FromQuery] string? activationName = null) =>
        {
            try
            {
                // agentName is now required
                if (string.IsNullOrEmpty(agentName))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = "agentName query parameter is required" },
                        statusCode: 400);
                }

                // Verify agent exists and check permissions
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                    return Results.Json(
                        new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" },
                        statusCode: 404);
                }

                // Check permissions for creating knowledge
                if (systemScoped)
                {
                    // Only SysAdmin can create system-scoped knowledge
                    if (!tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Only system administrators can create system-scoped knowledge" },
                            statusCode: 403);
                    }
                }
                else
                {
                    // For tenant-scoped, check agent permissions
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                            statusCode: 403);
                    }
                }

                // Determine the actual tenantId to use
                string? actualTenantId = systemScoped ? null : tenantId;

                // Use service to create knowledge
                var knowledge = await knowledgeService.CreateForTenantAsync(
                    request.Name,
                    request.Content,
                    request.Type,
                    actualTenantId,
                    tenantContext.LoggedInUser,
                    agentName,
                    request.Version,
                    activationName,
                    systemScoped,
                    request.Description,
                    request.Visible
                );

                return Results.Created(
                    AdminApiConstants.BuildVersionedPath($"tenants/{tenantId}/knowledge/{knowledge.Id}"),
                    knowledge);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Create Knowledge")
        .Produces<Knowledge>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create a new knowledge item (tenant-scoped or system-scoped)";
            operation.Description = @"Creates a new knowledge item with flexible scoping options. Knowledge can be scoped at three levels: system, tenant, or activation.

## Parameters

### Path Parameters
- **tenantId** (string, required): The tenant ID in the URL path. Used as context for tenant-scoped knowledge.

### Query Parameters
- **agentName** (string, **REQUIRED**): The agent this knowledge belongs to. Must be a valid agent name in the specified tenant.
- **systemScoped** (boolean, optional, default: `false`): 
  - When `true`: Creates system-scoped knowledge (tenantId will be null, accessible by all tenants)
  - When `false`: Creates tenant-scoped knowledge (uses tenantId from path)
  - **⚠️ Only SysAdmin can create system-scoped knowledge**
- **activationName** (string, optional): Activation name for activation-scoped knowledge. When provided, creates knowledge specific to this activation.

### Request Body
```json
{
  ""name"": ""string (required, 1-200 characters)"",
  ""content"": ""string (required)"",
  ""type"": ""string (required, 1-50 characters)"",
  ""version"": ""string (optional, auto-generated if not provided)""
}
```

## Knowledge Scoping Levels

### 1. System-Scoped Knowledge
- **How to create**: Set `systemScoped=true`
- **Properties**: `{ systemScoped: true, tenantId: null, activationName: null }`
- **Access**: Available to all tenants
- **Permissions**: Only `SysAdmin` can create
- **Example**: Global configurations, shared templates

### 2. Tenant-Scoped Knowledge
- **How to create**: Set `systemScoped=false` (default), don't provide `activationName`
- **Properties**: `{ systemScoped: false, tenantId: ""default"", activationName: null }`
- **Access**: Available only to the specified tenant
- **Permissions**: Agent owner, `TenantAdmin`, or `SysAdmin`
- **Example**: Tenant-specific configurations

### 3. Activation-Scoped Knowledge
- **How to create**: Set `systemScoped=false` (default), provide `activationName`
- **Properties**: `{ systemScoped: false, tenantId: ""default"", activationName: ""prod"" }`
- **Access**: Available only to the specified tenant and activation
- **Permissions**: Agent owner, `TenantAdmin`, or `SysAdmin`
- **Example**: Environment-specific settings (dev, staging, prod)

## Examples

### Create System-Scoped Knowledge
```http
POST /api/v1/admin/tenants/default/knowledge?agentName=MyAgent&systemScoped=true
{
  ""name"": ""GlobalConfig"",
  ""content"": ""Global configuration content"",
  ""type"": ""markdown""
}
```
**Result**: Knowledge accessible by all tenants (requires SysAdmin role)

### Create Tenant-Scoped Knowledge
```http
POST /api/v1/admin/tenants/default/knowledge?agentName=MyAgent
{
  ""name"": ""TenantConfig"",
  ""content"": ""Tenant-specific configuration"",
  ""type"": ""markdown""
}
```
**Result**: Knowledge accessible only by 'default' tenant

### Create Activation-Scoped Knowledge
```http
POST /api/v1/admin/tenants/default/knowledge?agentName=MyAgent&activationName=prod
{
  ""name"": ""ProdConfig"",
  ""content"": ""Production environment configuration"",
  ""type"": ""markdown""
}
```
**Result**: Knowledge accessible only by 'default' tenant in 'prod' activation

## Response

### Success (201 Created)
Returns the created knowledge object with all properties including auto-generated version hash if not provided.

### Error Responses
- **400 Bad Request**: Missing required `agentName` parameter or invalid request body
- **403 Forbidden**: Insufficient permissions (e.g., non-SysAdmin trying to create system-scoped knowledge)
- **404 Not Found**: Specified agent not found in tenant
- **500 Internal Server Error**: Unexpected server error

## Version Handling
- If `version` is not provided in the request body, it will be auto-generated as a hash of `content + type`
- If knowledge with the same version hash already exists for the same name/agent/tenant/activation combination, the existing knowledge may be returned instead of creating a duplicate

## Permissions Summary
| Knowledge Type | Required Role |
|---------------|---------------|
| System-scoped | SysAdmin |
| Tenant-scoped | Agent Owner, TenantAdmin, or SysAdmin |
| Activation-scoped | Agent Owner, TenantAdmin, or SysAdmin |";
            return operation;
        });

        // Update knowledge
        adminKnowledgeGroup.MapPatch("/{knowledgeId}", async (
            string tenantId,
            string knowledgeId,
            [FromBody] UpdateKnowledgeRequest request,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get existing knowledge using service layer
                var existingKnowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                if (existingKnowledge == null)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge not found" },
                        statusCode: 404);
                }

                // If knowledge is agent-scoped, check permissions
                if (!string.IsNullOrEmpty(existingKnowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(existingKnowledge.Agent, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(
                            new { error = "NotFound", message = $"Agent '{existingKnowledge.Agent}' not found" },
                            statusCode: 404);
                    }

                    // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this knowledge" },
                            statusCode: 403);
                    }
                }
                else
                {
                    // Tenant-level knowledge requires TenantAdmin or SysAdmin
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify tenant-level knowledge" },
                            statusCode: 403);
                    }
                }

                // Always calculate the version hash from the actual content to ensure integrity
                var calculatedVersion = global::Shared.Utils.HashGenerator.GenerateContentHash(request.Content + request.Type);
                
                // Use the calculated version, ignoring any provided version that doesn't match
                var newVersion = calculatedVersion;

                // Check if this exact version already exists for this knowledge name
                var versions = await knowledgeService.GetVersionsForTenantAsync(
                    existingKnowledge.Name, 
                    tenantId, 
                    existingKnowledge.Agent);
                
                var existingVersion = versions.FirstOrDefault(v => 
                    v.Version == newVersion && 
                    v.ActivationName == existingKnowledge.ActivationName);

                // If the exact same version already exists, return it instead of creating a duplicate
                if (existingVersion != null)
                {
                    return Results.Ok(existingVersion);
                }

                // Update knowledge using service layer (creates new version)
                // Pass null for version to let the service calculate it from content
                var updatedKnowledge = await knowledgeService.UpdateForTenantAsync(
                    knowledgeId,
                    request.Content,
                    request.Type,
                    tenantId,
                    tenantContext.LoggedInUser,
                    version: null,  // Always let service calculate version from content
                    description: request.Description,
                    visible: request.Visible
                );
                
                return Results.Ok(updatedKnowledge);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = "BadRequest", message = ex.Message },
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Update Knowledge")
        .Produces<Knowledge>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Update knowledge (creates new version if needed)";
            operation.Description = @"Updates knowledge by creating a new version if the content has changed. This maintains version history and the old version remains in the database.
            
            **Behavior:**
            - Version hash is **always auto-generated** from content + type (any provided version field is ignored)
            - If a knowledge version with the same content hash already exists, returns the existing version instead of creating a duplicate
            - If the content is different, creates a new version with the new content hash
            - This ensures content integrity and prevents version mismatches";
            return operation;
        });

        // Delete knowledge by ID
        adminKnowledgeGroup.MapDelete("/{knowledgeId}", async (
            string tenantId,
            string knowledgeId,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get the knowledge to verify tenant and permissions
                var knowledge = await knowledgeService.GetByIdForTenantAsync(knowledgeId, tenantId);
                if (knowledge == null)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found" }, statusCode: 404);
                }

                // If knowledge is agent-scoped, check permissions
                if (!string.IsNullOrEmpty(knowledge.Agent))
                {
                    var agent = await agentRepository.GetByNameInternalAsync(knowledge.Agent, tenantId);
                    if (agent != null && !CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to delete this knowledge" },
                            statusCode: 403);
                    }
                }
                else
                {
                    // Tenant-level knowledge requires TenantAdmin or SysAdmin
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to delete tenant-level knowledge" },
                            statusCode: 403);
                    }
                }

                // Use service to delete knowledge
                var result = await knowledgeService.DeleteByIdForTenantAsync(knowledgeId, tenantId);
                if (!result)
                {
                    return Results.Json(new { error = "NotFound", message = "Knowledge not found or could not be deleted" }, statusCode: 404);
                }
                
                return Results.Ok(new { message = "Knowledge deleted successfully" });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Delete Knowledge")
        .WithOpenApi(operation => {
            operation.Summary = "Delete knowledge by ID";
            operation.Description = "Deletes a specific knowledge item by its ID.";
            return operation;
        });

        // Get versions of knowledge by name
        adminKnowledgeGroup.MapGet("/{name}/versions", async (
            string tenantId,
            string name,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                string? resolvedAgentName = null;

                // If agentName is provided, use it
                if (!string.IsNullOrEmpty(agentName))
                {
                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                        return Results.Json(new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" }, statusCode: 404);
                    }

                    // Check permissions
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to view this agent's knowledge" },
                            statusCode: 403);
                    }

                    resolvedAgentName = agentName;
                }
                // If activationName is provided, resolve to agent name
                else if (!string.IsNullOrEmpty(activationName))
                {
                    // TODO: Implement activation name resolution logic
                    return Results.Json(
                        new { error = "NotImplemented", message = "Filtering by activationName is not yet implemented" },
                        statusCode: 501);
                }
                // If neither is provided, require TenantAdmin or SysAdmin
                else
                {
                    if (!tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) && 
                        !tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: must specify agentName or have TenantAdmin/SysAdmin role" },
                            statusCode: 403);
                    }
                }

                // Use service to get versions
                var versions = await knowledgeService.GetVersionsForTenantAsync(name, tenantId, resolvedAgentName);
                return Results.Ok(new { tenantId, name, agentName, activationName, versions });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Fetch Knowledge Versions")
        .WithOpenApi(operation => {
            operation.Summary = "Get all versions of knowledge by name";
            operation.Description = "Retrieves all versions of a knowledge item with a specific name. Can be filtered by agentName or activationName query parameters.";
            return operation;
        });

        // Delete all versions of knowledge by name and level
        adminKnowledgeGroup.MapDelete("/{name}/{level}/versions", async (
            string tenantId,
            string name,
            string level,
            [FromQuery] string agentName,
            [FromServices] IKnowledgeService knowledgeService,
            [FromServices] IAgentRepository agentRepository,
            [FromServices] ITenantContext tenantContext,
            [FromQuery] string? activationName = null) =>
        {
            try
            {
                // Validate level parameter
                level = level.ToLowerInvariant();
                if (level != "tenant" && level != "activation")
                {
                    return Results.Json(
                        new { error = "BadRequest", message = "Level must be either 'tenant' or 'activation'" },
                        statusCode: 400);
                }

                // agentName is required
                if (string.IsNullOrEmpty(agentName))
                {
                    return Results.Json(
                        new { error = "BadRequest", message = "agentName query parameter is required" },
                        statusCode: 400);
                }

                    // Verify agent exists and belongs to tenant
                    var agent = await agentRepository.GetByNameInternalAsync(agentName, tenantId);
                    if (agent == null)
                    {
                    return Results.Json(
                        new { error = "NotFound", message = $"Agent '{agentName}' not found in tenant '{tenantId}'" },
                        statusCode: 404);
                    }

                    // Check permissions (must be owner, TenantAdmin, or SysAdmin)
                    if (!CanModifyAgentResource(tenantContext, agent))
                    {
                        return Results.Json(
                            new { error = "Forbidden", message = "Access denied: insufficient permissions to modify this agent's knowledge" },
                            statusCode: 403);
                    }

                // Validate activationName based on level
                if (level == "activation")
                {
                    if (string.IsNullOrEmpty(activationName))
                    {
                    return Results.Json(
                            new { error = "BadRequest", message = "activationName query parameter is required when level is 'activation'" },
                            statusCode: 400);
                    }
                }
                else if (level == "tenant")
                {
                    // For tenant level, we explicitly ignore any provided activationName
                    // and delete all knowledge at tenant level (activationName = null)
                    activationName = null;
                }

                // Delete all versions for the specified scope
                // We need to delete all knowledge items that match:
                // - name
                // - tenantId
                // - agentName
                // - activationName (null for tenant level, specific value for activation level)
                
                // Get all versions first to check if any exist and to delete them properly
                var versions = await knowledgeService.GetVersionsForTenantAsync(name, tenantId, agentName);
                
                // Filter by activationName based on level
                var versionsToDelete = level == "tenant" 
                    ? versions.Where(v => string.IsNullOrEmpty(v.ActivationName)).ToList()
                    : versions.Where(v => v.ActivationName == activationName).ToList();

                if (versionsToDelete.Count == 0)
                    {
                        return Results.Json(
                        new { error = "NotFound", message = $"No knowledge versions found for name '{name}' at {level} level" },
                        statusCode: 404);
                }

                // Delete each version
                int deletedCount = 0;
                foreach (var version in versionsToDelete)
                {
                    var deleted = await knowledgeService.DeleteByIdForTenantAsync(version.Id, tenantId);
                    if (deleted)
                    {
                        deletedCount++;
                    }
                }

                if (deletedCount == 0)
                {
                    return Results.Json(
                        new { error = "NotFound", message = "Knowledge versions could not be deleted" },
                        statusCode: 404);
                }
                
                return Results.Ok(new { 
                    message = $"Successfully deleted {deletedCount} version(s) at {level} level",
                    deletedCount,
                    level,
                    name,
                    agentName,
                    activationName
                });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { error = "Internal server error", message = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("Delete All Knowledge Versions by Level")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Delete all versions of knowledge by name and scope level";
            operation.Description = @"Deletes all versions of a knowledge item with a specific name at the specified scope level.

## Parameters

### Path Parameters
- **tenantId** (string, required): The tenant ID
- **name** (string, required): The knowledge name
- **level** (string, required): Scope level - must be either `tenant` or `activation`

### Query Parameters
- **agentName** (string, **REQUIRED**): The agent name
- **activationName** (string, conditional): Required when `level=activation`, ignored when `level=tenant`

## Deletion Behavior

### Tenant Level (`level=tenant`)
```http
DELETE /api/v1/admin/tenants/default/knowledge/MyKnowledge/tenant/versions?agentName=MyAgent
```
**Deletes**: All knowledge versions where:
- `name` = ""MyKnowledge""
- `tenantId` = ""default""
- `agent` = ""MyAgent""
- `activationName` = null (tenant-scoped only, not activation-specific)

**Use Case**: Remove all tenant-level versions while preserving activation-specific versions

### Activation Level (`level=activation`)
```http
DELETE /api/v1/admin/tenants/default/knowledge/MyKnowledge/activation/versions?agentName=MyAgent&activationName=prod
```
**Deletes**: All knowledge versions where:
- `name` = ""MyKnowledge""
- `tenantId` = ""default""
- `agent` = ""MyAgent""
- `activationName` = ""prod"" (specific activation only)

**Use Case**: Remove all versions for a specific activation while preserving tenant-level and other activation versions

## Examples

### Example 1: Delete all tenant-level versions
You have knowledge with multiple tenant-level versions and some activation-specific versions:
```
- MyKnowledge v1 (tenant: default, activation: null)
- MyKnowledge v2 (tenant: default, activation: null)
- MyKnowledge v1 (tenant: default, activation: prod)
- MyKnowledge v1 (tenant: default, activation: dev)
```

Request:
```http
DELETE /api/v1/admin/tenants/default/knowledge/MyKnowledge/tenant/versions?agentName=MyAgent
```

Result: Deletes v1 and v2 (tenant-level), keeps prod and dev activation versions

### Example 2: Delete all activation-level versions
Request:
```http
DELETE /api/v1/admin/tenants/default/knowledge/MyKnowledge/activation/versions?agentName=MyAgent&activationName=prod
```

Result: Deletes only the prod activation version, keeps tenant-level and other activation versions

## Response

### Success (200 OK)
```json
{
  ""message"": ""Successfully deleted 2 version(s) at tenant level"",
  ""deletedCount"": 2,
  ""level"": ""tenant"",
  ""name"": ""MyKnowledge"",
  ""agentName"": ""MyAgent"",
  ""activationName"": null
}
```

### Error Responses
- **400 Bad Request**: Invalid level, missing required parameters
- **403 Forbidden**: Insufficient permissions
- **404 Not Found**: No knowledge versions found matching the criteria
- **500 Internal Server Error**: Unexpected server error

## Important Notes
- ✅ Deletes **all versions** at the specified level (not just the latest)
- ✅ Other scope levels remain unaffected
- ✅ System-scoped knowledge is never deleted by this endpoint (only tenant/activation)
- ⚠️ This operation cannot be undone
- ✅ Returns count of deleted versions for confirmation";
            return operation;
        });
    }
}


