# Usage Statistics Dashboard - Implementation Summary

## Quick Reference

This document provides a high-level overview of the Usage Statistics Dashboard implementation. For detailed specifications, refer to:
- **API Specification:** [USAGE_STATISTICS_API_SPEC.md](./USAGE_STATISTICS_API_SPEC.md)
- **UI Design:** [USAGE_STATISTICS_UI_DESIGN.md](./USAGE_STATISTICS_UI_DESIGN.md)

---

## Overview

The Usage Statistics Dashboard enables users to visualize and analyze:
- **Token Usage:** Track prompt, completion, and total tokens consumed
- **Message Usage:** Monitor message counts across conversations

**Key Features:**
- ✅ Role-based access control (Admin vs Regular Users)
- ✅ Flexible filtering (date range, user, type)
- ✅ Two visualization types (time series chart, user breakdown table)
- ✅ Breakdown by user
- ✅ Responsive design (desktop, tablet, mobile)
- ✅ Matches existing UI/UX theme

---

## API Endpoints Summary

### 1. Token Statistics
```
GET /api/client/usage/statistics/tokens
Query: tenantId, userId, startDate, endDate, groupBy
Auth: RequireTenantAuth
Returns: Time series data + user breakdown
```

### 2. Message Statistics
```
GET /api/client/usage/statistics/messages
Query: tenantId, userId, startDate, endDate, groupBy
Auth: RequireTenantAuth
Returns: Time series data + user breakdown
```

### 3. Users with Usage
```
GET /api/client/usage/statistics/users
Query: tenantId
Auth: RequireTenantAdmin
Returns: List of users for filter dropdown
```


---

## Security Model

### SysAdmin
- ✅ View all tenants
- ✅ View all users
- ✅ Filter by any tenant/user

### TenantAdmin
- ✅ View their tenant only
- ✅ View all users in tenant
- ✅ Filter by users in tenant

### TenantUser
- ✅ View their own data only
- ❌ Cannot view other users
- ❌ User filter hidden

**Implementation:**
```csharp
// Validation in API endpoints
private static void ValidateUserAccess(ITenantContext context, string? requestedUserId)
{
    if (context.UserRoles.Contains(SystemRoles.SysAdmin))
        return; // Full access
    
    if (context.UserRoles.Contains(SystemRoles.TenantAdmin))
        return; // Tenant-level access
    
    // Regular users: own data only
    if (!string.IsNullOrEmpty(requestedUserId) && 
        requestedUserId != context.LoggedInUser)
    {
        throw new UnauthorizedAccessException();
    }
}
```

---

## UI Components

### Navigation
```
Left Menu Item: "📊 Usage Statistics"
Route: /usage-statistics
Visible: All authenticated users
```

### Filter Bar
```
[Tenant ▼] [User: All Users ▼] [⦿ Tokens ○ Messages] [Date Range: Last 7 days ▼]
```

**Role-Based Rendering:**
- **Admin:** Tenant and User filters visible
- **Regular User:** Only Type and Date Range visible, auto-filtered to own data
- **Auto-apply:** No Apply button needed

### Visualizations

#### 1. Time Series Chart
- Shows usage over time (tokens or messages based on type filter)
- Group by: Day (default) / Week / Month
- Interactive hover tooltips
- Line chart style matching existing app theme
- Displays total across all users or for specific user

#### 2. User Breakdown Table
- Shows total tokens or messages per user
- Sortable columns (click header to sort)
- Shows top 100 users
- Uses existing table component styling
- Columns: User Name | Total Tokens/Messages | Prompt | Completion (for tokens only)

---

## Data Flow

### 1. Page Load
```
User navigates to /usage-statistics
    ↓
Check user role (admin vs user)
    ↓
Initialize filters (default or saved preferences)
    ↓
Fetch usage statistics from API
    ↓
Render dashboard with data
```

### 2. Filter Change
```
User modifies filter (e.g., date range)
    ↓
Update filter state
    ↓
Validate filters
    ↓
User clicks "Apply Filters"
    ↓
Fetch new data from API
    ↓
Update all visualizations
```

### 3. Drill Down
```
User clicks on user in breakdown
    ↓
Set user filter to selected user
    ↓
Reload dashboard with filtered data
    ↓
Show user-specific statistics
```

---

## Database Schema

### Existing Collection: `token_usage_events`

**Required Indexes:**
```javascript
// Date range queries
db.token_usage_events.createIndex({ 
    "tenant_id": 1, 
    "created_at": -1 
})

// User-specific queries
db.token_usage_events.createIndex({ 
    "tenant_id": 1, 
    "user_id": 1, 
    "created_at": -1 
})
```

**Note:** Only 2 indexes needed - no model filtering required.

