# Usage Quota Integration - Quick Summary

**Status:** RouterPlus UI is fully implemented and ready. Backend integration needed.

## What's Already Done ✅

RouterPlus frontend has **complete support** for usage quota tracking:

- ✅ Parses `usageCount`, `limitCount`, `usageResetAt` from API
- ✅ Displays usage bars with percentages
- ✅ Color-coded warnings (green/orange/red)
- ✅ Formatted text (8.5K/10K, 1.2M/5M)
- ✅ Reset time display
- ✅ Aggregates usage across multiple connections
- ✅ Tooltips with detailed information
- ✅ Sidebar badges and provider cards

## What's Needed 🔧

**9Router backend** needs to return usage data in the `GET /api/providers` response.

### Minimal Required Change

Add 3 optional fields to each connection object:

```json
{
  "connections": [
    {
      "id": "openrouter-1",
      "provider": "openrouter",
      "name": "Work Profile",
      "priority": 10,
      "isActive": true,
      "usageCount": 8500,           // ← Add this (optional)
      "limitCount": 10000,          // ← Add this (optional)
      "usageResetAt": "2026-08-27T00:00:00Z"  // ← Add this (optional)
    }
  ]
}
```

**That's it!** If these fields exist, RouterPlus will display them. If not, it gracefully shows no usage data.

## Quick Start for 9Router Backend

### 1. OpenRouter (Easiest to implement first)

```javascript
async function addOpenRouterUsage(connection) {
  const response = await fetch('https://openrouter.ai/api/v1/auth/key', {
    headers: { 'Authorization': \Bearer \\ }
  });
  
  const data = (await response.json()).data;
  
  return {
    ...connection,
    usageCount: Math.floor(data.usage * 100),      // Convert dollars to cents
    limitCount: Math.floor(data.limit * 100),
    usageResetAt: nextMonthFirstDay().toISOString()
  };
}
```

### 2. Ollama (Track locally - no external API)

```javascript
// Track in your database
async function getOllamaUsage(connectionId) {
  const usage = await db.query(
    'SELECT request_count, daily_limit FROM ollama_usage WHERE id = ?',
    [connectionId]
  );
  
  return {
    usageCount: usage.request_count,
    limitCount: usage.daily_limit || 1000,
    usageResetAt: tomorrow().toISOString()
  };
}
```

### 3. Add Caching (Recommended)

```javascript
const usageCache = new Map();

app.get('/api/providers', async (req, res) => {
  const connections = await db.getAllConnections();
  
  const enriched = await Promise.all(
    connections.map(async (conn) => {
      // Check cache (10 min TTL)
      const cached = usageCache.get(conn.id);
      if (cached && Date.now() - cached.time < 600000) {
        return { ...conn, ...cached.data };
      }
      
      // Fetch fresh usage
      let usage = null;
      try {
        if (conn.provider === 'openrouter') {
          usage = await fetchOpenRouterUsage(conn.apiKey);
        }
        // Add other providers...
        
        if (usage) {
          usageCache.set(conn.id, { data: usage, time: Date.now() });
        }
      } catch (error) {
        console.error(\Usage fetch failed for \:\, error);
      }
      
      return { ...conn, ...usage };
    })
  );
  
  res.json({ connections: enriched });
});
```

## Testing

### 1. Test Data Endpoint

Create a test endpoint:

```javascript
app.get('/api/providers/test', (req, res) => {
  res.json({
    connections: [{
      id: 'test-1',
      provider: 'openrouter',
      name: 'Test',
      priority: 10,
      isActive: true,
      usageCount: 8500,
      limitCount: 10000,
      usageResetAt: '2026-08-27T00:00:00Z'
    }]
  });
});
```

### 2. Verify in RouterPlus

1. Point RouterPlus to test endpoint
2. Sync profile
3. Check sidebar shows usage text
4. Check provider card shows usage bar
5. Hover for tooltip

### Expected UI Result

**Sidebar:**
`
🟢 OR 8.5K/10K
`

**Provider Card:**
`
OpenRouter
[████████░░] 85.0% đã sử dụng
8.5K / 10K
Reset sau 5 ngày
`

**Tooltip:**
`
OpenRouter: OK · 1 connection
Sử dụng: 8.5K/10K (85.0%)
Reset sau 5 ngày
`

## Field Specifications

| Field | Type | Format | Required |
|-------|------|--------|----------|
| `usageCount` | `number` (int64) | Positive integer | No |
| `limitCount` | `number` (int64) | Positive integer | No |
| `usageResetAt` | `string` | ISO 8601 datetime | No |

**Important:**
- All fields are optional
- `usageCount` and `limitCount` must use the same units
- `usageResetAt` must be in the future (UTC timezone)
- If `limitCount` is 0 or missing, percentage not shown

## Provider API Endpoints

| Provider | Endpoint | Method |
|----------|----------|--------|
| OpenRouter | `https://openrouter.ai/api/v1/auth/key` | GET |
| Codex | TBD - check dashboard | GET |
| Kiro | TBD - check dashboard | GET |
| Ollama | Local tracking (no API) | - |
| Kimchi | TBD - check dashboard | GET |

## Implementation Priority

1. **OpenRouter** ⭐ (Most common, documented API)
2. **Ollama** (Local tracking, no external API needed)
3. **Codex** (Check API docs)
4. **Kiro** (Check API docs)
5. **Kimchi** (Check API docs)

## Common Issues

### Issue: "Chưa có dữ liệu" shown

**Solution:** Backend is not returning `usageCount`/`limitCount` fields.

### Issue: Wrong percentage

**Solution:** Ensure `usageCount` and `limitCount` use same units (both requests or both tokens).

### Issue: "đã quá hạn"

**Solution:** `usageResetAt` is in the past. Use future date.

## Complete Documentation

- **[9Router Usage API Integration Guide](9router-usage-api-integration.md)** - Full integration guide
- **[Provider Usage API Examples](provider-usage-api-examples.md)** - Code examples for each provider
- **[Usage Tracking Implementation](usage-tracking-implementation.md)** - How RouterPlus processes the data
- **[Usage Tracking Mockup](mockups/usage-tracking-mockup.html)** - Visual mockup of UI

## Questions?

1. Check the complete integration guide: `docs/9router-usage-api-integration.md`
2. See code examples: `docs/provider-usage-api-examples.md`
3. Review RouterPlus parsing logic: `src/RouterPlus.Infrastructure/Router/RouterApiClient.cs`
4. Check UI implementation: `src/RouterPlus.App/ViewModels/ProviderCardViewModel.cs`

---

**Last Updated:** 2026-08-22  
**RouterPlus Version:** 0.3.0+  
**9Router API Version:** TBD
