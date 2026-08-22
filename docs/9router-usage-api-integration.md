# 9Router Usage Quota API Integration Guide

This guide explains how to add usage quota tracking to the 9Router backend so RouterPlus can display usage information for each provider connection.

## Overview

RouterPlus **already has full UI support** for displaying usage quotas. The frontend:
- Parses `usageCount`, `limitCount`, and `usageResetAt` from API responses
- Displays usage bars, percentages, and warnings
- Shows color-coded status indicators (healthy/warning/error)
- Aggregates usage across multiple connections per profile

**What's needed:** The 9Router backend (`http://localhost:20128`) needs to return these fields in the `GET /api/providers` endpoint response.

## Required API Changes

### Current API Response Format

`json
{
  "connections": [
    {
      "id": "openrouter-1",
      "provider": "openrouter",
      "name": "Work Profile",
      "priority": 10,
      "isActive": true,
      "email": "user@example.com",
      "createdAt": "2026-08-20T10:30:00Z",
      "testStatus": "ok",
      "errorCode": null,
      "lastError": null,
      "lastErrorAt": null
    }
  ]
}
`

### Enhanced API Response Format (with usage data)

`json
{
  "connections": [
    {
      "id": "openrouter-1",
      "provider": "openrouter",
      "name": "Work Profile",
      "priority": 10,
      "isActive": true,
      "email": "user@example.com",
      "createdAt": "2026-08-20T10:30:00Z",
      "testStatus": "ok",
      "errorCode": null,
      "lastError": null,
      "lastErrorAt": null,
      "usageCount": 8500,
      "limitCount": 10000,
      "usageResetAt": "2026-08-27T00:00:00Z"
    }
  ]
}
`

### New Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `usageCount` | `number` (int64) | Optional | Current usage count (requests, tokens, etc.) |
| `limitCount` | `number` (int64) | Optional | Maximum allowed usage before reset |
| `usageResetAt` | `string` (ISO 8601) | Optional | When the usage counter resets |

**All three fields are optional.** If not present, RouterPlus simply won't display usage information for that connection.

## How RouterPlus Processes Usage Data

### 1. Parsing (RouterApiClient.cs)

`csharp
var usageCount = element.TryGetProperty("usageCount", out var usageCountElement) &&
                 usageCountElement.ValueKind == JsonValueKind.Number &&
                 usageCountElement.TryGetInt64(out var parsedUsageCount)
    ? parsedUsageCount
    : (long?)null;

var limitCount = element.TryGetProperty("limitCount", out var limitCountElement) &&
                 limitCountElement.ValueKind == JsonValueKind.Number &&
                 limitCountElement.TryGetInt64(out var parsedLimitCount)
    ? parsedLimitCount
    : (long?)null;

var usageResetAt = element.TryGetProperty("usageResetAt", out var usageResetAtElement) &&
                   usageResetAtElement.ValueKind == JsonValueKind.String &&
                   usageResetAtElement.TryGetDateTimeOffset(out var parsedUsageResetAt)
    ? parsedUsageResetAt
    : (DateTimeOffset?)null;
`

### 2. Display Logic

RouterPlus calculates:
- **UsagePercentage**: `(usageCount / limitCount) * 100`
- **IsNearLimit**: `UsagePercentage >= 80%`
- **IsOverLimit**: `UsagePercentage >= 100%`

UI shows:
- Green status: `< 80%`
- Orange status: `>= 80%`
- Red status: `>= 100%`
- Usage text: `"8.5K/10K"` or `"1.2M/5M"`
- Reset time: `"Reset sau 5 ngày"`

## Fetching Usage Data from Providers

Each provider has its own API for fetching usage/quota information. Here's how to get this data:

### OpenRouter

**Endpoint:** `https://openrouter.ai/api/v1/auth/key`

**Headers:**
`
Authorization: Bearer {API_KEY}
`

**Response:**
`json
{
  "data": {
    "label": "My API Key",
    "usage": 42.50,
    "limit": 100.00,
    "is_free_tier": false,
    "rate_limit": {
      "requests": 200,
      "interval": "10s"
    }
  }
}
`