**Aggregation Example (Token Statistics by User):**
```javascript
db.token_usage_events.aggregate([
  {
    $match: {
      tenant_id: "tenant123",
      created_at: { $gte: startDate, $lte: endDate }
    }
  },
  {
    $group: {
      _id: "$user_id",
      totalTokens: { $sum: "$total_tokens" },
      promptTokens: { $sum: "$prompt_tokens" },
      completionTokens: { $sum: "$completion_tokens" },
      messageCount: { $sum: "$message_count" },
      requestCount: { $sum: 1 }
    }
  },
  {
    $sort: { totalTokens: -1 }
  },
  {
    $limit: 100
  }
])
```

---

## Implementation Phases

### Phase 1: Backend API (Week 1-2)
**Files to Create/Modify:**
- ✅ `Shared/Repositories/TokenUsageRepository.cs` - Add aggregation methods
- ✅ `Shared/Services/UsageStatisticsService.cs` - NEW service
- ✅ `Features/WebApi/Endpoints/UsageStatisticsEndpoints.cs` - NEW endpoints
- ✅ `Shared/Configuration/SharedConfiguration.cs` - Register services
- ✅ `Features/WebApi/Configuration/WebApiConfiguration.cs` - Map endpoints

**Tasks:**
1. Implement aggregation methods in repository
2. Create service layer with business logic
3. Build API endpoints with authorization
4. Add MongoDB indexes
5. Write unit tests
6. Write integration tests

**Deliverables:**
- 3 working API endpoints
- 2 MongoDB indexes
- 12+ unit tests
- 3+ integration tests
- Swagger documentation

---

### Phase 2: Frontend UI (Week 3-4)
**Files to Create:**
```
frontend/src/pages/UsageStatistics/
├── index.tsx                      # Main page component
├── components/
│   ├── FilterBar/
│   │   ├── TenantFilter.tsx      # Reuse existing
│   │   ├── UserFilter.tsx        # Reuse existing
│   │   ├── UsageTypeToggle.tsx
│   │   └── DateRangePicker.tsx   # Reuse existing
│   ├── UsageChart/
│   │   └── TimeSeriesChart.tsx   # Use existing chart library
│   └── UserBreakdownTable/
│       └── UserTable.tsx          # Reuse existing table component
├── hooks/
│   ├── useUsageStatistics.ts     # API calls
│   └── useFilters.ts              # Filter state management
├── utils/
│   ├── formatters.ts              # Number/date formatting
│   └── validators.ts              # Filter validation
└── types/
    └── usage.types.ts             # TypeScript definitions
```

**Tasks:**
1. Create page component structure
2. Implement filter bar with role-based rendering (reuse existing components)
3. Integrate time series chart (use existing chart library)
4. Implement user breakdown table (reuse existing table component)
5. Match existing app theme and styling
6. Implement responsive design
7. Write component tests
8. Add to navigation menu

**Deliverables:**
- Fully functional dashboard page
- 8-10 components (reusing existing where possible)
- Theme consistency with existing app
- Responsive design (desktop/tablet/mobile)
- 20+ component tests
- User documentation

---

### Phase 3: Integration & Testing (Week 5)
**Tasks:**
1. End-to-end testing
2. Performance testing (large datasets)
3. Security audit (authorization)
4. Cross-browser testing
5. Mobile device testing
6. Accessibility audit (WCAG 2.1 AA)
7. Load testing
8. Bug fixes

**Deliverables:**
- 10+ E2E tests
- Performance benchmarks
- Security test report
- Accessibility compliance report
- Bug fix log

---

## Performance Targets

### API Response Times
- Simple query (single user, < 30 days): **< 500ms**
- Complex aggregation (all users, grouped): **< 2s**

### UI Performance
- Initial page load: **< 3s**
- Filter application: **< 1s**
- Chart rendering: **< 500ms**
- Smooth 60fps interactions

### Optimization Strategies
1. **MongoDB Indexes:** Ensure all filter combinations are indexed
2. **API Caching:** Cache aggregation results for 5 minutes
3. **Frontend Caching:** Store filter preferences in localStorage
4. **Lazy Loading:** Load chart libraries on demand
5. **Debouncing:** Debounce filter changes (500ms)
6. **Code Splitting:** Separate bundles for charts

---

## Testing Strategy

### Backend Tests

#### Unit Tests (Repository)
```csharp
[Fact]
public async Task GetTokenStatistics_WithDateFilter_ReturnsAggregatedData()
{
    // Arrange
    var startDate = DateTime.UtcNow.AddDays(-7);
    var endDate = DateTime.UtcNow;
    
    // Act
    var result = await _repository.GetTokenStatisticsAsync(
        "tenant123", null, startDate, endDate);
    
    // Assert
    Assert.NotNull(result);
    Assert.True(result.TotalTokens > 0);
}
```

