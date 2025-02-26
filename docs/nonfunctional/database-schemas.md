# JSON Schema Design Guidelines

## File Structure

- Schema files should be named in kebab-case: `<entity>-schema.json`
- Schema files should be located in the `MongoDB/Schemas` directory

## Schema Structure

### Required Base Properties

Every entity schema should include these base properties:

```json
{
    "tenant_id": {
        "bsonType": "string",
        "description": "Tenant ID"
    },
    "created_at": {
        "bsonType": "date",
        "description": "Timestamp of creation"
    }
}
```

### Common Ownership Properties

When the entity needs ownership tracking:

```json
{
    "owner": {
        "bsonType": "string",
        "description": "Owner of the entity"
    },
    "permissions": {
        "bsonType": ["array", "null"],
        "items": {
            "bsonType": "object",
            "required": ["level", "owner"],
            "properties": {
                "level": {
                    "bsonType": ["string", "null"],
                    "description": "Permission level"
                },
                "owner": {
                    "bsonType": ["string", "null"],
                    "description": "Owner of the permission"
                }
            }
        }
    }
}
```

## Naming Conventions

### Property Names

- Use snake_case for all property names
- Use descriptive, full words (avoid abbreviations)
- Suffix timestamp fields with `_at` (e.g., `created_at`, `updated_at`)
- Suffix ID fields with `_id` (e.g., `tenant_id`, `project_id`)
- Use plural form for array fields (e.g., `activities`, `parameters`)

### Data Types

Use the following BSON types:

- `string`: For text and identifiers
- `date`: For timestamps
- `object`: For nested documents
- `array`: For lists
- Use `["type", "null"]` for optional fields (e.g., `["string", "null"]`)

## Schema Template

```json
{
    "$jsonSchema": {
        "bsonType": "object",
        "required": ["tenant_id", "created_at"],
        "properties": {
            "tenant_id": {
                "bsonType": "string",
                "description": "Tenant ID"
            },
            "created_at": {
                "bsonType": "date",
                "description": "Timestamp of creation"
            }
        }
    }
}
```

## Best Practices

### 1. Required Fields

- Always include `tenant_id` and `created_at` in required fields
- List all non-optional fields in the `required` array
- Consider including `owner` for entities that need ownership tracking

### 2. Descriptions

- Provide clear, concise descriptions for all properties
- Include any constraints or special formatting requirements
- Document the purpose and usage of each field

### 3. Validation

- Use `pattern` for string format validation
- Use `uniqueItems` for arrays that should not contain duplicates
- Use `enum` for fields with a fixed set of possible values
- Consider adding min/max constraints where appropriate

### 4. Nested Objects

- Define clear structure for nested objects
- Include required fields for nested objects
- Keep nesting depth reasonable (prefer flat structures where possible)
- Document relationships between nested objects

### 5. Arrays

- Always specify the `items` schema for arrays
- Define clear structure for array items
- Consider using `minItems` and `maxItems` when appropriate
- Use consistent item structure within arrays

## Common Patterns

### 1. Versioning Pattern

For versioned entities:

```json
{
    "version": {
        "bsonType": "string",
        "description": "SHA-256 hash of the content"
    },
    "hash": {
        "bsonType": "string",
        "description": "SHA-256 hash of the definition content"
    }
}
```

### 2. Configuration Pattern

For configurable entities:

```json
{
    "config": {
        "bsonType": "array",
        "description": "Array of configuration entries",
        "items": {
            "bsonType": "object",
            "required": ["group", "key", "value"],
            "properties": {
                "group": {
                    "bsonType": "string",
                    "description": "Configuration group name"
                },
                "key": {
                    "bsonType": "string",
                    "description": "Configuration key"
                },
                "value": {
                    "description": "Configuration value - can be any type"
                }
            }
        }
    }
}
```

### 3. Timing Pattern

For entities with timing requirements:

```json
{
    "started_time": {
        "bsonType": "date",
        "description": "Time when activity started"
    },
    "ended_time": {
        "bsonType": ["date", "null"],
        "description": "Time when activity ended"
    }
}
```

## Indexing Guidelines

### 1. Required Indexes

- Create unique indexes for primary identifiers
- Index frequently queried fields
- Index foreign key references
- Consider compound indexes for common query patterns

### 2. Index Types

```javascript
// Unique Index
{
    "field_name": 1,
    unique: true
}

// Compound Index
{
    "field1": 1,
    "field2": -1
}

// Sparse Index (for optional fields)
{
    "field_name": 1,
    sparse: true
}
```

### 3. Common Index Patterns

- Create ascending indexes on timestamp fields
- Create compound indexes for related fields
- Create unique indexes on business keys
- Index array fields when querying array elements

## Schema Validation

When implementing the schema in MongoDB:

```javascript
await db.createCollection("collection_name", {
    validator: {
        $jsonSchema: {
            // schema definition
        }
    }
});
```

## Error Handling

- Document should fail validation if required fields are missing
- Document should fail validation if field types don't match
- Consider adding custom error messages for validation failures
- Use appropriate error codes for different validation scenarios

```json
{
    "tenant_id": {
        "bsonType": "string",
        "description": "Tenant ID"
    },
    "created_at": {
        "bsonType": "date",
        "description": "Timestamp of creation"
    }
}
```
