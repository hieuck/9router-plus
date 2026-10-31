Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Search for profile
$searchBoxCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCond)
Write-Host "SearchBox found: " + ($searchBox -ne $null)
if ($searchBox) {
    $valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("hieuck.adobe2022@gmail.com")
        Write-Host "Set search text"
    }
}
Start-Sleep -Seconds 2

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)
Write-Host "ProfileList found: " + ($profileList -ne $null)
if ($profileList) {
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host ("Items after search: {0}" -f $items.Count)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        Write-Host ("  Item {0}: Name='{1}' Type='{2}'" -f $i, $itemName, $item.Current.ControlType.ProgrammaticName)
    }
    
    $itemCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com")
    $profileItem = $profileList.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
    Write-Host ("ProfileItem found: {0}" -f ($profileItem -ne $null))
    if ($profileItem) {
        Write-Host ("  Name: {0}" -f $profileItem.Current.Name)
        $patterns = $profileItem.GetSupportedPatterns()
        Write-Host ("  Patterns: {0}" -f $patterns.Count)
        foreach ($p in $patterns) {
            Write-Host ("    - {0}" -f $p.ProgrammaticName)
        }
    }
}