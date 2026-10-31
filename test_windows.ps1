Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find all windows
Write-Host "=== All Windows ==="
foreach ($elem in $all) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        Write-Host "Window: Name='$($elem.Current.Name)' Class='$($elem.Current.ClassName)'"
    }
}

Write-Host "Done"