# Slack User Info Integration

## Overview
The Slack webhook handler now fetches user information (including email) from the Slack API when processing incoming messages.

## Implementation Details

### Changes Made

1. **SlackModels.cs**
   - Added `ParentUserId` field to `SlackEvent` model to capture thread starter
   - Added `SlackUserInfoResponse` model for Slack API responses
   - Added `SlackUserInfo` model for user details
   - Added `SlackUserProfile` model containing email and other profile information
   - Added `SlackApiBaseUrl` constant

2. **SlackWebhookHandler.cs**
   - Added static cache `UserInfoCache` to store user info and avoid repeated API calls
   - Added `GetSlackUserInfoAsync()` method to fetch user information from Slack API
   - Updated `DetermineParticipantId()` to support `"userEmail"` as a `ParticipantIdSource` option
   - Updated `ProcessEventAsync()` to:
     - Conditionally fetch user info only when `ParticipantIdSource` is `"userEmail"`
     - Include `userEmail`, `userName`, and `parentUserId` in message data (when available)

### Data Structure

When a Slack message is processed, the following user information is now included in the message data:

```json
{
  "slack": {
    "userId": "U09H1HRRWG2",
    "userEmail": "user@example.com",
    "userName": "John Doe",
    "channel": "D0ACEUFL0NB",
    "threadTs": "1770204256.411579",
    "parentUserId": "U0ADQKXNQL8",
    "ts": "1770204885.502139",
    "teamId": "T07TBMZUEEN",
    "eventType": "message"
  }
}
```

## Configuration Requirements

### To Use Email as Participant ID

Set `participantIdSource` to `"userEmail"` in the mapping configuration:

```json
{
  "configuration": {
    "botToken": "xoxb-your-bot-token-here",
    "signingSecret": "..."
  },
  "mappingConfig": {
    "participantIdSource": "userEmail",
    "scopeSource": "channelId"
  }
}
```

### Required Slack OAuth Scopes
When using `"userEmail"` as the participant ID source, the bot token must have:
- `users:read` - To fetch basic user information
- `users:read.email` - To fetch user email addresses

### Performance Optimization
- User information is fetched from Slack **only** when `participantIdSource` is set to `"userEmail"`
- Results are cached in-memory to minimize API calls
- For other participant ID sources (`userId`, `channelId`, `threadId`), no API calls are made

## Caching Strategy

User information is cached in-memory using a `ConcurrentDictionary` with the key format:
```
{integrationId}:{userId}
```

This prevents repeated API calls for the same user within the application lifecycle.

## Error Handling

- If bot token is not configured, the handler logs a debug message and continues without user info
- If the Slack API call fails, the error is logged and the message is still processed
- If user info cannot be fetched, `userEmail` and `userName` will be `null` in the message data

## Participant ID Logic

The participant ID is determined based on the `ParticipantIdSource` configuration:

**Dynamic Options (from Slack event data):**
- **`userEmail`**: Fetches and uses the user's email address from Slack
  - Requires `users:read` and `users:read.email` scopes
  - Falls back to `userId` if email cannot be fetched
  - Provides consistent, human-readable identification across workspaces
  
- **`userId`**: Uses Slack user ID (e.g., "U09H1HRRWG2")
  - No additional scopes required
  
- **`channelId`**: Uses Slack channel ID
  - Treats all users in a channel as the same participant
  
- **`threadId`**: Uses thread timestamp or channel ID
  - Groups participants by conversation thread

**Fixed Value:**
- **`null` or empty**: Uses `defaultParticipantId` from configuration
  - Example: `"participantIdSource": null, "defaultParticipantId": "support-team"`
  - All messages use "support-team" as participant
  - Useful when treating all Slack users as a single participant

## Scope Logic

The scope is determined based on the `ScopeSource` configuration:

**Dynamic Options (from Slack event data):**
- **`channelId`**: Uses the Slack channel ID
- **`threadId`**: Uses the thread timestamp

**Fixed Value:**
- **`null` or empty**: Uses `defaultScope` from configuration
  - Example: `"scopeSource": null, "defaultScope": "Slack"`
  - All messages grouped under "Slack" scope

### Field Usage Clarification

**`userId` vs `parentUserId`:**
- **`userId`**: The actual person sending the current message
- **`parentUserId`**: The person who started the thread (useful for thread context)

Both are included in the message data for full context.

## API Details

The handler calls the Slack Web API endpoint:
```
GET https://slack.com/api/users.info?user={userId}
Authorization: Bearer {botToken}
```

Response includes user profile with email, real name, display name, and other details.
