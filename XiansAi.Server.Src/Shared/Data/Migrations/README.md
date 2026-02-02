# Database Migrations

This directory contains database migrations for the XiansAi Server.

## Email Normalization Migration

### Purpose
Normalizes all existing user email addresses to lowercase for consistency and to enable efficient case-insensitive email lookups.

### Why This Is Needed
- The `User` model now automatically converts emails to lowercase when setting the `Email` property
- This ensures all new emails are stored in lowercase
- However, existing emails in the database may have mixed case
- This migration updates all existing emails to be lowercase

### How to Run

You can run the migration using the Admin API endpoint:

```bash
POST /api/v1/admin/migrations/normalize-emails
Authorization: Bearer <your-admin-token>
```

**Example using curl:**

```bash
curl -X POST "https://your-api.com/api/v1/admin/migrations/normalize-emails" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

**Example Response:**

```json
{
  "message": "Email normalization completed successfully",
  "totalProcessed": 150,
  "updated": 23,
  "errors": 0
}
```

### What It Does

1. Fetches all users from the database
2. For each user, checks if the email needs normalization (contains uppercase letters)
3. Updates only those emails that need normalization
4. Logs all changes
5. Returns a summary of the migration results

### Safety Features

- **Idempotent**: Can be run multiple times safely - only updates emails that need normalization
- **Logged**: All changes are logged for audit purposes
- **Error Handling**: Continues processing even if individual user updates fail
- **No Data Loss**: Only changes the casing of emails, doesn't modify the email addresses themselves

### When to Run

- **One-time**: After deploying the code changes that add email normalization to the User model
- **Optional**: If you don't have any existing users, or all emails are already lowercase, you don't need to run this
- **Safe to Re-run**: You can run this multiple times without any negative effects

### After Migration

Once the migration is complete:
- All emails in the database will be lowercase
- New emails will automatically be stored in lowercase (handled by the User model setter)
- Email lookups will be fast and case-insensitive
