# Verify and Fix MongoDB TTL Index for Secrets

## Issue
TTL index with partial filter doesn't work reliably. MongoDB's TTL monitor may not evaluate partial filters correctly.

## Solution Applied
Removed the partial filter from the TTL index. MongoDB **automatically ignores** documents where the `expire_at` field is null or missing, so the partial filter was unnecessary.

## Steps to Verify and Fix

### 1. Check Existing Indexes
Connect to your MongoDB instance and run:

```javascript
use your_database_name
db.secrets.getIndexes()
```

Look for an index on `expire_at`. You should see something like:
```json
{
  "v": 2,
  "key": { "expire_at": 1 },
  "name": "expire_at_1",
  "expireAfterSeconds": 0
}
```

### 2. Drop Old TTL Index (if it has partial filter)
If the existing TTL index has a `partialFilterExpression`, drop it:

```javascript
db.secrets.dropIndex("expire_at_1")
```

### 3. Restart Your Application
The corrected index will be created automatically on startup. Check your logs for:
```
Created TTL index on expire_at (documents with null/missing expire_at are not affected)
```

### 4. Verify TTL is Working
Create a test secret that expires in 2 minutes:

**Via API:**
```json
POST /api/admin/tenants/{tenantId}/secrets
{
  "secretId": "test-ttl-secret",
  "secretValue": "test-value",
  "expireAt": "2026-02-06T10:05:00Z"  // 2 minutes from now in UTC
}
```

**Check MongoDB:**
```javascript
// Should exist immediately
db.secrets.find({ secret_id: "test-ttl-secret" })

// Wait 2-3 minutes (TTL monitor runs every 60 seconds)
// Should be deleted automatically
db.secrets.find({ secret_id: "test-ttl-secret" })  // Should return empty
```

## Important Notes

1. **TTL Delay**: MongoDB's TTL monitor runs approximately every 60 seconds, so documents may exist for up to 1 minute after expiration.

2. **UTC Only**: Ensure all `expireAt` values are in UTC. The TTL monitor compares against MongoDB server's UTC time.

3. **Null Safety**: Documents without `expire_at` or with `null` value are **never** deleted by TTL. This is MongoDB's default behavior.

4. **Background Process**: TTL deletion is done by a MongoDB background thread (`TTLMonitor`). If MongoDB is under heavy load, deletions may be delayed.

## Troubleshooting

### TTL Still Not Working?

1. **Check MongoDB version** (TTL requires MongoDB 2.2+):
   ```javascript
   db.version()
   ```

2. **Verify TTL monitor is running**:
   ```javascript
   db.currentOp({ "command.deleteIndexes": { $exists: false } })
   ```

3. **Check for index errors in MongoDB logs**:
   ```bash
   # Look for TTL-related errors
   grep -i "ttl" /var/log/mongodb/mongod.log
   ```

4. **Manually verify index structure**:
   ```javascript
   db.secrets.getIndexes().forEach(function(idx) {
       if (idx.key.expire_at) {
           print("Found TTL index:");
           printjson(idx);
       }
   });
   ```

5. **Test with a past date**:
   Create a secret with `expireAt` already in the past (e.g., 5 minutes ago). It should be deleted within ~60 seconds.

## Expected Behavior

- Secret with `expireAt: null` → **Never deleted**
- Secret with `expireAt: "2026-12-31T23:59:59Z"` → **Deleted after Dec 31, 2026**
- Secret with `expireAt: "2026-02-06T10:00:00Z"` (in the past) → **Deleted within ~60 seconds**
