Add-Type -AssemblyName 'UIAutomationClient'
Add-Type -AssemblyName 'UIAutomationTypes'

# Find the RouterPlus window
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$window = $null
foreach ($w in $wins) {
    if ($w.Current.Name -like '*RouterPlus*' -or $w.Current.ProcessId -eq (Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue).Id) {
        $window = $w
        break
    }
}
if (-not $window) {
    Write-Host 'ERROR: RouterPlus window not found'
    exit 1
}
Write-Host "Found window: $($window.Current.Name) PID: $($window.Current.ProcessId)"

# Find all descendant controls
$all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($ctrl in $all) {
    $name = $ctrl.Current.Name
    $autoId = $ctrl.Current.AutomationId
    $type = $ctrl.Current.ControlType.ProgrammaticName
    if ($name -or $autoId) {
        Write-Host "Type: $type | Name: '$name' | AutoId: '$autoId'"
    }
}