**Mapping:**
- `usageCount`: Convert `usage` (dollars) to approximate requests: `usage * 1000` (rough estimate)
- `limitCount`: Convert `limit` (dollars) to requests: `limit * 1000`
- `usageResetAt`: OpenRouter typically resets monthly; calculate based on account creation date

**Alternative - Generation Stats:**

Endpoint: `https://openrouter.ai/api/v1/generation?id={generation_id}`

Can track per-request usage, but requires tracking each generation.

### Codex

**Endpoint:** `https://api.codex.com/v1/usage` (hypothetical - adjust based on actual API)

Codex likely provides usage through their API. Check documentation at:
- Account settings page
- API documentation
- Dashboard API

### Ollama

**Note:** Ollama is typically self-hosted and may not have built-in quota limits.

For self-hosted Ollama:
- Track usage locally in 9Router's database
- Count requests per connection
- Set arbitrary limits (e.g., 1000 requests/day)

**If using Ollama Cloud:**
Check their API documentation for usage endpoints.

### Kiro

Similar to Codex, check Kiro's API documentation for usage endpoints. Typically at:
- `GET /api/v1/account/usage`
- `GET /api/v1/account/limits`

### Kimchi

Check Kimchi API documentation. Likely similar pattern:
- `GET /api/usage`
- `GET /api/account`

## Implementation Strategy for 9Router Backend

### Option 1: Fetch on Demand (Recommended for MVP)

When RouterPlus calls `GET /api/providers`:

1. For each active connection, make an async request to the provider's usage API
2. Cache the result for 5-10 minutes
3. Return cached data immediately if available
4. If cache miss, return connection without usage data (frontend handles gracefully)

**Pseudocode:**

`javascript
async function getProviderConnections() {
  const connections = await db.getAllConnections();
  
  const enhanced = await Promise.all(
    connections.map(async (conn) => {
      const cached = usageCache.get(conn.id);
      if (cached && !cached.isExpired()) {
        return { ...conn, ...cached.data };
      }
      
      // Fetch in background, don't block response
      fetchUsageData(conn).then(usage => {
        usageCache.set(conn.id, usage, ttl: 600000); // 10 min
      });
      
      return conn; // Return without usage data for now
    })
  );
  
  return { connections: enhanced };
}

async function fetchUsageData(connection) {
  switch (connection.provider) {
    case 'openrouter':
      return await fetchOpenRouterUsage(connection.apiKey);
    case 'codex':
      return await fetchCodexUsage(connection.token);
    case 'ollama':
      return await fetchLocalUsage(connection.id); // Local tracking
    default:
      return null;
  }
}

async function fetchOpenRouterUsage(apiKey) {
  const response = await fetch('https://openrouter.ai/api/v1/auth/key', {
    headers: { Authorization: \Bearer \\ }
  });
  
  const data = await response.json();
  
  return {
    usageCount: Math.floor(data.data.usage * 1000), // Rough conversion
    limitCount: Math.floor(data.data.limit * 1000),
    usageResetAt: calculateNextReset() // Monthly reset
  };
}

function calculateNextReset() {
  const now = new Date();
  const nextMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1);
  return nextMonth.toISOString();
}
`

### Option 2: Background Sync Worker

Run a background job every 5-10 minutes:

1. Fetch usage data for all active connections
2. Store in database
3. `GET /api/providers` just reads from database

**Advantages:**
- Faster API responses
- No timeout issues
- Can handle rate limits better

**Disadvantages:**
- Slightly stale data (up to 10 minutes old)
- More complex architecture

### Option 3: Webhook/Push Updates

Some providers may support webhooks for usage updates. If available, set up webhooks to receive real-time usage data.

## Testing the Integration

### 1. Create Test Data

In your 9Router backend, add test endpoints for development:

`javascript
// Development only
app.get('/api/providers/test', (req, res) => {
  res.json({
    connections: [
      {
        id: 'openrouter-test',
        provider: 'openrouter',
        name: 'Test Profile',
        priority: 10,
        isActive: true,
        usageCount: 8500,
        limitCount: 10000,
        usageResetAt: '2026-08-27T00:00:00Z'
      },
      {
        id: 'codex-test',
        provider: 'codex',
        name: 'Test Profile',
        priority: 5,
        isActive: true,
        usageCount: 1200000,
        limitCount: 5000000,
        usageResetAt: '2026-09-03T00:00:00Z'
      }
    ]
  });
});
`

