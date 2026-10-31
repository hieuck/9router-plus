Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)
$items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $items.Count; $i++) {
    $item = $items[$i]
    $itemName = $item.Current.Name
    Write-Host ("Item {0}: Name='{1}' Type='{2}'" -f $i, $itemName, $item.Current.ControlType.ProgrammaticName)
    $patterns = $item.GetSupportedPatterns()
    Write-Host ("  Patterns: {0}" -f $patterns.Count)
    foreach ($p in $patterns) {
        Write-Host ("    - {0}" -f $p.ProgrammaticName)
    }
}