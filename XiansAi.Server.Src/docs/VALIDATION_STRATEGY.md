# Validation and Sanitization Strategy

## Overview

This document outlines the comprehensive validation and sanitization strategy implemented to prevent vulnerabilities in repository functions and ensure data integrity across the application.

## Architecture

### 1. Multi-Layer Validation Approach

The validation strategy implements a multi-layer approach:

1. **Model-Level Validation**: Data Annotations + Custom Validation
2. **Repository-Level Enforcement**: Automatic validation before persistence
3. **Input Sanitization**: Prevention of injection attacks
4. **Consistent Patterns**: Reusable validation helpers

### 2. Core Components

#### IModelValidator Interface
```csharp
public interface IModelValidator
{
    void Validate();           // Validates model data
    void Sanitize();          // Sanitizes model data
    void ValidateAndSanitize(); // Combines both operations
}
```

#### ModelValidatorBase Class
Provides common validation logic using Data Annotations and custom validation rules.

#### ValidationHelpers Class
Contains reusable validation and sanitization utilities:
- Regex patterns for safe input validation
- String sanitization methods
- Date validation helpers
- List validation utilities

## Implementation Examples

### Model Implementation

```csharp
public class Agent : ModelValidatorBase
{
    [Required(ErrorMessage = "Agent ID is required")]
    [StringLength(50, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$")]
    public required string Id { get; set; }

    public override void Sanitize()
    {
        Id = ValidationHelpers.SanitizeString(Id);
        Name = ValidationHelpers.SanitizeString(Name);
        // ... sanitize other properties
    }

    public override void Validate()
    {
        base.Validate(); // Data Annotations
        
        // Custom validation
        if (!ValidationHelpers.IsValidPattern(Id, ValidationHelpers.Patterns.SafeId))
            throw new ValidationException("Invalid Agent ID format");
    }
}
```

### Repository Implementation

```csharp
public async Task CreateAsync(Agent agent)
{
    // Validate and sanitize before persistence
    agent.ValidateAndSanitize();
    
    agent.CreatedAt = DateTime.UtcNow;
    await _agents.InsertOneAsync(agent);
}
```

## Security Features

### 1. Input Sanitization
- **Control Character Removal**: Removes null bytes and control characters
- **Whitespace Normalization**: Prevents whitespace-based attacks
- **Pattern Validation**: Ensures input matches safe patterns

### 2. Validation Rules
- **Required Fields**: Ensures all required data is present
- **Length Limits**: Prevents buffer overflow attacks
- **Pattern Matching**: Validates against safe character sets
- **Date Validation**: Ensures dates are reasonable and logical

### 3. Vulnerability Prevention
- **SQL Injection**: Input sanitization and parameterized queries
- **NoSQL Injection**: Pattern validation and sanitization
- **XSS Prevention**: Control character removal and pattern validation
- **Buffer Overflow**: Length validation on all inputs

## Validation Patterns

### Safe Character Patterns
```csharp
public static class Patterns
{
    public static readonly Regex SafeId = new(@"^[a-zA-Z0-9._@-]{1,50}$");
    public static readonly Regex SafeName = new(@"^[a-zA-Z0-9\s._@-]{1,100}$");
    public static readonly Regex SafeUrl = new(@"^https?://[^\s/$.?#].[^\s]*$");
    public static readonly Regex SafeEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    public static readonly Regex SafeTenantId = new(@"^[a-zA-Z0-9._-]{1,50}$");
    public static readonly Regex SafeWorkflowType = new(@"^[a-zA-Z0-9._-]{1,100}$");
}
```

### Validation Rules by Field Type
- **IDs**: Alphanumeric, dots, underscores, hyphens, at signs (1-50 chars)
- **Names**: Alphanumeric, spaces, dots, underscores, hyphens, at signs (1-100 chars)
- **URLs**: HTTP/HTTPS URLs only
- **Emails**: Basic email format validation
- **Tenant IDs**: Alphanumeric, dots, underscores, hyphens (1-50 chars)
- **Workflow Types**: Alphanumeric, dots, underscores, hyphens (1-100 chars)

## Best Practices

### 1. Always Validate at Repository Level
```csharp
// ✅ Good
public async Task CreateAsync(Agent agent)
{
    agent.ValidateAndSanitize();
    await _agents.InsertOneAsync(agent);
}

// ❌ Bad
public async Task CreateAsync(Agent agent)
{
    await _agents.InsertOneAsync(agent); // No validation
}
```

### 2. Use Consistent Validation Patterns
```csharp
// ✅ Good
if (!ValidationHelpers.IsValidPattern(input, ValidationHelpers.Patterns.SafeId))
    throw new ValidationException("Invalid format");

// ❌ Bad
if (!Regex.IsMatch(input, @"^[a-zA-Z0-9]+$"))
    throw new ValidationException("Invalid format");
```

### 3. Sanitize All Inputs
```csharp
// ✅ Good
var sanitizedInput = ValidationHelpers.SanitizeString(rawInput);

// ❌ Bad
var input = rawInput; // No sanitization
```

### 4. Validate Collections
```csharp
// ✅ Good
if (!ValidationHelpers.IsValidList(items, item => item != null))
    throw new ValidationException("Invalid items in list");

// ❌ Bad
if (items == null) // Incomplete validation
    throw new ValidationException("Items cannot be null");
```

## Error Handling

### Validation Exceptions
- Use `ValidationException` for validation failures
- Include descriptive error messages
- Log validation failures for monitoring

### Repository Error Handling
```csharp
public async Task<bool> UpdateAsync(string id, Agent agent, string userId, string[] userRoles)
{
    try
    {
        agent.ValidateAndSanitize();
        // ... repository logic
    }
    catch (ValidationException ex)
    {
        _logger.LogWarning("Validation failed for agent {AgentId}: {Error}", id, ex.Message);
        return false;
    }
}
```

## Testing Strategy

### Unit Tests
- Test all validation patterns
- Test sanitization methods
- Test edge cases and boundary conditions

### Integration Tests
- Test repository validation enforcement
- Test end-to-end validation flow
- Test error handling scenarios

## Migration Guide

### For Existing Models
1. Implement `IModelValidator` interface
2. Add Data Annotations to properties
3. Implement `Sanitize()` and `Validate()` methods
4. Update repository methods to call validation

### For New Models
1. Inherit from `ModelValidatorBase`
2. Add appropriate Data Annotations
3. Override `Sanitize()` and `Validate()` if needed
4. Ensure repository methods validate before persistence

## Monitoring and Logging

### Validation Metrics
- Track validation failure rates
- Monitor sanitization effectiveness
- Alert on unusual validation patterns

### Logging Strategy
```csharp
_logger.LogWarning("Validation failed for {ModelType} {Id}: {Error}", 
    typeof(T).Name, model.Id, ex.Message);
```

## Future Enhancements

### Planned Improvements
1. **Custom Validation Attributes**: Domain-specific validation rules
2. **Validation Caching**: Cache validation results for performance
3. **Async Validation**: Support for async validation operations
4. **Validation Profiles**: Different validation rules for different contexts

### Security Enhancements
1. **Rate Limiting**: Prevent validation abuse
2. **Input Size Limits**: Prevent DoS attacks
3. **Validation Timeouts**: Prevent hanging validation
4. **Audit Logging**: Track all validation operations 