Write-Host "Test started"
$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
Write-Host "HWND: $hwnd"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
Write-Host "Root: $($automation.Current.Name)"

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    Write-Host "Found ProfileList"
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Children count: $($children.Count)"
} else {
    Write-Host "ProfileList NOT found"
}
Write-Host "Test completed"