#### Integration Tests (API)
```csharp
[Fact]
public async Task GetTokenStatistics_AsAdmin_ReturnsAllUsers()
{
    // Arrange
    var client = _factory.CreateClientWithAuth(SystemRoles.TenantAdmin);
    
    // Act
    var response = await client.GetAsync(
        "/api/client/usage/statistics/tokens?startDate=2025-12-01&endDate=2025-12-08");
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var data = await response.Content.ReadAsAsync<TokenUsageStatisticsResponse>();
    Assert.NotEmpty(data.UserBreakdown);
}
```

### Frontend Tests

#### Component Tests
```typescript
describe('FilterBar', () => {
  it('hides user filter for regular users', () => {
    render(<FilterBar userRole="user" />);
    expect(screen.queryByLabelText('User')).not.toBeInTheDocument();
  });

  it('shows user filter for admins', () => {
    render(<FilterBar userRole="admin" />);
    expect(screen.getByLabelText('User')).toBeInTheDocument();
  });
});
```

#### E2E Tests
```typescript
describe('Usage Statistics Dashboard', () => {
  it('allows admin to view all users', async () => {
    await loginAsAdmin();
    await navigateTo('/usage-statistics');
    await selectFilter('user', 'All Users');
    
    expect(await getChartData()).toContainAggregatedData();
    expect(await getUserTable()).toContainMultipleUsers();
  });

  it('restricts regular users to own data', async () => {
    await loginAsUser();
    await navigateTo('/usage-statistics');
    
    expect(await getUserFilter()).not.toBeVisible();
    expect(await getChartData()).toContainOnlyCurrentUser();
  });
});
```

---

## Configuration

### API Settings (appsettings.json)
```json
{
  "UsageStatistics": {
    "MaxDateRangeDays": 90,
    "DefaultPageSize": 100,
    "CacheDurationMinutes": 5
  }
}
```

### Feature Flags
```json
{
  "FeatureFlags": {
    "UsageStatistics": {
      "Enabled": true,
      "TokenView": true,
      "MessageView": true,
      "AdvancedFilters": false
    }
  }
}
```

---

## Deployment Checklist

### Backend
- [ ] MongoDB indexes created
- [ ] API endpoints tested
- [ ] Authorization rules verified
- [ ] Rate limiting configured
- [ ] Logging enabled
- [ ] Performance benchmarks met

### Frontend
- [ ] Navigation menu updated
- [ ] All components implemented
- [ ] Responsive design verified
- [ ] Cross-browser tested
- [ ] Accessibility audit passed
- [ ] Performance optimized

### Documentation
- [ ] API documentation in Swagger
- [ ] User guide created
- [ ] Admin guide created
- [ ] Release notes prepared

### Monitoring
- [ ] API endpoint metrics tracked
- [ ] Error logging configured
- [ ] Performance alerts set up
- [ ] Usage analytics enabled

---

## Rollout Plan

### Week 1: Internal Testing
- Deploy to staging environment
- Internal QA testing
- Performance validation
- Security review

### Week 2: Beta Release
- Enable for select tenants
- Gather feedback
- Monitor performance
- Fix critical bugs

### Week 3: General Availability
- Enable for all tenants
- Announce feature
- Provide training materials
- Monitor adoption

---

## Support & Maintenance

### Common Issues & Solutions

#### Issue: Dashboard loading slowly
**Solution:**
- Check MongoDB index usage with `explain()`
- Reduce date range
- Enable API response caching
- Optimize aggregation pipeline

#### Issue: User cannot see data
**Solution:**
- Verify authorization rules
- Check tenant context
- Confirm user has usage events

---

## Future Roadmap

### Q1 2026: Enhanced Analytics
- Trend analysis and predictions
- Anomaly detection
- Cost tracking (tokens → $)
- Budget alerts and notifications

### Q2 2026: Advanced Features
- Custom dashboards
- Scheduled reports (email/PDF)
- Real-time updates (WebSocket)
- Comparison views

### Q3 2026: Intelligence Layer
- Usage optimization recommendations
- Cost savings suggestions
- User behavior insights
- Advanced analytics

---

## References

- [API Specification](./USAGE_STATISTICS_API_SPEC.md)
- [UI Design](./USAGE_STATISTICS_UI_DESIGN.md)
- [Token Usage Event Model](./Shared/Data/Models/Usage/TokenUsageModels.cs)
- [MongoDB Aggregation Framework](https://docs.mongodb.com/manual/aggregation/)
- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)

---

**Last Updated:** December 8, 2025  
**Version:** 1.0  
**Status:** Ready for Implementation

