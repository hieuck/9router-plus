Add-Type -AssemblyName 'UIAutomationClient'
Add-Type -AssemblyName 'UIAutomationTypes'

$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$window = $null
foreach ($w in $wins) {
    if ($w.Current.Name -like '*RouterPlus*' -or $w.Current.Name -like '*9Router*' -or $w.Current.ProcessId -eq (Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue).Id) {
        $window = $w
        break
    }
}
if (-not $window) { Write-Host 'ERROR: Window not found'; exit 1 }
Write-Host "Found: $($window.Current.Name)"

$all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$btn = $null
foreach ($c in $all) {
    if ($c.Current.Name -eq 'Tự động thêm' -and $c.Current.ControlType.ProgrammaticName -eq 'ControlType.Button') {
        $btn = $c; break
    }
}
if (-not $btn) { Write-Host 'ERROR: Button not found'; exit 1 }
Write-Host "Found button"
Write-Host "IsEnabled: $($btn.Current.IsEnabled)"
if ($btn.Current.IsEnabled) {
    $ip = $btn.GetCurrentPattern([System.Windows.Automation.InvokePatternIdentifiers]::Pattern)
    $ip.Invoke()
    Write-Host 'Invoked'
} else { Write-Host 'Disabled' }