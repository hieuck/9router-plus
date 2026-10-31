$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
Add-Type -AssemblyName UIAutomationClient
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
Write-Host "From MainWindowHandle: Name='$($automation.Current.Name)' Class='$($automation.Current.ClassName)'"

# Try to find all top-level windows
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ProcessIdProperty, $proc.Id)
$windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
Write-Host "Windows for PID $($proc.Id): $($windows.Count)"
for ($i=0; $i -lt $windows.Count; $i++) {
    $w = $windows[$i]
    Write-Host "  [$i] Name='$($w.Current.Name)' Class='$($w.Current.ClassName)' Handle=$($w.Current.NativeWindowHandle)"
}