# 9Router Usage Quota - Phân tích tình hình hiện tại

**Ngày:** 2026-08-22  
**Vấn đề:** RouterPlus không hiển thị được usage quota dù 9Router dashboard có quota page

## Phát hiện từ điều tra

### ✅ RouterPlus Frontend - SẴN SÀNG 100%

RouterPlus đã implement đầy đủ:
- Parse `usageCount`, `limitCount`, `usageResetAt` từ API
- UI hiển thị usage bars, percentages, warnings
- Color-coded status (green/orange/red)
- Tooltips với detailed information

### ❌ 9Router Backend - THIẾU USAGE DATA

Kiểm tra `GET http://localhost:20128/api/providers`:

**Các field có sẵn:**
```
- id, provider, name, priority, isActive
- testStatus, errorCode, lastError, lastErrorAt
- createdAt, updatedAt, email, authType
- backoffLevel, providerSpecificData
- modelLock_* (various model locks)
```

**Các field THIẾU (RouterPlus cần):**
```
- usageCount (KHÔNG CÓ)
- limitCount (KHÔNG CÓ)
- usageResetAt (KHÔNG CÓ)
```

### 🔍 Usage Info ẨN trong Error Messages

Backend **ĐÃ BIẾT** về usage limits từ error messages:

**Codex:**
```
lastError: "[429]: The usage limit has been reached"
errorCode: 429
```

**Kiro:**
```
lastError: "[402]: {\"message\":\"You have reached the limit.\",\"reason\":\"MONTHLY_REQUEST_COUNT\"}"
errorCode: 402
```

**Ollama:**
```
lastError: "[429]: {\"error\":\"you (hieuckphotos1) have reached your session usage limit, upgrade for higher limit"
errorCode: 429
```

**OpenRouter:**
```
lastError: "[402]: {\"error\":{\"message\":\"This request requires more credits, or fewer max_tokens. You requested u"
errorCode: 402
```

**Kimchi:**
```
lastError: "[402]: {\"error\": \"the provider for model kimi-k2.7 has exhausted its credits and cannot process requ"
errorCode: 402
```

### 📊 Dashboard Page Tồn tại

Dashboard có route: `http://localhost:20128/dashboard/quota`

Nhưng **KHÔNG TÌM THẤY** API endpoint tương ứng:
- `/api/usage` → 404
- `/api/quota` → 404
- `/api/providers/usage` → Error: Connection not found

## Giả thuyết về Dashboard Implementation

### Khả năng 1: Dashboard Client-Side Parsing
Dashboard có thể parse `lastError` messages để extract usage info.

**Ví dụ:**
```javascript
function parseUsageFromError(lastError) {
  if (lastError.includes('usage limit has been reached')) {
    return { isAtLimit: true };
  }
  if (lastError.includes('You have reached the limit')) {
    return { isAtLimit: true };
  }
  return null;
}
```

### Khả năng 2: Dashboard Fetch riêng từ Provider APIs
Dashboard có thể gọi trực tiếp API của từng provider (OpenRouter, Codex, etc.) từ client-side để lấy usage.

### Khả năng 3: Internal API không public
9Router có thể có internal API endpoint chỉ dashboard biết, chưa được documented.

## Giải pháp đề xuất

### 🎯 Giải pháp 1: Thêm Usage Fields vào `/api/providers` (KHUYẾN NGHỊ)

**Backend cần:**

1. Track usage khi proxy requests đi qua 9Router
2. Fetch usage từ provider APIs định kỳ
3. Store usage data trong database
4. Trả về trong `/api/providers` response

**Example implementation:**

```javascript
// Khi nhận response từ provider
async function handleProviderResponse(connectionId, response) {
  // Extract usage từ provider response headers/body
  const usage = extractUsageFromResponse(response);
  
  if (usage) {
    await db.updateUsage(connectionId, {
      usageCount: usage.count,
      limitCount: usage.limit,
      usageResetAt: usage.resetAt
    });
  }
}

// Trong /api/providers endpoint
app.get('/api/providers', async (req, res) => {
  const connections = await db.getAllConnections();
  
  const enriched = connections.map(conn => ({
    ...conn,
    usageCount: conn.usageCount || null,
    limitCount: conn.limitCount || null,
    usageResetAt: conn.usageResetAt || null
  }));
  
  res.json({ connections: enriched });
});
```

**Database migration:**

