Add-Type -AssemblyName 'UIAutomationClient'
Add-Type -AssemblyName 'UIAutomationTypes'

# Find the RouterPlus window
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$window = $null
foreach ($w in $wins) {
    if ($w.Current.Name -like '*RouterPlus*' -or $w.Current.Name -like '*9Router*' -or $w.Current.ProcessId -eq (Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue).Id) {
        $window = $w
        break
    }
}
if (-not $window) {
    Write-Host 'ERROR: RouterPlus window not found'
    exit 1
}
Write-Host "Found window: $($window.Current.Name) PID: $($window.Current.ProcessId)"

# Find the Agnes card - look for "Tự động thêm" button
$all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesAutoAddBtn = $null
foreach ($ctrl in $all) {
    if ($ctrl.Current.Name -eq 'Tự động thêm' -and $ctrl.Current.ControlType.ProgrammaticName -eq 'ControlType.Button') {
        $agnesAutoAddBtn = $ctrl
        break
    }
}

if (-not $agnesAutoAddBtn) {
    Write-Host 'ERROR: Agnes "Tự động thêm" button not found'
    exit 1
}
Write-Host "Found Agnes 'Tự động thêm' button"

# Check IsEnabled
$isEnabled = $agnesAutoAddBtn.Current.IsEnabled
Write-Host "IsEnabled: $isEnabled"

if ($isEnabled) {
    # Invoke the button
    $invokePattern = $agnesAutoAddBtn.GetCurrentPattern([System.Windows.Automation.InvokePatternIdentifiers]::Pattern)
    if (-not $invokePattern) {
        Write-Host 'ERROR: InvokePattern not available on button'
        exit 1
    }
    $invokePattern.Invoke()
    Write-Host 'Invoked "Tự động thêm" button'
} else {
    Write-Host 'Button is disabled, not invoking'
}