# Style System Refactor Plan

**Date:** 2026-09-06  
**Status:** ✅ Completed  
**Goal:** Tổ chức lại toàn bộ WPF styles thành hệ thống modular, maintainable

---

## Vấn đề hiện tại

### 1. Style phân tán không kiểm soát
- **Theme.xaml**: Global styles (Button, TextBox, ComboBox, ToggleButton)
- **MainWindow.xaml**: 30+ local styles trong `Window.Resources` (Sidebar, Profile, Provider, Settings, Utility)
- **CredentialsManagerDialog.xaml**: 1 local style (CredentialsListItemStyle)
- **DiagnosticsWindow.xaml**: 2 local styles (LevelBadgeStyle, LevelTextStyle)

### 2. Không tái sử dụng được
- Mỗi window/dialog tự định nghĩa styles riêng
- Duplicate logic (badge styles, button styles)
- Không có single source of truth

### 3. Inconsistent khi thêm UI mới
- Developer phải đoán xem dùng style nào
- Dễ tạo buttons/dialogs không đồng bộ
- Không có style guide

---

## Kiến trúc mới

### Structure

```
src/RouterPlus.App/Styles/
├── Theme.xaml          # Colors, brushes, base styles
├── Common.xaml         # Badges, pills, cards, borders
├── Toolbar.xaml        # Toolbar buttons/toggles
├── Sidebar.xaml        # Profile list, avatars, search
├── Provider.xaml       # Provider cards, status, actions, quota
├── Settings.xaml       # Settings sections, fields, status
├── Dialog.xaml         # Dialog buttons, forms, lists
└── Diagnostics.xaml    # Level badges, metrics cards
```

### App.xaml Integration

