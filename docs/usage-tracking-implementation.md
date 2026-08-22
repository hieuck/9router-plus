# Tóm tắt: Thêm hiển thị theo dõi hạn mức cho từng profile từng provider

## Các thay đổi đã thực hiện:

### 1. ProviderConnection Model (src/RouterPlus.Core/Providers/ProviderConnection.cs)
- Thêm 3 trường mới:
  - UsageCount (long?): Số lượng đã sử dụng
  - LimitCount (long?): Giới hạn tối đa
  - UsageResetAt (DateTimeOffset?): Thời gian reset hạn mức
- Thêm các properties tính toán:
  - HasUsageData: Kiểm tra có dữ liệu usage
  - UsagePercentage: Tính % sử dụng
  - IsNearLimit: Cảnh báo khi >= 80%
  - IsOverLimit: Cảnh báo khi >= 100%

### 2. RouterApiClient (src/RouterPlus.Infrastructure/Router/RouterApiClient.cs)
- Cập nhật ParseConnection() để parse 3 trường mới từ API response:
  - usageCount (JSON number → long?)
  - limitCount (JSON number → long?)
  - usageResetAt (JSON string → DateTimeOffset?)

### 3. ProfileProviderStatusViewModel (src/RouterPlus.App/ViewModels/ProfileRowViewModel.cs)
- Thêm các trường private: _usageCount, _limitCount, _usageResetAt
- Thêm các properties public:
  - UsageCount, LimitCount, UsageResetAt
  - HasUsageData, UsagePercentage, IsNearLimit, IsOverLimit
  - UsageText: Hiển thị dạng "1.2K/10K" hoặc "500/1K"
- Cập nhật SetConnectionCount() nhận thêm 3 tham số usage
- Cập nhật ToolTip để hiển thị thông tin usage
- Thêm helper methods:
  - FormatNumber(): Format số dạng K/M
  - FormatResetTime(): Format thời gian reset
  - FormatUsageForTooltip(): Format usage cho tooltip

### 4. ProfileRowViewModel (src/RouterPlus.App/ViewModels/ProfileRowViewModel.cs)
- Cập nhật UpdateConnections() để tổng hợp usage từ nhiều connections:
  - Tính tổng UsageCount từ tất cả connections matching
  - Tính tổng LimitCount từ tất cả connections matching
  - Lấy UsageResetAt sớm nhất

### 5. MainWindow.xaml (src/RouterPlus.App/MainWindow.xaml)
- Cập nhật ItemsControl hiển thị ProviderStatuses:
  - Thay đổi từ chỉ Ellipse sang StackPanel với Ellipse + TextBlock
  - Ellipse thay đổi màu dựa trên IsNearLimit/IsOverLimit
  - TextBlock hiển thị UsageText với các trigger:
    - Visible khi HasUsageData = true
    - Màu cam (#FFA500) khi IsNearLimit = true
    - Màu đỏ (DangerBrush) khi IsOverLimit = true

## Cách hoạt động:

1. API trả về dữ liệu usage/limit cho mỗi connection
2. RouterApiClient parse dữ liệu vào ProviderConnection
3. ProfileRowViewModel tổng hợp usage từ tất cả connections của cùng profile + provider
4. ProfileProviderStatusViewModel tính toán % và xác định trạng thái (near/over limit)
5. UI hiển thị:
   - Ellipse: Màu xanh (healthy), cam (near limit), đỏ (over limit/error)
   - TextBlock: Hiển thị "usage/limit" khi có dữ liệu
   - Tooltip: Hiển thị chi tiết usage, %, và thời gian reset

## Ví dụ hiển thị:
- "1.2K/10K" (12% sử dụng)
- "8.5K/10K" (85% - màu cam, cảnh báo gần hết hạn)
- "11K/10K" (110% - màu đỏ, vượt hạn mức)
- Tooltip: "OpenRouter: OK · 2 connection tên theo profile\nSử dụng: 8.5K/10K (85.0%) · reset sau 5 ngày"

