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
        if ($itemName -eq "hieuck.adobe2022@gmail.com") {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
            if ($parent) {
                $selPattern = $parent.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($selPattern) { $selPattern.Select() }
            }
        }
    }
}
Start-Sleep -Seconds 2

# Find Agnes card
$namesToTry = @("Agnes AI", "Agnes", "AG")
foreach ($name in $namesToTry) {
    $cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, $name)
    $agnesCard = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($agnesCard) {
        Write-Host ("Found Agnes card: '{0}' Type='{1}'" -f $agnesCard.Current.Name, $agnesCard.Current.ControlType.ProgrammaticName)
        $patterns = $agnesCard.GetSupportedPatterns()
        Write-Host ("  Patterns: {0}" -f $patterns.Count)
        foreach ($p in $patterns) {
            Write-Host ("    - {0}" -f $p.ProgrammaticName)
        }
        
        # Check parent
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($agnesCard)
        if ($parent) {
            Write-Host ("  Parent: Name='{0}' Type='{1}'" -f $parent.Current.Name, $parent.Current.ControlType.ProgrammaticName)
            $parentPatterns = $parent.GetSupportedPatterns()
            Write-Host ("  Parent Patterns: {0}" -f $parentPatterns.Count)
            foreach ($pp in $parentPatterns) {
                Write-Host ("    - {0}" -f $pp.ProgrammaticName)
            }
            
            # Check grandparent
            $grandparent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            if ($grandparent) {
                Write-Host ("  Grandparent: Name='{0}' Type='{1}'" -f $grandparent.Current.Name, $grandparent.Current.ControlType.ProgrammaticName)
                $gpPatterns = $grandparent.GetSupportedPatterns()
                Write-Host ("  Grandparent Patterns: {0}" -f $gpPatterns.Count)
                foreach ($gpp in $gpPatterns) {
                    Write-Host ("    - {0}" -f $gpp.ProgrammaticName)
                }
            }
        }
        break
    }
}