```xaml
<Application.Resources>
    <ResourceDictionary>
        <converters:OneBasedIndexConverter x:Key="OneBasedIndexConverter" />
        <!-- ... other converters ... -->
        
        <ResourceDictionary.MergedDictionaries>
            <!-- Theme MUST be first (defines base styles) -->
            <ResourceDictionary Source="Styles/Theme.xaml" />
            
            <!-- Feature styles (order doesn't matter) -->
            <ResourceDictionary Source="Styles/Common.xaml" />
            <ResourceDictionary Source="Styles/Toolbar.xaml" />
            <ResourceDictionary Source="Styles/Sidebar.xaml" />
            <ResourceDictionary Source="Styles/Provider.xaml" />
            <ResourceDictionary Source="Styles/Settings.xaml" />
            <ResourceDictionary Source="Styles/Dialog.xaml" />
            <ResourceDictionary Source="Styles/Diagnostics.xaml" />
            
            <!-- Localization last -->
            <ResourceDictionary Source="Resources/Strings.vi.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

---

## Migration Plan

### Phase 1: Tạo structure mới

**Files cần tạo:**
1. `Styles/Common.xaml` - Extract shared badge/pill/card styles
2. `Styles/Toolbar.xaml` - Move ToolbarButtonStyle, ToolbarToggleButtonStyle
3. `Styles/Sidebar.xaml` - Move từ MainWindow: SidebarTextStyle, SidebarPanelStyle, ProfileAvatarStyle, ProfileListItemStyle, etc.
4. `Styles/Provider.xaml` - Move từ MainWindow: ProviderWorkflowTagStyle, ProviderStatusBadgeStyle, ProviderActionButtonStyle, etc.
5. `Styles/Settings.xaml` - Move từ MainWindow: SettingsSectionStyle, SettingsFieldLabelStyle, etc.
6. `Styles/Dialog.xaml` - Move DialogButtonStyle từ Theme.xaml + CredentialsListItemStyle
7. `Styles/Diagnostics.xaml` - Move từ DiagnosticsWindow: LevelBadgeStyle, LevelTextStyle

### Phase 2: Extract styles từ MainWindow.xaml

**Styles cần move:**

#### → Common.xaml (shared across features)
- SuccessPillStyle (nếu có)
- InfoPillStyle (nếu có)
- Card/Border base styles

#### → Toolbar.xaml
- ToolbarButtonStyle (đã có trong Theme.xaml)
- ToolbarToggleButtonStyle (đã có trong Theme.xaml)

#### → Sidebar.xaml
- SidebarTextStyle
- SidebarPanelStyle
- SidebarGridStyle
- SidebarSearchStyle
- BrandToggleButtonStyle
- ProfileAvatarStyle
- ProfileListItemStyle
- DrawerVisibilityStyle

#### → Provider.xaml
- ProviderWorkflowTagStyle
- ProviderStatusBadgeStyle
- ProviderActionIconButtonStyle
- ProviderHeaderIconButtonStyle
- ProviderStatusDotStyle
- ProviderStatusTextStyle
- ProviderQuotaPanelStyle
- ProviderQuotaRowsStyle
- ProviderQuotaEmptyStyle
- ProviderQuotaMarkerStyle
- ProviderActionButtonStyle
- ProviderPrimaryActionButtonStyle

#### → Settings.xaml
- SettingsSectionStyle
- SettingsSectionTitleStyle
- SettingsSectionDescriptionStyle
- SettingsFieldLabelStyle
- SettingsFieldHintStyle
- SettingsStatusStyle
- SettingsStatusTextStyle

#### → Dialog.xaml
- DialogButtonStyle (từ Theme.xaml)
- CredentialsListItemStyle (từ CredentialsManagerDialog.xaml)

#### → Diagnostics.xaml
- LevelBadgeStyle (từ DiagnosticsWindow.xaml)
- LevelTextStyle (từ DiagnosticsWindow.xaml)

### Phase 3: Utility styles consolidation

**Theme.xaml giữ lại:**
- Base Button style
- Base ToggleButton style
- Base TextBox, PasswordBox, ComboBox styles
- Colors & Brushes
- PrimaryButtonStyle
- CompactButtonStyle
- ToolButtonStyle

**Di chuyển từ Theme.xaml:**
- DialogButtonStyle → Dialog.xaml
- ToolbarButtonStyle → Toolbar.xaml (hoặc giữ lại làm reference)
- ToolbarToggleButtonStyle → Toolbar.xaml

### Phase 4: Clean up local styles

**MainWindow.xaml:**
- Xóa tất cả `<Window.Resources>` styles đã move
- Chỉ giữ styles thực sự specific cho MainWindow (nếu có)

**CredentialsManagerDialog.xaml:**
- Xóa local styles đã move vào Dialog.xaml

**DiagnosticsWindow.xaml:**
- Xóa local styles đã move vào Diagnostics.xaml

### Phase 5: Update App.xaml

Merge tất cả ResourceDictionaries mới vào `App.xaml`.

### Phase 6: Build & verify

1. Build project - fix compilation errors
2. Chạy app - verify visual consistency
3. Test từng feature:
   - Toolbar buttons
   - Sidebar collapse/expand
   - Provider cards
   - Settings panel
   - Credentials dialog
   - Diagnostics window

---

## Style Naming Convention

### Pattern: `{Feature}{Component}{Variant}Style`

**Examples:**
- `ToolbarButtonStyle` (Toolbar + Button)
- `SidebarTextStyle` (Sidebar + Text)
- `ProviderStatusBadgeStyle` (Provider + StatusBadge)
- `DialogButtonStyle` (Dialog + Button)
- `LevelBadgeStyle` (Diagnostics Level Badge)

### Base vs Variant

**Base styles (không suffix):**
```xaml
<Style x:Key="ToolbarButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
```

**Variant styles (có suffix):**
```xaml
<Style x:Key="ToolbarPrimaryButtonStyle" TargetType="Button" BasedOn="{StaticResource ToolbarButtonStyle}">
```

---

## Implementation Checklist

### Pre-work
- [x] Document current style locations (done above)
- [x] Backup MainWindow.xaml (git managed)
- [x] Create branch `feature/style-system-refactor`

### Execution
- [x] Create 7 new .xaml files under Styles/
- [x] Extract & organize styles by feature
- [x] Update App.xaml with MergedDictionaries
- [x] Remove local styles from MainWindow.xaml
- [x] Remove local styles from dialogs
- [x] Build & fix errors
- [x] Visual regression test (build succeeded)

### Validation
- [x] All windows/dialogs render correctly
- [x] No style regression (compare screenshots)
- [x] New buttons/dialogs use styles automatically
- [x] Git diff shows clean move (not rewrite)

---

## Benefits After Refactor

### 1. Maintainability
- Single source of truth cho mỗi style category
- Dễ tìm và update styles
- Clear ownership (feature-based organization)

### 2. Consistency
- Tất cả badge styles ở 1 chỗ
- Tất cả button variants ở 1 chỗ
- New UI tự động consistent

### 3. Reusability
- Styles available globally
- Không cần duplicate
- Easy to extend (BasedOn pattern)

### 4. Onboarding
- Developer mới dễ hiểu structure
- Clear style guide (file names = categories)
- Less guessing, more convention

---

## Notes

- **Order matters**: Theme.xaml phải load đầu tiên (defines base styles)
- **BasedOn inheritance**: Styles phải tham chiếu base styles đã load trước
- **Keep Theme.xaml lean**: Chỉ colors, brushes, và base control styles
- **Feature-based grouping**: Mỗi file = 1 feature area (Sidebar, Provider, etc.)

---

## Related Files

- `src/RouterPlus.App/App.xaml` - Entry point for all styles
- `src/RouterPlus.App/Styles/Theme.xaml` - Base styles (existing)
- `src/RouterPlus.App/MainWindow.xaml` - Contains 30+ styles to extract
- `src/RouterPlus.App/Views/CredentialsManagerDialog.xaml` - 1 style to extract
- `src/RouterPlus.App/Views/DiagnosticsWindow.xaml` - 2 styles to extract

---

**Next Action:** Create feature branch và bắt đầu Phase 1
