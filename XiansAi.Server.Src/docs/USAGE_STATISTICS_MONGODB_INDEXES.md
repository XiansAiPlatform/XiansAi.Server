# MongoDB Indexes for Usage Statistics

## Overview
This document provides the MongoDB index definitions required for optimal performance of the Usage Statistics Dashboard.

## Collection: `token_usage_events`

### Required Indexes

#### 1. Tenant and Date Range Index
This index supports queries filtering by tenant and date range, which is the most common query pattern.

```javascript
db.token_usage_events.createIndex(
  { 
    "tenant_id": 1, 
    "created_at": -1 
  },
  {
    name: "idx_tenant_date",
    background: true
  }
)
```

**Used by:**
- Token statistics queries
- Message statistics queries
- All time-series aggregations

**Query Pattern:**
```javascript
{
  "tenant_id": "tenant123",
  "created_at": { "$gte": startDate, "$lte": endDate }
}
```

---

#### 2. Tenant, User, and Date Range Index
This index supports user-specific queries, which are used when filtering by a specific user.

```javascript
db.token_usage_events.createIndex(
  { 
    "tenant_id": 1, 
    "user_id": 1, 
    "created_at": -1 
  },
  {
    name: "idx_tenant_user_date",
    background: true
  }
)
```

**Used by:**
- User-specific token statistics
- User-specific message statistics
- Regular user dashboard views (auto-filtered to own user)

**Query Pattern:**
```javascript
{
  "tenant_id": "tenant123",
  "user_id": "user456",
  "created_at": { "$gte": startDate, "$lte": endDate }
}
```

---

## Index Creation Script

You can create all required indexes at once using the following script:

```javascript
// MongoDB Shell Script
// Usage: mongosh <connection-string> < create-usage-statistics-indexes.js

use your_database_name;

print("Creating indexes for token_usage_events collection...");

// Index 1: Tenant and Date Range
db.token_usage_events.createIndex(
  { "tenant_id": 1, "created_at": -1 },
  { name: "idx_tenant_date", background: true }
);
print("✓ Created idx_tenant_date");

// Index 2: Tenant, User, and Date Range
db.token_usage_events.createIndex(
  { "tenant_id": 1, "user_id": 1, "created_at": -1 },
  { name: "idx_tenant_user_date", background: true }
);
print("✓ Created idx_tenant_user_date");

print("All indexes created successfully!");
```

---

## Verifying Indexes

After creating the indexes, verify they exist:

```javascript
db.token_usage_events.getIndexes()
```

Expected output should include:
- `_id_` (default)
- `idx_tenant_date`
- `idx_tenant_user_date`

---

## Index Performance

### Expected Query Performance

| Query Type | Index Used | Expected Response Time |
|-----------|-----------|----------------------|
| Tenant-wide stats (< 30 days) | `idx_tenant_date` | < 500ms |
| Tenant-wide stats (< 90 days) | `idx_tenant_date` | < 2s |
| User-specific stats (< 30 days) | `idx_tenant_user_date` | < 200ms |
| User-specific stats (< 90 days) | `idx_tenant_user_date` | < 1s |

---

## Query Optimization Tips

### 1. Verify Index Usage
Use `explain()` to verify queries are using indexes:

```javascript
db.token_usage_events.find({
  "tenant_id": "tenant123",
  "created_at": { "$gte": ISODate("2025-12-01"), "$lte": ISODate("2025-12-08") }
}).explain("executionStats")
```

Look for:
- `executionStats.executionSuccess: true`
- `winningPlan.inputStage.indexName: "idx_tenant_date"`
- `executionStats.totalDocsExamined` should be close to `nReturned`

### 2. Index Hit Rate
Monitor index hit rates in production:

```javascript
db.token_usage_events.aggregate([
  { $indexStats: {} }
])
```

### 3. Slow Query Log
Enable slow query logging to identify unoptimized queries:

```javascript
db.setProfilingLevel(1, { slowms: 1000 })
```

---

## Index Maintenance

### Rebuilding Indexes
If indexes become fragmented over time:

```javascript
db.token_usage_events.reIndex()
```

⚠️ **Warning:** `reIndex()` blocks the collection. Use only during maintenance windows or with replica sets.

### Dropping Old Indexes
If you need to remove indexes (e.g., during schema changes):

```javascript
db.token_usage_events.dropIndex("idx_tenant_date")
db.token_usage_events.dropIndex("idx_tenant_user_date")
```

---

## Storage Estimates

### Index Size Estimates
- **idx_tenant_date:** ~5-10% of collection size
- **idx_tenant_user_date:** ~10-15% of collection size

### Example:
If `token_usage_events` collection is 1 GB:
- Total index size: ~150-250 MB
- Total storage: ~1.15-1.25 GB

---

## Production Deployment

### Pre-Deployment Checklist
- [ ] Create indexes in **staging** environment first
- [ ] Verify query performance in staging
- [ ] Use `background: true` to avoid blocking writes
- [ ] Schedule index creation during low-traffic periods
- [ ] Monitor server resources (CPU, RAM, disk I/O) during creation

### Rollback Plan
If indexes cause performance issues:
1. Drop the new indexes immediately
2. Investigate query patterns
3. Adjust index definitions as needed
4. Re-create indexes with background option

---

## Monitoring

### Key Metrics to Monitor
- **Query Response Time:** Track P50, P95, P99
- **Index Hit Rate:** Should be > 95%
- **Collection Scan Rate:** Should be < 1%
- **Index Size:** Should remain < 20% of collection size

### Alerts to Set Up
- Query response time > 2s for simple queries
- Index hit rate < 90%
- Collection scan detected on `token_usage_events`

---

## Additional Considerations

### Compound Index Order
The order of fields in compound indexes matters:
- **Equality** filters first (`tenant_id`)
- **Sort/Range** filters last (`created_at`)

### Covered Queries
For maximum performance, queries that only need projected fields from the index (not full documents) will be "covered" and won't need to read from the collection.

### Partial Indexes (Future Optimization)
If you only need statistics for recent data, consider partial indexes:

```javascript
db.token_usage_events.createIndex(
  { "tenant_id": 1, "created_at": -1 },
  { 
    name: "idx_tenant_date_recent",
    partialFilterExpression: { 
      "created_at": { "$gte": new Date("2025-01-01") } 
    }
  }
)
```

This reduces index size by only indexing recent documents.

---

**Last Updated:** December 8, 2025  
**Version:** 1.0  
**Related Docs:**
- [API Specification](./USAGE_STATISTICS_API_SPEC.md)
- [UI Design](./USAGE_STATISTICS_UI_DESIGN.md)
- [Implementation Summary](./USAGE_STATISTICS_IMPLEMENTATION_SUMMARY.md)