```sql
ALTER TABLE connections 
ADD COLUMN usageCount BIGINT DEFAULT NULL,
ADD COLUMN limitCount BIGINT DEFAULT NULL,
ADD COLUMN usageResetAt TIMESTAMP DEFAULT NULL;
```

### 🔧 Giải pháp 2: Parse từ Provider Response Headers

Nhiều provider trả về usage trong response headers:

**OpenRouter Headers:**
```
x-ratelimit-requests-limit: 10000
x-ratelimit-requests-remaining: 1500
x-ratelimit-requests-reset: 1724486400
```

**Implementation:**
```javascript
function extractUsageFromHeaders(provider, headers) {
  if (provider === 'openrouter') {
    const limit = parseInt(headers['x-ratelimit-requests-limit']);
    const remaining = parseInt(headers['x-ratelimit-requests-remaining']);
    const reset = parseInt(headers['x-ratelimit-requests-reset']);
    
    return {
      usageCount: limit - remaining,
      limitCount: limit,
      usageResetAt: new Date(reset * 1000).toISOString()
    };
  }
  // Add other providers...
}
```

### 🛠️ Giải pháp 3: Background Worker Sync Usage

```javascript
// Chạy mỗi 10 phút
setInterval(async () => {
  const connections = await db.getActiveConnections();
  
  for (const conn of connections) {
    try {
      const usage = await fetchUsageFromProvider(conn);
      await db.updateUsage(conn.id, usage);
    } catch (error) {
      console.error(\Failed to fetch usage for \\, error);
    }
  }
}, 10 * 60 * 1000);
```

### ⚡ Giải pháp 4: Parse từ `lastError` (Quick Fix)

Nếu không thể fetch usage từ provider APIs, có thể parse từ error messages:

```javascript
function inferUsageFromError(lastError, errorCode) {
  if (errorCode === 429 || errorCode === 402) {
    if (lastError.includes('usage limit') || 
        lastError.includes('reached the limit') ||
        lastError.includes('exhausted')) {
      return {
        usageCount: 100,  // Assume 100%
        limitCount: 100,
        usageResetAt: null  // Unknown
      };
    }
  }
  return null;
}
```

**Nhược điểm:**
- Không có số chính xác
- Không biết reset time
- Chỉ biết khi đã over limit

## Kiểm tra Dashboard Source

Để hiểu dashboard lấy data từ đâu:

### Option 1: Xem Network Tab
1. Mở Chrome DevTools
2. Vào tab Network
3. Mở `http://localhost:20128/dashboard/quota`
4. Xem request nào được gọi

### Option 2: Xem dashboard source code
Nếu có access vào 9Router source:
```bash
# Tìm file quota page
find . -name "*quota*" -type f

# Tìm API calls trong dashboard
grep -r "usageCount\|limitCount\|quota" dashboard/
grep -r "/api/" dashboard/src/
```

### Option 3: Intercept API calls
```bash
# Monitor all HTTP requests from 9Router
netstat -ano | findstr :20128
```

## Next Steps - Action Items

### Cho Backend Developer:

1. **Khám phá dashboard implementation** - Xem dashboard lấy data từ đâu
2. **Check internal APIs** - Có endpoint nào chưa document?
3. **Implement usage tracking** - Theo 1 trong 4 giải pháp trên
4. **Add fields to API response** - Thêm usageCount, limitCount, usageResetAt
5. **Test với RouterPlus** - Verify UI hiển thị đúng

### Cho RouterPlus (không cần làm gì):

✅ Frontend đã sẵn sàng  
✅ Chỉ cần backend trả data đúng format

## Testing Checklist

Sau khi backend implement:

- [ ] `curl http://localhost:20128/api/providers` trả về usage fields
- [ ] RouterPlus sync và hiển thị usage bars
- [ ] Usage percentage tính đúng
- [ ] Warning màu cam khi >= 80%
- [ ] Error màu đỏ khi >= 100%
- [ ] Tooltip hiển thị reset time

## Contact & References

- **RouterPlus Parsing Code:** `src/RouterPlus.Infrastructure/Router/RouterApiClient.cs` (line 280-295)
- **UI Implementation:** `src/RouterPlus.App/ViewModels/ProviderCardViewModel.cs`
- **Integration Guide:** `docs/9router-usage-api-integration.md`
- **Code Examples:** `docs/provider-usage-api-examples.md`

---

**Kết luận:** Backend 9Router cần thêm 3 fields (usageCount, limitCount, usageResetAt) vào response của `/api/providers`. RouterPlus đã sẵn sàng nhận và hiển thị data này.
