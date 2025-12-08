# Usage Statistics Dashboard - UI Design Specification

## Overview
This document defines the UI/UX design for the Usage Statistics Dashboard that allows users to visualize and analyze token usage and message usage metrics.

**Design Principle:** Maintain consistency with existing UI/UX theme and patterns used throughout the application.

---

## Table of Contents
1. [Navigation](#navigation)
2. [Dashboard Layout](#dashboard-layout)
3. [Filter Controls](#filter-controls)
4. [Visualization Components](#visualization-components)
5. [User Experience Flows](#user-experience-flows)
6. [Responsive Design](#responsive-design)
7. [Implementation Guide](#implementation-guide)

---

## Navigation

### 1. Left Sidebar Menu

Add new menu item in the main navigation:

```
┌─────────────────────────┐
│ 🏠 Dashboard           │
│ 💬 Conversations       │
│ 🤖 Agents              │
│ 📚 Knowledge Base      │
│ 👥 Users               │
│ 🔑 API Keys            │
│ 📊 Usage Statistics    │  ← NEW
│ ⚙️  Settings           │
└─────────────────────────┘
```

**Icon:** 📊 Chart/Graph icon  
**Label:** "Usage Statistics"  
**Route:** `/usage-statistics`  
**Permissions:** 
- Visible to all authenticated users
- TenantAdmin/SysAdmin see full features
- Regular users see limited view (own data only)

---

## Dashboard Layout

### Main Container Structure

**Note:** Use existing UI theme, color scheme, fonts, and component styles from the current application.

```
┌────────────────────────────────────────────────────────────────────┐
│  📊 Usage Statistics                                               │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │  Filter Controls                                             │ │
│  │  [Tenant ▼] [User: All Users ▼] [⦿ Tokens ○ Messages]      │ │
│  │  [Date Range: Last 7 days ▼]                                │ │
│  └──────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │  Usage Over Time                                             │ │
│  │                                                              │ │
│  │  Chart: Total Tokens/Messages by Date                       │ │
│  │                                                              │ │
│  │  150k ┤                                    ╭──╮             │ │
│  │  100k ┤              ╭──╮           ╭──╮  │  │             │ │
│  │   50k ┤  ╭──╮  ╭──╮  │  │  ╭──╮   │  │  │  │             │ │
│  │     0 ┴──┴──┴──┴──┴──┴──┴──┴──┴───┴──┴──┴──┴─            │ │
│  │       Mon  Tue  Wed  Thu  Fri  Sat  Sun                    │ │
│  │                                                              │ │
│  │  Total: 1,500,000 tokens across 450 requests                │ │
│  └──────────────────────────────────────────────────────────────┘ │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │  Breakdown by User                                           │ │
│  │  [Table View]                                                │ │
│  │                                                              │ │
│  │  User              │  Total Tokens  │  Total Messages       │ │
│  │  ─────────────────────────────────────────────────────────  │ │
│  │  Jane Smith        │  1,000,000     │  300                  │ │
│  │  John Doe          │    500,000     │  150                  │ │
│  │  ─────────────────────────────────────────────────────────  │ │
│  │  TOTAL             │  1,500,000     │  450                  │ │
│  │                                                              │ │
│  └──────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

**Key Layout Principles:**
- ✅ Follow existing page header patterns
- ✅ Use existing filter component styles
- ✅ Match existing card/panel styling
- ✅ Use existing table component
- ✅ Apply existing color palette
- ✅ Maintain consistent spacing/padding

---

## Filter Controls

### 1. Tenant Filter (Admin Only)

**Component:** Dropdown Select  
**Visibility:** SysAdmin only  
**Default:** Current tenant  
**Position:** Top-left of filter bar

```tsx
<TenantSelector
  value={selectedTenant}
  onChange={handleTenantChange}
  disabled={!isSysAdmin}
/>
```

**Behavior:**
- Loads list of all tenants for SysAdmin
- Automatically set to current tenant for TenantAdmin
- Hidden or disabled for regular users

---

### 2. User Filter (Admin Only)

**Component:** Searchable Dropdown Select  
**Visibility:** SysAdmin, TenantAdmin  
**Default:** "All Users"  
**Position:** After tenant filter

```tsx
<UserSelector
  tenantId={selectedTenant}
  value={selectedUser}
  onChange={handleUserChange}
  placeholder="All Users"
  searchable={true}
  disabled={!isAdmin}
/>
```

**Behavior:**
- Shows "All Users" option for admins
- Auto-populated with current user for regular users
- Hidden completely for regular users (not just disabled)
- Search functionality for large user lists
- Displays user name + email in dropdown

**States:**
- **Admin:** Shows dropdown with all users + "All Users" option
- **Regular User:** Component not rendered, userId hardcoded in API call

---

### 3. Usage Type Toggle

**Component:** Radio Button Group / Toggle Switch  
**Options:** "Tokens" | "Messages"  
**Default:** "Tokens"  
**Position:** Center of filter bar

```tsx
<UsageTypeToggle
  value={usageType}
  onChange={handleTypeChange}
  options={[
    { value: 'tokens', label: '🪙 Tokens', icon: <TokenIcon /> },
    { value: 'messages', label: '💬 Messages', icon: <MessageIcon /> }
  ]}
/>
```

**Visual Design:**
```
┌──────────────────────────────────┐
│  ⦿ Tokens    ○ Messages         │
└──────────────────────────────────┘
```

or as toggle:
```
┌──────────────┬──────────────┐
│ 🪙 Tokens    │ 💬 Messages │
│   ACTIVE     │              │
└──────────────┴──────────────┘
```

---

### 4. Date Range Picker

**Component:** Date Range Dropdown with Presets  
**Position:** Center-right of filter bar  
**Required:** Yes

**Preset Options:**
- Today
- Yesterday
- Last 7 days (default)
- Last 30 days
- Last 90 days
- This month
- Last month
- Custom range

```tsx
<DateRangePicker
  value={dateRange}
  onChange={handleDateRangeChange}
  presets={datePresets}
  maxDays={90}
  required={true}
/>
```

**Visual Design:**
```
┌─────────────────────────────────┐
│ Last 7 days ▼                  │
└─────────────────────────────────┘

Dropdown opens to:
┌─────────────────────────────────┐
│ ✓ Last 7 days                  │
│   Last 30 days                  │
│   Last 90 days                  │
│   This month                    │
│   ─────────────────────         │
│   Custom range...               │
└─────────────────────────────────┘
```

**Custom Range Modal:**
```
┌──────────────────────────────────┐
│  Select Date Range              │
│                                  │
│  From: [2025-12-01] 📅          │
│  To:   [2025-12-08] 📅          │
│                                  │
│  Max range: 90 days              │
│                                  │
│  [Cancel]  [Apply]              │
└──────────────────────────────────┘
```

---

**Note:** Filters should auto-apply on change (no "Apply" button needed) to match existing UI patterns.

---

## Visualization Components

### 1. Time Series Chart (Primary Component)

**Chart Type:** Line Chart (consistent with existing app theme)  
**Library:** Use existing chart library in the application  
**Position:** Top section, full-width  
**Height:** 400px (desktop), 300px (tablet), 250px (mobile)

**Display:**
- **X-Axis:** Date/Time (based on grouping: hourly, daily, weekly)
- **Y-Axis:** Token count OR Message count (based on selected type)
- **Title:** "Token Usage Over Time" or "Message Usage Over Time"
- **Subtitle:** Shows total count and date range

**When Type = Tokens:**
```
Token Usage Over Time
Dec 1 - Dec 8, 2025  |  Total: 1,500,000 tokens
────────────────────────────────────────────
150k │                            ╭─────╮
     │                    ╭───╮   │     │
100k │          ╭───╮     │   │   │     │
     │  ╭───╮   │   │╭───╮│   │╭─╮│     │
 50k │  │   │╭─╮│   ││   ││   ││ ││     │
     │╭─│   ││ ││   ││   ││   ││ ││     │
   0 └──┴───┴┴─┴┴───┴┴───┴┴───┴┴─┴┴─────
     Dec 1    Dec 3    Dec 5    Dec 7
```

**When Type = Messages:**
```
Message Usage Over Time
Dec 1 - Dec 8, 2025  |  Total: 450 messages
────────────────────────────────────────────
150 │                            ╭─────╮
    │                    ╭───╮   │     │
100 │          ╭───╮     │   │   │     │
    │  ╭───╮   │   │╭───╮│   │╭─╮│     │
 50 │  │   │╭─╮│   ││   ││   ││ ││     │
    │╭─│   ││ ││   ││   ││   ││ ││     │
  0 └──┴───┴┴─┴┴───┴┴───┴┴───┴┴─┴┴─────
     Dec 1    Dec 3    Dec 5    Dec 7
```

**Interactive Features:**
- Hover to show exact values with date
- Tooltip shows breakdown if viewing all users
- Use existing tooltip styling from app theme

**Styling:**
- Use existing app color palette
- Match existing chart/graph styling
- Consistent font family and sizes
- Same border radius and shadows

---

### 2. Breakdown by User (Table View)

**Component:** Data Table (use existing table component from app)  
**Position:** Below the time series chart  
**Full Width:** Yes

**When Type = Tokens:**
```
┌──────────────────────────────────────────────────────────────────┐
│  Breakdown by User                                               │
│  Sorted by: Total Tokens (Descending)                            │
├──────────────────┬──────────────┬──────────────┬────────────────┤
│ User             │ Total Tokens │ Prompt       │ Completion     │
├──────────────────┼──────────────┼──────────────┼────────────────┤
│ Jane Smith       │  1,000,000   │   600,000    │    400,000     │
│ John Doe         │    500,000   │   300,000    │    200,000     │
├──────────────────┼──────────────┼──────────────┼────────────────┤
│ TOTAL            │  1,500,000   │   900,000    │    600,000     │
└──────────────────┴──────────────┴──────────────┴────────────────┘
```

**When Type = Messages:**
```
┌──────────────────────────────────────────────────────────────────┐
│  Breakdown by User                                               │
│  Sorted by: Total Messages (Descending)                          │
├──────────────────┬──────────────┬──────────────────────────────┤
│ User             │ Total Messages│ Total Requests               │
├──────────────────┼──────────────┼──────────────────────────────┤
│ Jane Smith       │     300      │       300                    │
│ John Doe         │     150      │       150                    │
├──────────────────┼──────────────┼──────────────────────────────┤
│ TOTAL            │     450      │       450                    │
└──────────────────┴──────────────┴──────────────────────────────┘
```

**Features:**
- **Sortable columns:** Click header to sort
- **Pagination:** If > 20 users, paginate
- **Sticky header:** Header stays visible on scroll
- **Row highlight:** Hover effect on rows
- **Use existing table styling:** Match other tables in the app

**When viewing as Regular User:**
- Table shows only single row (current user)
- Total row shows same values (no aggregation needed)

---

## User Experience Flows

### Flow 1: Admin Viewing All Users

```
1. Admin logs in → navigates to Usage Statistics
2. Dashboard loads with default filters:
   - Tenant: Current tenant
   - User: All Users
   - Type: Tokens
   - Date: Last 7 days
   - Model: All Models
3. View time series chart showing daily usage
4. View user breakdown table showing top users
5. Click on specific user in breakdown table
6. Dashboard refreshes showing only that user's data
8. Admin exports data to CSV
```

### Flow 2: Regular User Viewing Own Data

```
1. User logs in → navigates to Usage Statistics
2. Dashboard loads with forced filters:
   - User: Current user (hidden from UI)
   - Type: Tokens
   - Date: Last 7 days
   - Model: All Models
3. User cannot see other users' data
4. User changes type to Messages
5. Dashboard updates to show message counts
6. User selects "Last 30 days"
7. Dashboard reloads with new date range
8. User exports their own data
```

### Flow 3: Comparing Time Periods

```
1. Admin selects "Last 30 days"
2. Views current usage
3. Summary cards show comparison to previous 30 days
4. Notices 15% increase in token usage
5. Clicks on user breakdown
6. Identifies which users increased usage
7. Drills down to specific user
8. Views model breakdown for that user
9. Exports detailed report
```

---

## Responsive Design

### Desktop (> 1024px)
- Full layout as shown above
- Filters in single row
- Full-width chart
- Full-width table

### Tablet (768px - 1024px)
- Filters may wrap to 2 rows
- Full-width chart (reduced height)
- Full-width table with horizontal scroll if needed

### Mobile (< 768px)
- Stacked layout for all sections
- Filters stack vertically or in collapsible panel
- Chart reduced height (250px)
- Table with horizontal scroll
- Simplified chart interactions (tap instead of hover)

```
Mobile Layout:
┌────────────────────┐
│  📊 Usage Stats    │
├────────────────────┤
│  [☰ Filters]      │
├────────────────────┤
│  [Chart]          │
│  (scrollable)     │
├────────────────────┤
│  [User Table]     │
│  (scrollable)     │
└────────────────────┘
```

**Note:** Follow existing responsive breakpoints and patterns used in the app.

---

## Implementation Guide

### Technology Stack

**IMPORTANT:** Use the same technology stack as the existing application.

**Frontend Framework:** Match existing (React / Vue / Angular)  
**Chart Library:** Use existing chart library in the app  
**Date Picker:** Use existing date picker component  
**State Management:** Use existing state management approach  
**Styling:** Use existing CSS framework and theme system
**Components:** Reuse existing UI component library

### Component Structure

```
src/pages/UsageStatistics/
├── index.tsx                      # Main page component
├── components/
│   ├── FilterBar/
│   │   ├── TenantFilter.tsx
│   │   ├── UserFilter.tsx
│   │   ├── UsageTypeToggle.tsx
│   │   └── DateRangePicker.tsx
│   ├── UsageChart/
│   │   └── TimeSeriesChart.tsx   # Main chart component
│   └── UserBreakdownTable/
│       └── UserTable.tsx          # User breakdown table
├── hooks/
│   ├── useUsageStatistics.ts     # API calls
│   └── useFilters.ts              # Filter state management
├── utils/
│   ├── chartHelpers.ts
│   ├── formatters.ts              # Number/date formatting
│   └── validators.ts              # Filter validation
└── types/
    └── usage.types.ts             # TypeScript definitions

**Important:** Reuse existing components where possible:
- Use existing Table component
- Use existing Dropdown/Select components
- Use existing DatePicker component
- Match existing layout containers and spacing
```

### Key React Hooks

```tsx
// Custom hook for fetching usage statistics
function useUsageStatistics(filters: UsageFilters) {
  const [data, setData] = useState<UsageStatisticsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    async function fetchData() {
      setLoading(true);
      try {
        const response = await api.getUsageStatistics(filters);
        setData(response);
      } catch (err) {
        setError(err as Error);
      } finally {
        setLoading(false);
      }
    }
    fetchData();
  }, [filters]);

  return { data, loading, error };
}

// Custom hook for managing filter state
function useFilters(userRole: UserRole) {
  const [filters, setFilters] = useState<UsageFilters>({
    tenantId: currentTenant,
    userId: userRole === 'user' ? currentUser : 'all',
    type: 'tokens',
    startDate: subDays(new Date(), 7),
    endDate: new Date(),
    groupBy: 'day'
  });

  const updateFilter = (key: keyof UsageFilters, value: any) => {
    setFilters(prev => ({ ...prev, [key]: value }));
  };

  return { filters, updateFilter };
}
```

### API Integration

```typescript
// api/usageStatistics.ts
export async function getTokenStatistics(
  params: UsageStatisticsParams
): Promise<TokenUsageStatisticsResponse> {
  const response = await axios.get('/api/client/usage/statistics/tokens', {
    params: {
      tenantId: params.tenantId,
      userId: params.userId === 'all' ? undefined : params.userId,
      startDate: params.startDate.toISOString(),
      endDate: params.endDate.toISOString(),
      groupBy: params.groupBy
    }
  });
  return response.data;
}

export async function getMessageStatistics(
  params: UsageStatisticsParams
): Promise<MessageUsageStatisticsResponse> {
  const response = await axios.get('/api/client/usage/statistics/messages', {
    params: {
      tenantId: params.tenantId,
      userId: params.userId === 'all' ? undefined : params.userId,
      startDate: params.startDate.toISOString(),
      endDate: params.endDate.toISOString(),
      groupBy: params.groupBy
    }
  });
  return response.data;
}
```

### State Management

```typescript
// Redux slice for usage statistics
const usageStatisticsSlice = createSlice({
  name: 'usageStatistics',
  initialState: {
    filters: defaultFilters,
    tokenData: null,
    messageData: null,
    loading: false,
    error: null
  },
  reducers: {
    setFilters: (state, action) => {
      state.filters = action.payload;
    },
    setTokenData: (state, action) => {
      state.tokenData = action.payload;
    },
    setMessageData: (state, action) => {
      state.messageData = action.payload;
    },
    setLoading: (state, action) => {
      state.loading = action.payload;
    },
    setError: (state, action) => {
      state.error = action.payload;
    }
  }
});
```

---

## Accessibility

### WCAG 2.1 AA Compliance

1. **Keyboard Navigation:**
   - All filters accessible via Tab key
   - Charts navigable with arrow keys
   - Export buttons have keyboard shortcuts

2. **Screen Reader Support:**
   - ARIA labels on all interactive elements
   - Chart data accessible via table alternative
   - Status announcements for loading/errors

3. **Color Contrast:**
   - All text meets 4.5:1 contrast ratio
   - Charts use patterns in addition to colors
   - High contrast mode available

4. **Focus Indicators:**
   - Visible focus rings on all controls
   - Skip to content link
   - Focus trapped in modals

### Example ARIA Labels:

```tsx
<button 
  aria-label="Filter usage data by date range"
  aria-describedby="date-filter-description"
>
  Apply Filters
</button>

<div 
  role="img" 
  aria-label="Line chart showing token usage over time. 1.5 million total tokens used across 450 requests in the last 7 days."
>
  {/* Chart component */}
</div>
```

---

## Performance Optimization

1. **Lazy Loading:**
   - Load chart libraries only when needed
   - Defer non-critical components

2. **Caching:**
   - Cache API responses for 5 minutes
   - Store filter preferences in localStorage
   - Memoize expensive calculations

3. **Debouncing:**
   - Debounce filter changes (500ms)
   - Throttle chart re-renders
   - Lazy load dropdown options

4. **Code Splitting:**
   - Separate bundle for charts
   - Route-based code splitting

---

## Testing Strategy

### Unit Tests
- Filter validation logic
- Data formatting functions
- Chart data transformations

### Integration Tests
- API integration
- Filter state management

### E2E Tests
- Complete user flows
- Admin vs user permissions
- Cross-browser compatibility

### Visual Regression Tests
- Chart rendering
- Responsive layouts
- Theme variants

---

## Future Enhancements

### Phase 2 Features
- Real-time updates (WebSocket)
- Custom alerts/notifications
- Scheduled email reports
- Cost tracking (tokens → $)
- Budget alerts

### Phase 3 Features
- Advanced analytics (trends, predictions)
- Anomaly detection
- Comparison views (user vs user, period vs period)
- Custom dashboards
- API usage analytics

---

## Design Assets Needed

1. **Icons:**
   - Token icon (🪙)
   - Message icon (💬)
   - Chart icon (📊)

2. **Color Palette:**
   - Primary: #3B82F6 (blue)
   - Secondary: #10B981 (green)
   - Accent: #F59E0B (amber)
   - Error: #EF4444 (red)
   - Success: #10B981 (green)

5. **Typography:**
   - Headings: Inter/Roboto Bold
   - Body: Inter/Roboto Regular
   - Numbers: Tabular figures for alignment

---

## Implementation Checklist

### Backend
- [ ] Create UsageStatisticsEndpoints.cs
- [ ] Implement UsageStatisticsService.cs
- [ ] Add aggregation methods to TokenUsageRepository.cs
- [ ] Create MongoDB indexes for performance
- [ ] Add authorization checks
- [ ] Write API tests

### Frontend
- [ ] Create UsageStatistics page component
- [ ] Implement FilterBar (reuse existing filter components)
- [ ] Integrate TimeSeriesChart (use existing chart library)
- [ ] Implement UserBreakdownTable (reuse existing table component)
- [ ] Add role-based rendering (hide user filter for regular users)
- [ ] Implement responsive layout
- [ ] Add loading/error states (use existing patterns)
- [ ] Match existing UI theme and styling
- [ ] Write component tests

### Integration
- [ ] Connect frontend to API endpoints
- [ ] Test permission-based UI rendering
- [ ] Verify data accuracy
- [ ] Performance testing
- [ ] Verify theme consistency with existing app
- [ ] Cross-browser testing
- [ ] Mobile testing

### Documentation
- [ ] Update user documentation
- [ ] Create admin guide
- [ ] Add API documentation

---

This design provides a comprehensive, user-friendly interface for viewing and analyzing usage statistics while maintaining security and performance standards.

