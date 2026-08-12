# ADR-0002: Minimal API Pattern

**Status:** Proposed

**Date:** 2026-07-16

## Context

ASP.NET Core offers two primary approaches for defining HTTP endpoints:

1. **MVC Controllers**: Class-based controllers with action methods decorated with routing attributes
2. **Minimal APIs**: Functional endpoint definitions using `Map*` methods

The project needed a consistent approach that:
- Reduces boilerplate and ceremony
- Provides clear, discoverable endpoint definitions
- Aligns with modern .NET best practices
- Supports flexible composition (grouping, filters, policies)

## Decision

We adopt **ASP.NET Core Minimal APIs** for all HTTP endpoint definitions. Endpoints are defined as static extension methods on `RouteGroupBuilder` or `WebApplication` within dedicated `*Endpoints.cs` files in each feature's `Endpoints/` directory.

Pattern:
```csharp
public static class AdminStatsEndpoints
{
    public static void MapAdminStatsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/stats", async (string tenantId, ...) => { ... })
            .Produces<StatsResponse>(200)
            .WithName("GetAdminStats");
    }
}
```

MVC controllers (`[ApiController]`, `ControllerBase`) are not permitted.

## Consequences

**Positive:**
- Less boilerplate: no need for controller classes, base classes, or attribute routing
- Better composition: groups, filters, and policies are applied programmatically
- Improved readability: routes, handlers, and metadata are co-located
- Modern .NET alignment: Microsoft recommends Minimal APIs for new projects
- Easier testing: handlers are simple delegates or local functions

**Negative:**
- Less familiar to developers coming from ASP.NET MVC or Web API backgrounds
- Complex handlers can become inline and harder to extract
- No built-in model binding validation attributes (must use validators or filters)

**Mitigations:**
- Complex handlers delegate to service classes
- Use endpoint filters for cross-cutting validation
- Maintain clear naming conventions for endpoint files