### 2. Manual Testing in RouterPlus

1. Open RouterPlus
2. Select a profile
3. Click "Đồng bộ" (Sync)
4. Check if usage bars appear in the sidebar
5. Check provider cards show usage information
6. Verify tooltips show usage details

### 3. Expected UI Behavior

**Sidebar (Profile Row):**
- Each provider badge shows usage text like `"8.5K/10K"`
- Badge color: green (healthy), orange (≥80%), red (≥100%)
- Tooltip: `"OpenRouter: OK · 2 connections\nSử dụng: 8.5K/10K (85.0%) · reset sau 5 ngày"`

**Provider Cards:**
- Usage bar shows visual progress
- Text: `"8.5K / 10K"`
- Percentage: `"85.0% đã sử dụng"`
- Reset time: `"Reset sau 5 ngày"`
- Warning message for ≥80%

## Common Issues and Solutions

### Issue: Usage data not showing

**Check:**
1. 9Router backend returns correct JSON format
2. Field names are exactly: `usageCount`, `limitCount`, `usageResetAt`
3. `usageCount` and `limitCount` are numbers (not strings)
4. `usageResetAt` is ISO 8601 string
5. RouterPlus successfully synced (check last sync time)

### Issue: Incorrect percentages

**Check:**
1. `limitCount` is not zero
2. `usageCount` and `limitCount` use same units (both requests or both tokens)
3. Values are not negative

### Issue: Reset time shows "đã quá hạn"

**Check:**
1. `usageResetAt` is in the future
2. Timezone is correct (use UTC: `2026-08-27T00:00:00Z`)

## API Documentation for 9Router

Add this to your 9Router API docs:

---

### GET /api/providers

Returns all provider connections with optional usage quota data.

**Response:**

`json
{
  "connections": [
    {
      "id": "string",
      "provider": "openrouter" | "codex" | "kiro" | "ollama" | "kimchi",
      "name": "string",
      "priority": "number",
      "isActive": "boolean",
      "email": "string | null",
      "createdAt": "string (ISO 8601) | null",
      "testStatus": "string | null",
      "errorCode": "string | null",
      "lastError": "string | null",
      "lastErrorAt": "string (ISO 8601) | null",
      "usageCount": "number (int64) | null",
      "limitCount": "number (int64) | null",
      "usageResetAt": "string (ISO 8601) | null"
    }
  ]
}
`

**Usage Fields:**

- `usageCount`: Current usage count. Can represent requests, tokens, or other units depending on provider.
- `limitCount`: Maximum allowed usage before reset or quota exhaustion.
- `usageResetAt`: ISO 8601 timestamp when usage counter resets. Must be in future.

All three usage fields are optional. If not provided, client will not display usage information.

**Example:**

`ash
curl http://localhost:20128/api/providers
`

---

## Next Steps

1. **Choose implementation strategy** (Option 1 recommended for MVP)
2. **Implement OpenRouter usage fetching first** (most common provider)
3. **Add caching layer** to avoid rate limits
4. **Test with RouterPlus** to verify UI displays correctly
5. **Add other providers incrementally**
6. **Monitor API rate limits** and adjust cache TTL accordingly

## Additional Resources

- [OpenRouter API Documentation](https://openrouter.ai/docs)
- [RouterPlus Usage Tracking Implementation](usage-tracking-implementation.md)
- [Usage Tracking UI Mockup](mockups/usage-tracking-mockup.html)

## Questions?

If you have questions about the integration:

1. Check existing code in `src/RouterPlus.Infrastructure/Router/RouterApiClient.cs`
2. See UI implementation in `src/RouterPlus.App/ViewModels/ProviderCardViewModel.cs`
3. Review mockup at `docs/mockups/usage-tracking-mockup.html`
4. Open an issue on GitHub with specifics about your 9Router backend architecture
