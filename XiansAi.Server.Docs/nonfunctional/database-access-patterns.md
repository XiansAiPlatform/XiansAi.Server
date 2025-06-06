# Database Access Patterns

This document catalogs MongoDB collection access patterns across the XiansAi.Server codebase. For each collection, it details:
- Repository implementations and their locations
- Read/write query patterns with their filter combinations
- Index definitions and recommendations
- Special operations (e.g., background tasks, projections)
- Implementation differences between AgentApi and WebApi where applicable

Repository Locations:
- Features/AgentApi/Repositories/
- Features/WebApi/Repositories/

## activity_history
Repos: 
- Features/AgentApi/Repositories/ActivityHistoryRepository.cs [WRITE-ONLY]
- Features/WebApi/Repositories/ActivityRepository.cs [FULL ACCESS]
```
READ: [WebApi]
- (WorkflowId, ActivityId)
- (WorkflowId) SORT(StartedTime DESC)
- (WorkflowType) SORT(StartedTime DESC)
- (StartedTime >= start AND StartedTime <= end) SORT(StartedTime DESC)
- (EndedTime = null) SORT(StartedTime DESC)
- (TaskQueue) SORT(StartedTime DESC)
- SEARCH: (ActivityName OR WorkflowType) [REGEX, CASE-INSENSITIVE] SORT(StartedTime DESC)

WRITE:
- INSERT: Full document [WebApi]
- INSERT: Full document [BACKGROUND, AgentApi]
- UPDATE: By (ActivityId) → Full document [WebApi]
- UPDATE: By (ActivityId) → {EndedTime, Result} [WebApi]
- DELETE: By (ActivityId) [WebApi]

INDEXES: [CONSIDER: 
  - (StartedTime DESC)
  - (WorkflowId, ActivityId)
  - (WorkflowType)
  - (TaskQueue)
]

NOTES:
- AgentApi implementation is write-only with background inserts
- WebApi implementation provides full CRUD with consistent StartedTime DESC sorting
```

## webhooks
Repos:
- Features/AgentApi/Repositories/WebhookRepository.cs
- Features/WebApi/Repositories/WebhookRepository.cs
```
READ:
- (Id, TenantId)
- (WorkflowId, TenantId, IsActive) [AgentApi]
- (WorkflowId, TenantId) [WebApi]
- (TenantId) SORT(CreatedAt DESC) [WebApi]

WRITE:
- INSERT: Full document + CreatedAt
- UPDATE: By (Id)
- DELETE: By (Id, TenantId)

INDEXES:
- (TenantId) ASC [BACKGROUND]
- (WorkflowId, TenantId) ASC [BACKGROUND]

NOTES:
- AgentApi filters by IsActive
- WebApi includes additional tenant-wide listing
```

## logs
Repos:
- Features/AgentApi/Repositories/LogRepository.cs
- Features/WebApi/Repositories/LogRepository.cs
```
READ:
- (Id)
- (TenantId) SORT(CreatedAt DESC)
- (WorkflowId) SORT(CreatedAt DESC)
- (WorkflowRunId, Level?) SORT(CreatedAt DESC) PAGINATED
- (Level) SORT(CreatedAt DESC)
- (CreatedAt >= start AND CreatedAt <= end) SORT(CreatedAt DESC)
- GROUP(WorkflowRunId) LAST() SORT(CreatedAt DESC)

DISTINCT:
- (TenantId, Agent) → ParticipantId PAGINATED
- (TenantId, Agent, ParticipantId?) → WorkflowType
- (TenantId, Agent, WorkflowType, ParticipantId?) → WorkflowId

FILTERED:
- (TenantId, Agent) AND
  Optional: (ParticipantId, WorkflowType, WorkflowId, Level, CreatedAt Range)
  SORT(CreatedAt DESC) PAGINATED

CRITICAL:
- (TenantId, WorkflowType IN list, Level = 5, CreatedAt Range) SORT(CreatedAt DESC)

WRITE:
- INSERT: Full document
- UPDATE: By (Id)
- UPDATE: By (Id) → Properties
- DELETE: By (Id)

INDEXES: [CONSIDER: 
  - (TenantId, Agent, ParticipantId)
  - (TenantId, Agent, WorkflowType)
  - (CreatedAt DESC)
  - (WorkflowRunId)
  - (Level)
]
```

## certificates
Repo: Features/AgentApi/Repositories/ICertificateRepository.cs
```
READ:
- (Thumbprint)
- (Thumbprint) → IsRevoked [PROJECTION]
- (TenantId, IssuedTo)

WRITE:
- INSERT: Full document with duplicate check on Thumbprint
- UPDATE: By (Thumbprint) for revocation
- UPDATE: By (Thumbprint, TenantId) for full document

INDEXES:
- (Thumbprint) ASC UNIQUE
- (TenantId) ASC
- (ExpiresAt) ASC
```

## flow_definitions
Repo: Features/AgentApi/Repositories/FlowDefinitionRepository.cs
```
READ:
- (WorkflowType)

WRITE:
- INSERT: Full document
- UPDATE: By (Id)
- DELETE: By (Id)

INDEXES: None
```

## tenants
Repo: Features/WebApi/Repositories/TenantRepository.cs
```
READ:
- (Id)
- (TenantId)
- (Domain)
- (CreatedBy)
- ALL
- SEARCH: (Name, Domain, Description) [REGEX, CASE-INSENSITIVE]

WRITE:
- INSERT: Full document
- UPDATE: By (Id)
- DELETE: By (Id)

NESTED (Agents):
- PUSH: By (Id) → Agents
- UPDATE: By (Id, Agent.Name) → Agent
- PULL: By (Id, Agent.Name)

INDEXES:
- (TenantId) ASC UNIQUE
- (Domain) ASC UNIQUE
``` 