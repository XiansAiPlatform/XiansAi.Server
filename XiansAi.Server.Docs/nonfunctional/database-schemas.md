# JSON Schema Design Guidelines

## File Structure

- Schema files should be named in kebab-case: `<entity>-schema.json`
- Schema files should be located in the `XiansAi.Server.Src/Shared/Data/Schemas` directory

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
    },
    "created_by": {
        "bsonType": "string",
        "description": "User ID of the creator"
    },
    "updated_at": {
        "bsonType": "date",
        "description": "Timestamp of last update"
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
        "required": ["tenant_id", "created_at", "created_by"],
        "properties": {
            "tenant_id": {
                "bsonType": "string",
                "description": "Tenant ID"
            },
            "created_at": {
                "bsonType": "date",
                "description": "Timestamp of creation"
            },
            "updated_at": {
                "bsonType": "date",
                "description": "Timestamp of last update"
            },
            "created_by": {
                "bsonType": "string",
                "description": "User ID of the creator"
            }
        }
    }
}
```

## Best Practices

### 1. Required Fields

- Always include `tenant_id` and `created_at` in required fields
- List all non-optional fields in the `required` array

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

## Error Handling

- Document should fail validation if required fields are missing
- Document should fail validation if field types don't match
- Consider adding custom error messages for validation failures
- Use appropriate error codes for different validation scenarios

## Conversation API Schemas

The following schemas are used for the Conversation API feature:

### conversation-thread-schema.json

Represents a conversation thread between agents and participants:

- **Primary Properties**: tenant_id, workflow_id, participant_id, status
- **Indexes**: 
  - Composite key on (tenant_id, workflow_id, participant_id) for uniqueness
  - Status lookup by tenant_id and status
  - Updated_at index for time-based queries

### conversation-message-schema.json

Represents individual messages within a conversation thread:

- **Primary Properties**: thread_id, channel, channel_key, direction, content
- **Nested Data**: logs array for tracking message delivery status
- **Indexes**:
  - Thread lookup by tenant_id, thread_id, and created_at
  - Channel lookup for filtering by communication channel
  - Status lookup for tracking message states

### webhook-subscription-schema.json

Manages webhook integration for outbound messages:

- **Primary Properties**: url, secret, events
- **Performance Features**: error_count tracking, status monitoring
- **Indexes**:
  - Status lookup for active webhooks
  - Workflow-specific webhook filtering
  - Events index for efficient event type matching
