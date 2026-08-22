# Provider Usage API Examples

Concrete code examples for fetching usage quota data from each provider's API.

## OpenRouter

### API Documentation
- [OpenRouter API Docs](https://openrouter.ai/docs)
- [Account Management](https://openrouter.ai/settings/keys)

### Fetch Account Usage

**Endpoint:** `GET https://openrouter.ai/api/v1/auth/key`

**Request:**
```bash
curl -X GET https://openrouter.ai/api/v1/auth/key \
  -H "Authorization: Bearer YOUR_API_KEY"
```

**Response:**
```json
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
```

### Node.js Implementation

```javascript
async function fetchOpenRouterUsage(apiKey) {
  const response = await fetch('https://openrouter.ai/api/v1/auth/key', {
    method: 'GET',
    headers: {
      'Authorization': \Bearer \\,
      'HTTP-Referer': 'http://localhost:20128',
      'X-Title': '9Router'
    }
  });

  if (!response.ok) {
    throw new Error(\OpenRouter API error: \\);
  }

  const json = await response.json();
  const data = json.data;

  // OpenRouter uses dollar amounts, convert to request estimates
  // Assuming average cost of \.001 per request (adjust based on model)
  const avgCostPerRequest = 0.001;
  
  return {
    usageCount: Math.floor(data.usage / avgCostPerRequest),
    limitCount: Math.floor(data.limit / avgCostPerRequest),
    usageResetAt: calculateMonthlyReset().toISOString()
  };
}

function calculateMonthlyReset() {
  const now = new Date();
  const nextMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1, 0, 0, 0, 0);
  return nextMonth;
}
```

### Alternative: Track by Credits/Dollars

```javascript
async function fetchOpenRouterUsageInDollars(apiKey) {
  const response = await fetch('https://openrouter.ai/api/v1/auth/key', {
    method: 'GET',
    headers: {
      'Authorization': \Bearer \\
    }
  });

  const json = await response.json();
  const data = json.data;

  // Track in cents to avoid floating point issues
  return {
    usageCount: Math.floor(data.usage * 100), // \.50 -> 4250 cents
    limitCount: Math.floor(data.limit * 100), // \.00 -> 10000 cents
    usageResetAt: calculateMonthlyReset().toISOString()
  };
}
```

## Codex (OpenAI Codex)

### API Documentation
- Check Codex dashboard for API documentation
- Likely uses OpenAI-style API structure

### Estimated Endpoint

**Endpoint:** `GET https://api.openai.com/v1/usage` or `GET https://api.openai.com/v1/dashboard/billing/usage`

**Request:**
```bash
curl -X GET https://api.openai.com/v1/dashboard/billing/usage?date=2026-08 \
  -H "Authorization: Bearer YOUR_API_KEY"
```

### Node.js Implementation (Hypothetical)

```javascript
async function fetchCodexUsage(apiKey) {
  // Replace with actual Codex API endpoint
  const now = new Date();
  const yearMonth = \\-\\;
  
  const response = await fetch(
    \https://api.codex.com/v1/usage?date=\\,
    {
      method: 'GET',
      headers: {
        'Authorization': \Bearer \\
      }
    }
  );

  if (!response.ok) {
    throw new Error(\Codex API error: \\);
  }

  const json = await response.json();

  // Adjust based on actual Codex API response structure
  return {
    usageCount: json.total_tokens || json.total_requests || 0,
    limitCount: json.token_limit || json.request_limit || 5000000,
    usageResetAt: calculateMonthlyReset().toISOString()
  };
}
```

### Alternative: Check Account Info

Some providers expose limits in account endpoint:

```javascript
async function fetchCodexAccount(apiKey) {
  const response = await fetch('https://api.codex.com/v1/account', {
    method: 'GET',
    headers: {
      'Authorization': \Bearer \\
    }
  });

  const account = await response.json();

  return {
    usageCount: account.usage?.current || 0,
    limitCount: account.usage?.limit || 0,
    usageResetAt: account.usage?.reset_at || calculateMonthlyReset().toISOString()
  };
}
```

## Kiro

### API Documentation
- Check Kiro dashboard for API endpoints
- Likely similar to Codex/OpenAI structure

### Node.js Implementation

```javascript
async function fetchKiroUsage(apiKey) {
  // Replace with actual Kiro API endpoint
  const response = await fetch('https://api.kiro.ai/v1/account/usage', {
    method: 'GET',
    headers: {
      'Authorization': \Bearer \\,
      'Content-Type': 'application/json'
    }
  });

  if (!response.ok) {
    throw new Error(\Kiro API error: \\);
  }

  const json = await response.json();

  // Adjust based on actual Kiro API response
  return {
    usageCount: json.requests_used || json.tokens_used || 0,
    limitCount: json.requests_limit || json.tokens_limit || 500,
    usageResetAt: json.reset_date || calculateMonthlyReset().toISOString()
  };
}
```

## Ollama

### Self-Hosted Tracking

Ollama doesn't have built-in quota limits. Track usage locally in 9Router's database.

### Database Schema

```sql
CREATE TABLE ollama_usage (
  connection_id TEXT PRIMARY KEY,
  request_count INTEGER DEFAULT 0,
  last_reset_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  daily_limit INTEGER DEFAULT 1000
);
```

### Node.js Implementation

```javascript
// Increment usage when request is made
async function trackOllamaRequest(connectionId, db) {
  await db.run(
    \UPDATE ollama_usage 
     SET request_count = request_count + 1 
     WHERE connection_id = ?\,
    [connectionId]
  );
}

// Reset daily counter
async function resetOllamaUsageIfNeeded(connectionId, db) {
  const row = await db.get(
    'SELECT * FROM ollama_usage WHERE connection_id = ?',
    [connectionId]
  );

  if (!row) return;

  const lastReset = new Date(row.last_reset_at);
  const now = new Date();
  const hoursSinceReset = (now - lastReset) / (1000 * 60 * 60);

  if (hoursSinceReset >= 24) {
    await db.run(
      \UPDATE ollama_usage 
       SET request_count = 0, last_reset_at = CURRENT_TIMESTAMP 
       WHERE connection_id = ?\,
      [connectionId]
    );
  }
}

// Get usage for API response
async function getOllamaUsage(connectionId, db) {
  const row = await db.get(
    'SELECT * FROM ollama_usage WHERE connection_id = ?',
    [connectionId]
  );

  if (!row) {
    // Initialize if not exists
    await db.run(
      \INSERT INTO ollama_usage (connection_id, request_count, daily_limit) 
       VALUES (?, 0, 1000)\,
      [connectionId]
    );
    return {
      usageCount: 0,
      limitCount: 1000,
      usageResetAt: calculateDailyReset().toISOString()
    };
  }

  return {
    usageCount: row.request_count,
    limitCount: row.daily_limit,
    usageResetAt: calculateDailyReset(new Date(row.last_reset_at)).toISOString()
  };
}

function calculateDailyReset(lastReset = new Date()) {
  const tomorrow = new Date(lastReset);
  tomorrow.setDate(tomorrow.getDate() + 1);
  tomorrow.setHours(0, 0, 0, 0);
  return tomorrow;
}
```

## Kimchi

### API Documentation
- Check [Kimchi Dashboard](https://app.kimchi.dev/)

### Node.js Implementation

```javascript
async function fetchKimchiUsage(accessToken) {
  // Replace with actual Kimchi API endpoint
  const response = await fetch('https://api.kimchi.dev/v1/usage', {
    method: 'GET',
    headers: {
      'Authorization': \Bearer \\,
      'Content-Type': 'application/json'
    }
  });

  if (!response.ok) {
    throw new Error(\Kimchi API error: \\);
  }

  const json = await response.json();

  // Adjust based on actual Kimchi API response
  return {
    usageCount: json.usage || 0,
    limitCount: json.limit || 1000,
    usageResetAt: json.reset_at || calculateMonthlyReset().toISOString()
  };
}
```

## Complete Integration Example

### Main Usage Fetcher

```javascript
class UsageTracker {
  constructor(db) {
    this.db = db;
    this.cache = new Map();
    this.cacheTTL = 10 * 60 * 1000; // 10 minutes
  }

  async getUsageForConnection(connection) {
    // Check cache first
    const cached = this.cache.get(connection.id);
    if (cached && Date.now() - cached.timestamp < this.cacheTTL) {
      return cached.data;
    }

    // Fetch fresh data
    let usageData;
    try {
      switch (connection.provider) {
        case 'openrouter':
          usageData = await fetchOpenRouterUsage(connection.apiKey);
          break;
        case 'codex':
          usageData = await fetchCodexUsage(connection.apiKey);
          break;
        case 'kiro':
          usageData = await fetchKiroUsage(connection.apiKey);
          break;
        case 'ollama':
          usageData = await getOllamaUsage(connection.id, this.db);
          break;
        case 'kimchi':
          usageData = await fetchKimchiUsage(connection.accessToken);
          break;
        default:
          usageData = null;
      }

      // Cache the result
      if (usageData) {
        this.cache.set(connection.id, {
          data: usageData,
          timestamp: Date.now()
        });
      }

      return usageData;
    } catch (error) {
      console.error(\Failed to fetch usage for \:\, error);
      return null; // Return null so connection is still shown without usage
    }
  }

  async enrichConnectionsWithUsage(connections) {
    // Fetch usage for all connections in parallel
    const enriched = await Promise.all(
      connections.map(async (conn) => {
        const usage = await this.getUsageForConnection(conn);
        return {
          ...conn,
          ...(usage || {})
        };
      })
    );

    return enriched;
  }

  clearCache() {
    this.cache.clear();
  }
}
```

### Express Route Handler

```javascript
const express = require('express');
const app = express();
const usageTracker = new UsageTracker(db);

app.get('/api/providers', async (req, res) => {
  try {
    // Get base connections from database
    const connections = await db.getAllConnections();

    // Enrich with usage data
    const enriched = await usageTracker.enrichConnectionsWithUsage(connections);

    res.json({ connections: enriched });
  } catch (error) {
    console.error('Error fetching providers:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

// Optional: Manual cache refresh endpoint
app.post('/api/providers/refresh-usage', async (req, res) => {
  usageTracker.clearCache();
  res.json({ success: true, message: 'Usage cache cleared' });
});
```

## Error Handling

### Graceful Degradation

```javascript
async function safeGetUsage(fetcher, connectionId) {
  try {
    const usage = await fetcher();
    return usage;
  } catch (error) {
    console.error(\Usage fetch failed for \:\, error.message);
    
    // Log but don't fail the entire request
    // RouterPlus will simply not show usage for this connection
    return null;
  }
}
```

### Rate Limit Handling

```javascript
class RateLimitedFetcher {
  constructor(maxRequestsPerMinute = 10) {
    this.requests = [];
    this.maxRequests = maxRequestsPerMinute;
  }

  async fetch(url, options) {
    await this.waitIfNeeded();
    this.requests.push(Date.now());
    return fetch(url, options);
  }

  async waitIfNeeded() {
    const now = Date.now();
    const oneMinuteAgo = now - 60000;
    
    // Remove old requests
    this.requests = this.requests.filter(time => time > oneMinuteAgo);

    if (this.requests.length >= this.maxRequests) {
      const oldestRequest = this.requests[0];
      const waitTime = 60000 - (now - oldestRequest);
      await new Promise(resolve => setTimeout(resolve, waitTime));
    }
  }
}
```

## Testing

### Mock Data for Development

```javascript
// For testing without real API calls
const MOCK_USAGE_DATA = {
  'openrouter-1': {
    usageCount: 8500,
    limitCount: 10000,
    usageResetAt: '2026-08-27T00:00:00Z'
  },
  'codex-1': {
    usageCount: 1200000,
    limitCount: 5000000,
    usageResetAt: '2026-09-01T00:00:00Z'
  },
  'kiro-1': {
    usageCount: 450,
    limitCount: 500,
    usageResetAt: '2026-08-24T00:00:00Z'
  }
};

function getMockUsage(connectionId) {
  return MOCK_USAGE_DATA[connectionId] || null;
}
```

### Test Endpoint

```javascript
app.get('/api/providers/test', (req, res) => {
  res.json({
    connections: [
      {
        id: 'openrouter-test',
        provider: 'openrouter',
        name: 'Test Profile',
        priority: 10,
        isActive: true,
        testStatus: 'ok',
        ...getMockUsage('openrouter-1')
      },
      {
        id: 'codex-test',
        provider: 'codex',
        name: 'Test Profile',
        priority: 5,
        isActive: true,
        testStatus: 'ok',
        ...getMockUsage('codex-1')
      }
    ]
  });
});
```

## Next Steps

1. **Start with OpenRouter** - it has the most straightforward API
2. **Add caching** - use 10 minute TTL to avoid rate limits
3. **Test with mock data** - verify RouterPlus displays correctly
4. **Add other providers** - incrementally based on priority
5. **Monitor logs** - watch for API errors and rate limit issues

## Related Documentation

- [9Router Usage API Integration Guide](9router-usage-api-integration.md)
- [Usage Tracking Implementation](usage-tracking-implementation.md)
- [RouterPlus User Guide](user-guide.md)
