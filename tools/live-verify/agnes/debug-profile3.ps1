Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Search for profile
$searchBoxCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCond)
if ($searchBox) {
    $valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("hieuck.adobe2022@gmail.com")
    }
}
Start-Sleep -Seconds 2

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)
if ($profileList) {
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        Write-Host ("Item {0}: Name='{1}' Type='{2}'" -f $i, $itemName, $item.Current.ControlType.ProgrammaticName)
        
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        for ($j = 0; $j -lt $children.Count; $j++) {
            $child = $children[$j]
            $childName = $child.Current.Name
            $childAutoId = $child.Current.AutomationId
            if ($childName -or $childAutoId) {
                Write-Host ("  Child {0}: Name='{1}' AutoId='{2}' Type='{3}'" -f $j, $childName, $childAutoId, $child.Current.ControlType.ProgrammaticName)
                $patterns = $child.GetSupportedPatterns()
                if ($patterns.Count -gt 0) {
                    foreach ($p in $patterns) {
                        Write-Host ("      Pattern: {0}" -f $p.ProgrammaticName)
                    }
                }
            }
        }
    }
}